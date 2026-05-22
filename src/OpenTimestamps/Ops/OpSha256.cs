using System.Security.Cryptography;

namespace OpenTimestamps.Ops;

/// <summary>
/// SHA-256 (256-bit). The default file-hash operation for new stamps.
/// </summary>
public sealed class OpSha256 : CryptOp
{
    internal const byte OpTag = 0x08;

    public override byte Tag => OpTag;

    public override string Name => "sha256";

    public override int DigestLength => 32;

    protected override byte[] DoCall(byte[] message) => SHA256.HashData(message);

    protected override byte[] HashStreamCore(Stream stream)
    {
        using var hasher = SHA256.Create();
        return hasher.ComputeHash(stream);
    }
}
