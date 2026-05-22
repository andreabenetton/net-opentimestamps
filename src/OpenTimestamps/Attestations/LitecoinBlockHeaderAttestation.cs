using OpenTimestamps.Serialization;

namespace OpenTimestamps.Attestations;

/// <summary>
/// Asserts that the commitment at this point in the proof equals the merkle root
/// of Litecoin block <see cref="Height"/>. Verification against Litecoin headers
/// is not implemented in this library.
/// </summary>
public sealed class LitecoinBlockHeaderAttestation : TimeAttestation
{
    /// <summary>The 8-byte type tag <c>06 86 9a 0d 73 d7 1b 45</c>.</summary>
    public static ReadOnlySpan<byte> AttestationTag => [0x06, 0x86, 0x9A, 0x0D, 0x73, 0xD7, 0x1B, 0x45];

    public LitecoinBlockHeaderAttestation(ulong height)
    {
        Height = height;
    }

    /// <summary>Litecoin block height.</summary>
    public ulong Height { get; }

    public override ReadOnlySpan<byte> Tag => AttestationTag;

    protected override void SerializePayload(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt(Height);
    }

    internal static LitecoinBlockHeaderAttestation DeserializePayload(OtsReader reader)
    {
        ulong height = reader.ReadVarUInt();
        return new LitecoinBlockHeaderAttestation(height);
    }

    public override string ToString() => $"litecoin block {Height}";
}
