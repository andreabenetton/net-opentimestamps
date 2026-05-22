using OpenTimestamps.Serialization;

namespace OpenTimestamps.Attestations;

/// <summary>
/// Asserts that the commitment at this point in the proof equals the
/// <c>transactionsRoot</c> of Ethereum block <see cref="Height"/>.
/// </summary>
/// <remarks>
/// Treated as "dubious" upstream because the Ethereum chain has experienced
/// consensus splits in the past. Verification against Ethereum is not implemented
/// in this library.
/// </remarks>
public sealed class EthereumBlockHeaderAttestation : TimeAttestation
{
    /// <summary>The 8-byte type tag <c>30 fe 80 87 b5 c7 ea d7</c>.</summary>
    public static ReadOnlySpan<byte> AttestationTag => [0x30, 0xFE, 0x80, 0x87, 0xB5, 0xC7, 0xEA, 0xD7];

    public EthereumBlockHeaderAttestation(ulong height)
    {
        Height = height;
    }

    public ulong Height { get; }

    public override ReadOnlySpan<byte> Tag => AttestationTag;

    protected override void SerializePayload(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt(Height);
    }

    internal static EthereumBlockHeaderAttestation DeserializePayload(OtsReader reader)
    {
        ulong height = reader.ReadVarUInt();
        return new EthereumBlockHeaderAttestation(height);
    }

    public override string ToString() => $"ethereum block {Height}";
}
