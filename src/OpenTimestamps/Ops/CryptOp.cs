using System.Collections.Frozen;
using OpenTimestamps.Serialization;

namespace OpenTimestamps.Ops;

/// <summary>
/// A cryptographic hash operation. Crypt ops may serve as the <c>file_hash_op</c>
/// of a detached timestamp file; that's why they expose a fixed
/// <see cref="DigestLength"/>.
/// </summary>
public abstract class CryptOp : UnaryOp
{
    private static readonly FrozenDictionary<byte, Func<CryptOp>> CryptRegistry = BuildRegistry();

    /// <summary>The number of bytes produced by this hash.</summary>
    public abstract int DigestLength { get; }

    /// <summary>Hash a stream in chunks, returning the final digest.</summary>
    public byte[] HashStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return HashStreamCore(stream);
    }

    /// <summary>Hash a file by path.</summary>
    public byte[] HashFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var fs = File.OpenRead(path);
        return HashStreamCore(fs);
    }

    /// <summary>Read a CryptOp from the wire. The tag byte is consumed by this call.</summary>
    public static CryptOp DeserializeCrypt(OtsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte tag = reader.ReadUInt8();
        if (!CryptRegistry.TryGetValue(tag, out var factory))
        {
            throw new DeserializationException($"Tag 0x{tag:x2} is not a CryptOp.");
        }

        return factory();
    }

    /// <summary>Subclass entry-point: stream-friendly hashing.</summary>
    protected abstract byte[] HashStreamCore(Stream stream);

    private static FrozenDictionary<byte, Func<CryptOp>> BuildRegistry()
    {
        var entries = new Dictionary<byte, Func<CryptOp>>
        {
            [OpSha1.OpTag] = static () => new OpSha1(),
            [OpRipemd160.OpTag] = static () => new OpRipemd160(),
            [OpSha256.OpTag] = static () => new OpSha256(),
            [OpKeccak256.OpTag] = static () => new OpKeccak256(),
        };

        return entries.ToFrozenDictionary();
    }
}
