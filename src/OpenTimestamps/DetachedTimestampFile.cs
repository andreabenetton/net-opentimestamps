using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;

namespace OpenTimestamps;

/// <summary>
/// A complete <c>.ots</c> detached-timestamp file: a magic prefix, version,
/// the file-hash operation, the digest of the file, and the timestamp tree
/// of operations and attestations rooted at that digest.
/// </summary>
public sealed class DetachedTimestampFile
{
    /// <summary>The fixed 31-byte file magic.</summary>
    public static ReadOnlySpan<byte> HeaderMagic =>
    [
        0x00, 0x4F, 0x70, 0x65, 0x6E, 0x54, 0x69, 0x6D, 0x65, 0x73, 0x74, 0x61, 0x6D, 0x70, 0x73, 0x00,
        0x00, 0x50, 0x72, 0x6F, 0x6F, 0x66, 0x00, 0xBF, 0x89, 0xE2, 0xE8, 0x84, 0xE8, 0x92, 0x94,
    ];

    /// <summary>Currently the only supported major version.</summary>
    public const byte MajorVersion = 1;

    /// <summary>Minimum permitted file digest length (160-bit).</summary>
    public const int MinFileDigestLength = 20;

    /// <summary>Maximum permitted file digest length (256-bit).</summary>
    public const int MaxFileDigestLength = 32;

    public DetachedTimestampFile(CryptOp fileHashOp, Timestamp timestamp)
    {
        ArgumentNullException.ThrowIfNull(fileHashOp);
        ArgumentNullException.ThrowIfNull(timestamp);
        if (timestamp.Msg.Length != fileHashOp.DigestLength)
        {
            throw new ArgumentException(
                $"timestamp.msg length ({timestamp.Msg.Length}) does not match " +
                $"file_hash_op digest length ({fileHashOp.DigestLength}).");
        }

        FileHashOp = fileHashOp;
        Timestamp = timestamp;
    }

    /// <summary>The hash operation applied to the file's bytes to produce <c>Timestamp.Msg</c>.</summary>
    public CryptOp FileHashOp { get; }

    /// <summary>The timestamp tree rooted at the file digest.</summary>
    public Timestamp Timestamp { get; }

    /// <summary>The file digest under <see cref="FileHashOp"/>. Equal to <c>Timestamp.Msg</c>.</summary>
    public ReadOnlySpan<byte> FileDigest => Timestamp.Msg;

    /// <summary>
    /// Build a detached timestamp for a file by hashing it with <paramref name="fileHashOp"/>.
    /// </summary>
    public static DetachedTimestampFile FromFileBytes(CryptOp fileHashOp, byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileHashOp);
        ArgumentNullException.ThrowIfNull(fileBytes);
        byte[] digest = fileHashOp.Call(fileBytes);
        return new DetachedTimestampFile(fileHashOp, new Timestamp(digest));
    }

    /// <summary>
    /// Build a detached timestamp for a file by hashing it with <paramref name="fileHashOp"/>.
    /// </summary>
    public static DetachedTimestampFile FromFile(CryptOp fileHashOp, string path)
    {
        ArgumentNullException.ThrowIfNull(fileHashOp);
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] digest = fileHashOp.HashFile(path);
        return new DetachedTimestampFile(fileHashOp, new Timestamp(digest));
    }

    /// <summary>
    /// Build a detached timestamp for an arbitrary stream by hashing it with
    /// <paramref name="fileHashOp"/>.
    /// </summary>
    public static DetachedTimestampFile FromStream(CryptOp fileHashOp, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(fileHashOp);
        ArgumentNullException.ThrowIfNull(stream);
        byte[] digest = fileHashOp.HashStream(stream);
        return new DetachedTimestampFile(fileHashOp, new Timestamp(digest));
    }

    /// <summary>Write a complete .ots file to <paramref name="stream"/>.</summary>
    public void Serialize(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var writer = new OtsWriter(stream);
        Serialize(writer);
    }

    /// <summary>Serialize a complete .ots file via the supplied writer.</summary>
    public void Serialize(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBytes(HeaderMagic);
        writer.WriteUInt8(MajorVersion);
        FileHashOp.Serialize(writer);
        writer.WriteBytes(Timestamp.MsgArray());
        Timestamp.Serialize(writer);
    }

    /// <summary>Serialize a complete .ots file to a byte array.</summary>
    public byte[] SerializeToArray()
    {
        using var ms = new MemoryStream();
        Serialize(ms);
        return ms.ToArray();
    }

    /// <summary>Write a complete .ots file to disk.</summary>
    public void SerializeToFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var fs = File.Create(path);
        Serialize(fs);
    }

    /// <summary>Read a complete .ots file from <paramref name="stream"/>.</summary>
    public static DetachedTimestampFile Deserialize(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new OtsReader(stream);
        return Deserialize(reader);
    }

    /// <summary>Read a complete .ots file via the supplied reader.</summary>
    public static DetachedTimestampFile Deserialize(OtsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reader.AssertMagic(HeaderMagic);
        byte major = reader.ReadUInt8();
        if (major != MajorVersion)
        {
            throw new UnsupportedMajorVersionException(major);
        }

        CryptOp fileHashOp = CryptOp.DeserializeCrypt(reader);
        byte[] fileDigest = reader.ReadBytes(fileHashOp.DigestLength);
        Timestamp timestamp = Timestamp.Deserialize(reader, fileDigest);
        reader.AssertEof();
        return new DetachedTimestampFile(fileHashOp, timestamp);
    }

    /// <summary>Read a complete .ots file from a byte buffer.</summary>
    public static DetachedTimestampFile DeserializeFromArray(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var ms = new MemoryStream(data, writable: false);
        return Deserialize(ms);
    }

    /// <summary>Read a complete .ots file from disk.</summary>
    public static DetachedTimestampFile DeserializeFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var fs = File.OpenRead(path);
        return Deserialize(fs);
    }
}
