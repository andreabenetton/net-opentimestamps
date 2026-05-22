using System.Collections.Frozen;
using OpenTimestamps.Serialization;

namespace OpenTimestamps.Ops;

/// <summary>
/// A timestamp-proof operation. Operations transform a <c>msg</c> byte string
/// into another byte string, e.g. by hashing it, appending bytes, or hexlifying.
/// </summary>
/// <remarks>
/// Mirrors <c>opentimestamps/core/op.py</c>. Subclasses are pinned: the wire-format
/// registry is closed in the reference, so we do not allow dynamic registration.
/// </remarks>
public abstract class Op : IEquatable<Op>, IComparable<Op>
{
    /// <summary>Maximum bytes any op may produce.</summary>
    public const int MaxResultLength = 4096;

    /// <summary>Default maximum input length to any op.</summary>
    public const int DefaultMaxMessageLength = 4096;

    private static readonly FrozenDictionary<byte, Func<OtsReader, Op>> Registry = BuildRegistry();

    /// <summary>The single-byte tag this op uses on the wire.</summary>
    public abstract byte Tag { get; }

    /// <summary>The human-readable name of this op, e.g. <c>"sha256"</c>.</summary>
    public abstract string Name { get; }

    /// <summary>Maximum input length accepted by this op.</summary>
    public virtual int MaxMessageLength => DefaultMaxMessageLength;

    /// <summary>Apply the operation to the given message and return its output.</summary>
    /// <exception cref="OpMessageException">
    /// Thrown when the input length or output length is outside protocol bounds.
    /// </exception>
    public byte[] Call(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Length > MaxMessageLength)
        {
            throw new OpMessageException(
                $"Message too long for {Name}: {message.Length} > {MaxMessageLength}.");
        }

        byte[] result = DoCall(message);
        if (result.Length == 0)
        {
            throw new OpMessageException($"{Name} produced an empty result.");
        }

        if (result.Length > MaxResultLength)
        {
            throw new OpMessageException(
                $"{Name} result too long: {result.Length} > {MaxResultLength}.");
        }

        return result;
    }

    /// <summary>Subclass-specific evaluation; preconditions are already checked by <see cref="Call"/>.</summary>
    protected abstract byte[] DoCall(byte[] message);

    /// <summary>Write the op to the OTS wire format.</summary>
    public virtual void Serialize(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteUInt8(Tag);
    }

    /// <summary>
    /// Read a single op (including any operand bytes) from the wire.
    /// </summary>
    public static Op Deserialize(OtsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte tag = reader.ReadUInt8();
        return DeserializeFromTag(reader, tag);
    }

    /// <summary>Resolve and read an op whose tag byte has already been consumed.</summary>
    public static Op DeserializeFromTag(OtsReader reader, byte tag)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!Registry.TryGetValue(tag, out var factory))
        {
            throw new DeserializationException($"Unknown operation tag 0x{tag:x2}.");
        }

        return factory(reader);
    }

    /// <summary>Whether the given tag identifies a registered op.</summary>
    public static bool IsKnownTag(byte tag) => Registry.ContainsKey(tag);

    /// <summary>
    /// The argument bytes contributing to identity and ordering. Unary ops return empty.
    /// </summary>
    protected virtual ReadOnlySpan<byte> ArgumentBytes => [];

    public bool Equals(Op? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Tag == other.Tag && ArgumentBytes.SequenceEqual(other.ArgumentBytes);
    }

    public override bool Equals(object? obj) => obj is Op other && Equals(other);

    public override int GetHashCode()
    {
        // Mirror Python: TAG[0] ^ tuple.__hash__(self).
        var hash = new HashCode();
        hash.Add(Tag);
        ReadOnlySpan<byte> arg = ArgumentBytes;
        for (int i = 0; i < arg.Length; i++)
        {
            hash.Add(arg[i]);
        }

        return hash.ToHashCode();
    }

    public int CompareTo(Op? other)
    {
        if (other is null)
        {
            return 1;
        }

        int tagCmp = Tag.CompareTo(other.Tag);
        if (tagCmp != 0)
        {
            return tagCmp;
        }

        return ArgumentBytes.SequenceCompareTo(other.ArgumentBytes);
    }

    public static bool operator ==(Op? left, Op? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Op? left, Op? right) => !(left == right);

    public static bool operator <(Op left, Op right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(Op left, Op right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(Op left, Op right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(Op left, Op right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) >= 0;
    }

    /// <summary>The canonical comparer for ops (by tag, then argument).</summary>
    public static IComparer<Op> Comparer { get; } = Comparer<Op>.Create(static (a, b) =>
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        return a.CompareTo(b);
    });

    public override string ToString() => Name;

    private static FrozenDictionary<byte, Func<OtsReader, Op>> BuildRegistry()
    {
        var entries = new Dictionary<byte, Func<OtsReader, Op>>
        {
            [OpAppend.OpTag] = static r => new OpAppend(r.ReadVarBytes(MaxResultLength, minLength: 1)),
            [OpPrepend.OpTag] = static r => new OpPrepend(r.ReadVarBytes(MaxResultLength, minLength: 1)),
            [OpReverse.OpTag] = static _ => new OpReverse(),
            [OpHexlify.OpTag] = static _ => new OpHexlify(),
            [OpSha1.OpTag] = static _ => new OpSha1(),
            [OpRipemd160.OpTag] = static _ => new OpRipemd160(),
            [OpSha256.OpTag] = static _ => new OpSha256(),
            [OpKeccak256.OpTag] = static _ => new OpKeccak256(),
        };

        return entries.ToFrozenDictionary();
    }
}
