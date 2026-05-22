using OpenTimestamps.Serialization;

namespace OpenTimestamps.Attestations;

/// <summary>
/// Asserts that the commitment at this point in the proof equals the
/// <c>hashMerkleRoot</c> of Bitcoin block <see cref="Height"/>. Verifying
/// requires fetching that block's header from a trusted source.
/// </summary>
public sealed class BitcoinBlockHeaderAttestation : TimeAttestation
{
    /// <summary>The 8-byte type tag <c>05 88 96 0d 73 d7 19 01</c>.</summary>
    public static ReadOnlySpan<byte> AttestationTag => [0x05, 0x88, 0x96, 0x0D, 0x73, 0xD7, 0x19, 0x01];

    public BitcoinBlockHeaderAttestation(ulong height)
    {
        Height = height;
    }

    /// <summary>Block height the commitment is anchored in.</summary>
    public ulong Height { get; }

    public override ReadOnlySpan<byte> Tag => AttestationTag;

    protected override void SerializePayload(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt(Height);
    }

    internal static BitcoinBlockHeaderAttestation DeserializePayload(OtsReader reader)
    {
        ulong height = reader.ReadVarUInt();
        return new BitcoinBlockHeaderAttestation(height);
    }

    public override string ToString() => $"bitcoin block {Height}";
}
