using OpenTimestamps;
using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests;

/// <summary>
/// Confirms that malformed or truncated .ots inputs produce typed
/// <see cref="DeserializationException"/> failures rather than crashes,
/// unbounded reads, or silent successes.
/// </summary>
public sealed class NegativeInputTests
{
    [Fact]
    public void Empty_Stream_Throws()
    {
        Assert.Throws<DeserializationException>(
            () => DetachedTimestampFile.DeserializeFromArray([]));
    }

    [Fact]
    public void Bad_Magic_Throws()
    {
        byte[] bytes = new byte[64];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)i;
        }

        Assert.Throws<DeserializationException>(
            () => DetachedTimestampFile.DeserializeFromArray(bytes));
    }

    [Fact]
    public void Unsupported_Major_Version_Throws_Typed()
    {
        byte[] magic = DetachedTimestampFile.HeaderMagic.ToArray();
        byte[] bytes = [.. magic, (byte)99];
        Assert.Throws<UnsupportedMajorVersionException>(
            () => DetachedTimestampFile.DeserializeFromArray(bytes));
    }

    [Fact]
    public void Truncated_After_Magic_Throws()
    {
        byte[] magic = DetachedTimestampFile.HeaderMagic.ToArray();
        Assert.Throws<DeserializationException>(
            () => DetachedTimestampFile.DeserializeFromArray(magic));
    }

    [Fact]
    public void Truncated_Mid_Tree_Throws()
    {
        // Take a real fixture and chop off the last byte.
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "python-opentimestamps",
            "hello-world.txt.ots");
        byte[] original = File.ReadAllBytes(path);
        byte[] truncated = original.AsSpan(0, original.Length - 1).ToArray();

        Assert.Throws<DeserializationException>(
            () => DetachedTimestampFile.DeserializeFromArray(truncated));
    }

    [Fact]
    public void Trailing_Garbage_After_Tree_Throws()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "python-opentimestamps",
            "hello-world.txt.ots");
        byte[] original = File.ReadAllBytes(path);
        byte[] withGarbage = [.. original, 0x00, 0x00, 0x00];

        Assert.Throws<DeserializationException>(
            () => DetachedTimestampFile.DeserializeFromArray(withGarbage));
    }

    [Fact]
    public void Invalid_File_Hash_Op_Tag_Throws()
    {
        byte[] bytes =
        [
            .. DetachedTimestampFile.HeaderMagic.ToArray(),
            DetachedTimestampFile.MajorVersion,
            0xAA,   // not a known crypt-op tag
        ];

        Assert.Throws<DeserializationException>(
            () => DetachedTimestampFile.DeserializeFromArray(bytes));
    }

    [Fact]
    public void OpAppend_With_Zero_Length_Arg_Throws_On_Parse()
    {
        // Build a minimal valid header + sha256 op + 32-byte digest, then
        // emit an OpAppend tag (0xF0) with varuint(0) length — the parser
        // should reject (binary ops require length >= 1).
        byte[] digest = new byte[32];
        byte[] bytes =
        [
            .. DetachedTimestampFile.HeaderMagic.ToArray(),
            DetachedTimestampFile.MajorVersion,
            0x08,            // OpSHA256 tag for file_hash_op
            .. digest,
            0xF0,            // OpAppend tag
            0x00,            // varuint length 0 ← invalid (minLength=1)
        ];

        Assert.Throws<DeserializationException>(
            () => DetachedTimestampFile.DeserializeFromArray(bytes));
    }
}
