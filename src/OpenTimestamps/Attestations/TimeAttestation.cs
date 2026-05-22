using OpenTimestamps.Serialization;

namespace OpenTimestamps.Attestations;

/// <summary>
/// A time-attesting tag at the tip of a timestamp proof. Each subclass declares
/// the 8-byte <see cref="Tag"/> that identifies its type on the wire.
/// </summary>
/// <remarks>
/// Mirrors <c>opentimestamps/core/notary.py</c>.
/// </remarks>
public abstract class TimeAttestation : IEquatable<TimeAttestation>, IComparable<TimeAttestation>
{
    /// <summary>Fixed length of the attestation type tag.</summary>
    public const int TagSize = 8;

    /// <summary>Maximum length of an attestation payload after the tag.</summary>
    public const int MaxPayloadSize = 8192;

    /// <summary>The 8-byte type tag for this attestation.</summary>
    public abstract ReadOnlySpan<byte> Tag { get; }

    /// <summary>Compute and return the on-wire serialized payload bytes (without the tag or framing).</summary>
    public byte[] SerializePayloadToArray()
    {
        using var ms = new MemoryStream();
        var writer = new OtsWriter(ms);
        SerializePayload(writer);
        return ms.ToArray();
    }

    /// <summary>Serialize this attestation in full (tag + varbytes-framed payload).</summary>
    public void Serialize(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBytes(Tag);
        writer.WriteVarBytes(SerializePayloadToArray());
    }

    /// <summary>Subclass entry-point that writes the payload bytes (without framing).</summary>
    protected abstract void SerializePayload(OtsWriter writer);

    /// <summary>Read an attestation from the wire (consuming both tag and payload).</summary>
    public static TimeAttestation Deserialize(OtsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        byte[] tag = reader.ReadBytes(TagSize);
        byte[] payload = reader.ReadVarBytes(MaxPayloadSize);

        if (tag.AsSpan().SequenceEqual(PendingAttestation.AttestationTag))
        {
            return DeserializeKnown(payload, static r => PendingAttestation.DeserializePayload(r));
        }

        if (tag.AsSpan().SequenceEqual(BitcoinBlockHeaderAttestation.AttestationTag))
        {
            return DeserializeKnown(payload, static r => BitcoinBlockHeaderAttestation.DeserializePayload(r));
        }

        if (tag.AsSpan().SequenceEqual(LitecoinBlockHeaderAttestation.AttestationTag))
        {
            return DeserializeKnown(payload, static r => LitecoinBlockHeaderAttestation.DeserializePayload(r));
        }

        if (tag.AsSpan().SequenceEqual(EthereumBlockHeaderAttestation.AttestationTag))
        {
            return DeserializeKnown(payload, static r => EthereumBlockHeaderAttestation.DeserializePayload(r));
        }

        return new UnknownAttestation(tag, payload);
    }

    private static TimeAttestation DeserializeKnown(
        byte[] payload, Func<OtsReader, TimeAttestation> factory)
    {
        using var ms = new MemoryStream(payload, writable: false);
        var reader = new OtsReader(ms);
        TimeAttestation attestation = factory(reader);
        reader.AssertEof();
        return attestation;
    }

    public bool Equals(TimeAttestation? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Tag.SequenceEqual(other.Tag) && PayloadEquals(other);
    }

    public override bool Equals(object? obj) => obj is TimeAttestation other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        ReadOnlySpan<byte> tag = Tag;
        for (int i = 0; i < tag.Length; i++)
        {
            hash.Add(tag[i]);
        }

        ContributeToHash(ref hash);
        return hash.ToHashCode();
    }

    public int CompareTo(TimeAttestation? other)
    {
        if (other is null)
        {
            return 1;
        }

        int tagCmp = Tag.SequenceCompareTo(other.Tag);
        if (tagCmp != 0)
        {
            return tagCmp;
        }

        return ComparePayloads(other);
    }

    /// <summary>
    /// Subclass-specific equality check. Default uses the serialized payload bytes.
    /// </summary>
    protected virtual bool PayloadEquals(TimeAttestation other) =>
        SerializePayloadToArray().AsSpan().SequenceEqual(other.SerializePayloadToArray());

    /// <summary>
    /// Subclass-specific ordering. Default uses the serialized payload bytes.
    /// </summary>
    protected virtual int ComparePayloads(TimeAttestation other) =>
        SerializePayloadToArray().AsSpan().SequenceCompareTo(other.SerializePayloadToArray());

    /// <summary>Subclass-specific contribution to <see cref="GetHashCode"/>.</summary>
    protected virtual void ContributeToHash(ref HashCode hash)
    {
        byte[] payload = SerializePayloadToArray();
        for (int i = 0; i < payload.Length; i++)
        {
            hash.Add(payload[i]);
        }
    }
}
