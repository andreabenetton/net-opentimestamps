using System.Security.Cryptography;

namespace OpenTimestamps.Ops;

/// <summary>
/// SHA-1 (160-bit). Retained for compatibility with legacy proofs; SHA-1 is collision-broken
/// and must not be selected as the file-hash operation for newly stamped files.
/// </summary>
public sealed class OpSha1 : CryptOp
{
    internal const byte OpTag = 0x02;

    public override byte Tag => OpTag;

    public override string Name => "sha1";

    public override int DigestLength => 20;

    protected override byte[] DoCall(byte[] message) => SHA1.HashData(message);

    protected override byte[] HashStreamCore(Stream stream)
    {
        using var hasher = SHA1.Create();
        return hasher.ComputeHash(stream);
    }
}
