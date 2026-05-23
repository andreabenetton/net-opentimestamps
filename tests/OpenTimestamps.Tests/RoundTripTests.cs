using OpenTimestamps;
using Xunit;

namespace OpenTimestamps.Tests;

public sealed class RoundTripTests
{
    private static readonly string FixturesRoot =
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>
    /// Fixtures whose upstream serializer emits ops in non-canonical (unsorted)
    /// order. Our serializer always emits canonical sorted order (matching the
    /// Python reference), so byte-identical round-trip would fail — but the
    /// fixture is still valid and parseable. We exclude these from the strict
    /// byte-identity check and exercise them under the weaker "stability"
    /// check instead (re-serializing our re-serialization is byte-identical to
    /// our first re-serialization).
    /// </summary>
    private static readonly HashSet<string> NonCanonicalFixtures = new(StringComparer.Ordinal)
    {
        // javascript-opentimestamps emits sibling ops in insertion order rather
        // than sorted by (tag, arg). Python ots info reads it fine; our parser
        // reads it fine; our serializer canonicalizes.
        "javascript-opentimestamps/ripemd160/README.md.ots",
    };

    /// <summary>
    /// Enumerate every .ots fixture under <c>fixtures/**</c>. Adding a new
    /// fixture is a drop-in: copy it under the appropriate upstream subdirectory,
    /// add a PROVENANCE.md row, and the round-trip test picks it up
    /// automatically on the next run.
    /// </summary>
    public static IEnumerable<object[]> Fixtures
    {
        get
        {
            if (!Directory.Exists(FixturesRoot))
            {
                yield break;
            }

            foreach (string path in Directory
                .EnumerateFiles(FixturesRoot, "*.ots", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(FixturesRoot, path)
                    .Replace('\\', '/');
                yield return [rel];
            }
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Fixtures))]
    public void Parse_Serialize_RoundTrip_Is_ByteIdentical(string relativePath)
    {
        Skip.If(NonCanonicalFixtures.Contains(relativePath),
            $"{relativePath} is upstream-non-canonical; covered by Parse_Reserialize_Is_Stable.");

        string path = Path.Combine(FixturesRoot, relativePath);
        byte[] original = File.ReadAllBytes(path);

        DetachedTimestampFile parsed = DetachedTimestampFile.DeserializeFromArray(original);
        byte[] reserialized = parsed.SerializeToArray();

        Assert.Equal(original.Length, reserialized.Length);
        Assert.True(
            original.AsSpan().SequenceEqual(reserialized),
            $"Re-serialized bytes differ from original for {relativePath}.");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Parse_Reserialize_Is_Stable(string relativePath)
    {
        // Stability invariant for every fixture (canonical or not): once we
        // canonicalize on first re-serialize, a second re-serialize must
        // produce identical bytes — our output is the fixed point.
        string path = Path.Combine(FixturesRoot, relativePath);
        byte[] original = File.ReadAllBytes(path);

        DetachedTimestampFile parsed1 = DetachedTimestampFile.DeserializeFromArray(original);
        byte[] ser1 = parsed1.SerializeToArray();

        DetachedTimestampFile parsed2 = DetachedTimestampFile.DeserializeFromArray(ser1);
        byte[] ser2 = parsed2.SerializeToArray();

        Assert.True(
            ser1.AsSpan().SequenceEqual(ser2),
            $"Serializer is not idempotent on {relativePath}.");
    }

    [Fact]
    public void HelloWorld_File_Digest_Matches_Sha256()
    {
        string otsPath = Path.Combine(FixturesRoot, "python-opentimestamps", "hello-world.txt.ots");
        string filePath = Path.Combine(FixturesRoot, "python-opentimestamps", "hello-world.txt");

        DetachedTimestampFile dtf = DetachedTimestampFile.DeserializeFromFile(otsPath);
        byte[] expected = dtf.FileDigest.ToArray();
        byte[] actual = dtf.FileHashOp.HashFile(filePath);

        Assert.Equal(expected, actual);
    }
}
