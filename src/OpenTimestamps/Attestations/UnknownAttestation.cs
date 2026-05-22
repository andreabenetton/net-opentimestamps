using OpenTimestamps.Serialization;

namespace OpenTimestamps.Attestations;

/// <summary>
/// An attestation whose 8-byte tag is not recognised. Preserved verbatim so that
/// proofs containing future or third-party attestation types still round-trip
/// byte-identically.
/// </summary>
public sealed class UnknownAttestation : TimeAttestation
{
    private readonly byte[] _tag;
    private readonly byte[] _payload;

    public UnknownAttestation(byte[] tag, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(payload);
        if (tag.Length != TagSize)
        {
            throw new ArgumentException($"Tag must be exactly {TagSize} bytes; got {tag.Length}.", nameof(tag));
        }

        if (payload.Length > MaxPayloadSize)
        {
            throw new ArgumentException(
                $"Payload too large: {payload.Length} > {MaxPayloadSize}.", nameof(payload));
        }

        _tag = (byte[])tag.Clone();
        _payload = (byte[])payload.Clone();
    }

    public override ReadOnlySpan<byte> Tag => _tag;

    /// <summary>The opaque attestation payload, retained verbatim.</summary>
    public ReadOnlySpan<byte> Payload => _payload;

    /// <summary>A copy of the attestation payload bytes.</summary>
    public byte[] PayloadArray() => (byte[])_payload.Clone();

    protected override void SerializePayload(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBytes(_payload);
    }

    public override string ToString() => $"unknown 0x{Convert.ToHexString(_tag).ToLowerInvariant()}";
}
