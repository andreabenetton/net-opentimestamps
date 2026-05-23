using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests.Fuzz;

/// <summary>
/// Property-style fuzz harness for the .ots parser.
/// </summary>
/// <remarks>
/// Goal: assert that the parser only ever surfaces a documented exception
/// family for any input. The set of acceptable outcomes is:
/// <list type="bullet">
///   <item><see cref="DeserializationException"/> (or any subclass — e.g. <see cref="RecursionLimitException"/>, <see cref="VarUIntOverflowException"/>)</item>
///   <item><see cref="UnsupportedMajorVersionException"/></item>
///   <item><see cref="EndOfStreamException"/> / <see cref="IOException"/> (rare; underlying stream)</item>
///   <item>Successful parse (random bytes happened to be a valid prefix)</item>
/// </list>
/// Any other escaping exception (IndexOutOfRangeException, OverflowException,
/// NullReferenceException, AggregateException, raw Exception, etc.) is a bug
/// — it means a code path didn't validate something it assumed about the bytes.
///
/// This is NOT industrial fuzzing — it's a regression net against crash-bug
/// classes that targeted unit tests miss. Coverage-guided fuzzing (SharpFuzz /
/// AFL) would be a follow-on; this harness is fast enough to run in normal CI.
/// </remarks>
public sealed class RandomParseTests
{
    private static readonly string[] FixtureNames =
    [
        "hello-world.txt.ots",
        "two-calendars.txt.ots",
        "incomplete.txt.ots",
        "known-and-unknown-notary.txt.ots",
        "unknown-notary.txt.ots",
        "different-blockchains.txt.ots",
    ];

    [Theory]
    [InlineData(0xC0FFEE)]
    [InlineData(unchecked((int)0xDEADBEEF))]
    [InlineData(0x1337)]
    public void Pure_Random_Bytes_Never_Crash_The_Parser(int seed)
    {
        var rng = new Random(seed);
        const int iterations = 1000;
        const int maxBodyBytes = 4096;

        for (int i = 0; i < iterations; i++)
        {
            int len = rng.Next(0, maxBodyBytes);
            byte[] payload = new byte[len];
            rng.NextBytes(payload);

            AssertParserOnlyThrowsAllowedExceptions(payload, seed, i, kind: "random");
        }
    }

    [Theory]
    [InlineData(unchecked((int)0xCAFEBABE))]
    [InlineData(unchecked((int)0xFEEDFACE))]
    public void Mutated_Fixtures_Never_Crash_The_Parser(int seed)
    {
        string fixtureDir = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "python-opentimestamps");
        if (!Directory.Exists(fixtureDir))
        {
            return;
        }

        var rng = new Random(seed);

        foreach (string name in FixtureNames)
        {
            string path = Path.Combine(fixtureDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            byte[] original = File.ReadAllBytes(path);

            // 200 mutations per fixture per seed: 100 bit-flips and 100 truncations.
            for (int i = 0; i < 100; i++)
            {
                byte[] mutant = (byte[])original.Clone();
                int flips = rng.Next(1, 6);
                for (int f = 0; f < flips; f++)
                {
                    int idx = rng.Next(mutant.Length);
                    byte mask = (byte)(1 << rng.Next(8));
                    mutant[idx] ^= mask;
                }

                AssertParserOnlyThrowsAllowedExceptions(mutant, seed, i, kind: $"bitflip:{name}");
            }

            for (int i = 0; i < 100; i++)
            {
                int cut = rng.Next(0, original.Length);
                byte[] mutant = original.AsSpan(0, cut).ToArray();
                AssertParserOnlyThrowsAllowedExceptions(mutant, seed, i, kind: $"truncate:{name}");
            }
        }
    }

    private static void AssertParserOnlyThrowsAllowedExceptions(
        byte[] payload, int seed, int iteration, string kind)
    {
        try
        {
            _ = DetachedTimestampFile.DeserializeFromArray(payload);
        }
        catch (DeserializationException)
        {
            // Expected — covers UnsupportedMajorVersionException, RecursionLimitException,
            // VarUIntOverflowException via inheritance.
        }
        catch (EndOfStreamException)
        {
            // Expected — underlying stream ran out.
        }
        catch (IOException)
        {
            // Expected — underlying stream issue.
        }
        catch (Exception ex)
        {
            // Anything else is a parser bug.
            Assert.Fail(
                $"Parser leaked unexpected exception type {ex.GetType().FullName} " +
                $"(seed={seed}, iteration={iteration}, kind={kind}): {ex.Message}\n" +
                $"payload hex: {Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 64)).ToArray())}");
        }
    }
}
