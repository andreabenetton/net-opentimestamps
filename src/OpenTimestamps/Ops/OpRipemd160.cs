using Org.BouncyCastle.Crypto.Digests;

namespace OpenTimestamps.Ops;

/// <summary>
/// RIPEMD-160 (160-bit). Implemented via BouncyCastle because .NET no longer ships
/// RIPEMD-160 in the BCL.
/// </summary>
public sealed class OpRipemd160 : CryptOp
{
    internal const byte OpTag = 0x03;

    private const int BufferSize = 1 << 20;

    public override byte Tag => OpTag;

    public override string Name => "ripemd160";

    public override int DigestLength => 20;

    protected override byte[] DoCall(byte[] message)
    {
        var digest = new RipeMD160Digest();
        digest.BlockUpdate(message, 0, message.Length);
        byte[] output = new byte[DigestLength];
        digest.DoFinal(output, 0);
        return output;
    }

    protected override byte[] HashStreamCore(Stream stream)
    {
        var digest = new RipeMD160Digest();
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
