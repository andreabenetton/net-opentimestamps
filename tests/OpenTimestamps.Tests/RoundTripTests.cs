using OpenTimestamps;
using Xunit;

namespace OpenTimestamps.Tests;

public sealed class RoundTripTests
{
    public static TheoryData<string> Fixtures =>
        new()
        {
            "hello-world.txt.ots",
            "two-calendars.txt.ots",
            "incomplete.txt.ots",
            "known-and-unknown-notary.txt.ots",
            "unknown-notary.txt.ots",
            "different-blockchains.txt.ots",
        };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Parse_Serialize_RoundTrip_Is_ByteIdentical(string fixtureName)
    {
        string path = FixturePath(fixtureName);
        byte[] original = File.ReadAllBytes(path);

        DetachedTimestampFile parsed = DetachedTimestampFile.DeserializeFromArray(original);
        byte[] reserialized = parsed.SerializeToArray();

        Assert.Equal(original.Length, reserialized.Length);
        Assert.True(
            original.AsSpan().SequenceEqual(reserialized),
            $"Re-serialized bytes differ from original for {fixtureName}.");
    }

    [Fact]
    public void HelloWorld_File_Digest_Matches_Sha256()
    {
        string otsPath = FixturePath("hello-world.txt.ots");
        string filePath = FixturePath("hello-world.txt");

        DetachedTimestampFile dtf = DetachedTimestampFile.DeserializeFromFile(otsPath);
        byte[] expected = dtf.FileDigest.ToArray();
        byte[] actual = dtf.FileHashOp.HashFile(filePath);

        Assert.Equal(expected, actual);
    }

    private static string FixturePath(string name) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "python-opentimestamps",
            name);
}
