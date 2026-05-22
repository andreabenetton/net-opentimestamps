using OpenTimestamps.Attestations;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;

namespace OpenTimestamps;

/// <summary>
/// A node in a timestamp proof tree. Each node carries a <c>msg</c>, zero or
/// more <see cref="Attestations"/> attached to that <c>msg</c>, and a map of
/// outgoing operations (with their resulting child timestamps).
/// </summary>
/// <remarks>
/// Mirrors <c>opentimestamps/core/timestamp.py</c>. A leaf of the tree is a
/// node whose attestation set is non-empty and whose ops dictionary is empty —
/// every path through the tree must terminate in at least one attestation.
/// </remarks>
public sealed class Timestamp
{
    /// <summary>The default recursion depth limit while parsing.</summary>
    public const int DefaultRecursionLimit = 256;

    private readonly byte[] _msg;

    public Timestamp(byte[] msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        _msg = (byte[])msg.Clone();
        Ops = [];
        Attestations = [];
    }

    /// <summary>The message at this node. The root's msg is the file digest under the file-hash op.</summary>
    public ReadOnlySpan<byte> Msg => _msg;

    /// <summary>A copy of the message bytes.</summary>
    public byte[] MsgArray() => (byte[])_msg.Clone();

    /// <summary>Outgoing operations from this node, each producing a child Timestamp.</summary>
    public Dictionary<Op, Timestamp> Ops { get; }

    /// <summary>Attestations attached to <see cref="Msg"/>.</summary>
    public HashSet<TimeAttestation> Attestations { get; }

    /// <summary>True if this node has neither attestations nor operations (invalid for serialization).</summary>
    public bool IsEmpty => Attestations.Count == 0 && Ops.Count == 0;

    /// <summary>
    /// Merge <paramref name="other"/> into this timestamp. Requires the two have
    /// the same <see cref="Msg"/>. Unions attestations and recursively merges ops.
    /// </summary>
    public void Merge(Timestamp other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!_msg.AsSpan().SequenceEqual(other._msg))
        {
            throw new InvalidOperationException(
                "Cannot merge timestamps for different messages together.");
        }

        foreach (TimeAttestation attestation in other.Attestations)
        {
            Attestations.Add(attestation);
        }

        foreach (KeyValuePair<Op, Timestamp> kvp in other.Ops)
        {
            if (!Ops.TryGetValue(kvp.Key, out Timestamp? existing))
            {
                existing = new Timestamp(kvp.Value._msg);
                Ops[kvp.Key] = existing;
            }

            existing.Merge(kvp.Value);
        }
    }

    /// <summary>Write this Timestamp subtree to the OTS wire format.</summary>
    public void Serialize(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot serialize an empty timestamp.");
        }

        TimeAttestation[] sortedAttestations = [.. Attestations];
        Array.Sort(sortedAttestations);

        // Emit every attestation except possibly the last one with the 0xFF 0x00 prefix.
        for (int i = 0; i < sortedAttestations.Length - 1; i++)
        {
            writer.WriteUInt8(0xFF);
            writer.WriteUInt8(0x00);
            sortedAttestations[i].Serialize(writer);
        }

        if (Ops.Count == 0)
        {
            // Final leaf is an attestation; no 0xFF prefix.
            writer.WriteUInt8(0x00);
            sortedAttestations[^1].Serialize(writer);
            return;
        }

        // We have ops, so the last attestation (if any) is still a non-final leaf.
        if (sortedAttestations.Length > 0)
        {
            writer.WriteUInt8(0xFF);
            writer.WriteUInt8(0x00);
            sortedAttestations[^1].Serialize(writer);
        }

        KeyValuePair<Op, Timestamp>[] sortedOps = [.. Ops];
        Array.Sort(sortedOps, static (a, b) => a.Key.CompareTo(b.Key));

        for (int i = 0; i < sortedOps.Length - 1; i++)
        {
            writer.WriteUInt8(0xFF);
            sortedOps[i].Key.Serialize(writer);
            sortedOps[i].Value.Serialize(writer);
        }

        sortedOps[^1].Key.Serialize(writer);
        sortedOps[^1].Value.Serialize(writer);
    }

    /// <summary>Read a Timestamp subtree from the wire, given the initial message at this node.</summary>
    public static Timestamp Deserialize(OtsReader reader, byte[] initialMsg)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(initialMsg);
        return DeserializeCore(reader, initialMsg, DefaultRecursionLimit);
    }

    private static Timestamp DeserializeCore(OtsReader reader, byte[] msg, int recursionLimit)
    {
        if (recursionLimit <= 0)
        {
            throw new RecursionLimitException();
        }

        var timestamp = new Timestamp(msg);
        byte tag = reader.ReadUInt8();
        while (tag == 0xFF)
        {
            byte inner = reader.ReadUInt8();
            HandleTag(reader, timestamp, inner, recursionLimit);
            tag = reader.ReadUInt8();
        }

        HandleTag(reader, timestamp, tag, recursionLimit);
        return timestamp;
    }

    private static void HandleTag(OtsReader reader, Timestamp timestamp, byte tag, int recursionLimit)
    {
        if (tag == 0x00)
        {
            TimeAttestation attestation = TimeAttestation.Deserialize(reader);
            timestamp.Attestations.Add(attestation);
            return;
        }

        Op op = Op.DeserializeFromTag(reader, tag);
        byte[] childMsg;
        try
        {
            childMsg = op.Call(timestamp.MsgArray());
        }
        catch (OpMessageException ex)
        {
            throw new DeserializationException(
                $"Invalid timestamp; message invalid for op {op}: {ex.Message}");
        }

        Timestamp child = DeserializeCore(reader, childMsg, recursionLimit - 1);
        timestamp.Ops[op] = child;
    }

    /// <summary>
    /// Enumerate every (commitment, attestation) pair in the subtree. The commitment
    /// is the <c>msg</c> at the node carrying the attestation.
    /// </summary>
    public IEnumerable<(byte[] Msg, TimeAttestation Attestation)> AllAttestations()
    {
        var stack = new Stack<Timestamp>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            Timestamp current = stack.Pop();
            foreach (TimeAttestation attestation in current.Attestations)
            {
                yield return (current.MsgArray(), attestation);
            }

            foreach (Timestamp child in current.Ops.Values)
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>Enumerate every node in the subtree, depth-first.</summary>
    public IEnumerable<Timestamp> AllNodes()
    {
        var stack = new Stack<Timestamp>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            Timestamp current = stack.Pop();
            yield return current;
            foreach (Timestamp child in current.Ops.Values)
            {
                stack.Push(child);
            }
        }
    }
}
