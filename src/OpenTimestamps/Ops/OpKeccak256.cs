using Org.BouncyCastle.Crypto.Digests;

namespace OpenTimestamps.Ops;

/// <summary>
/// Keccak-256 (Ethereum's pre-NIST Keccak with 256-bit output).
/// </summary>
/// <remarks>
/// This is NOT NIST SHA3-256 — Keccak-256 and SHA3-256 differ in padding and
/// will produce different digests for the same input. Use
/// <see cref="KeccakDigest"/>(256), not <see cref="Sha3Digest"/>.
/// </remarks>
public sealed class OpKeccak256 : CryptOp
{
    internal const byte OpTag = 0x67;

    private const int BufferSize = 1 << 20;

    public override byte Tag => OpTag;

    public override string Name => "keccak256";

    public override int DigestLength => 32;

    protected override byte[] DoCall(byte[] message)
    {
        var digest = new KeccakDigest(256);
        digest.BlockUpdate(message, 0, message.Length);
        byte[] output = new byte[DigestLength];
        digest.DoFinal(output, 0);
        return output;
    }

    protected override byte[] HashStreamCore(Stream stream)
    {
        var digest = new KeccakDigest(256);
        byte[] buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            digest.BlockUpdate(buffer, 0, read);
        }

        byte[] output = new byte[DigestLength];
        digest.DoFinal(output, 0);
        return output;
    }
}
