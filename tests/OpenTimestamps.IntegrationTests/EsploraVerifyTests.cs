using OpenTimestamps;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.IntegrationTests;

[Trait("Category", "Network")]
public sealed class EsploraVerifyTests : IClassFixture<NetworkFixture>
{
    private readonly NetworkFixture _network;

    public EsploraVerifyTests(NetworkFixture network)
    {
        _network = network;
    }

    [SkippableFact]
    public async Task HelloWorld_Verifies_Against_Blockstream_Esplora()
    {
        Skip.If(_network.SkipNetwork, "OTS_SKIP_NETWORK=1");

        string fixturesDir = LocateFixturesDir();
        string filePath = Path.Combine(fixturesDir, "hello-world.txt");
        string otsPath = Path.Combine(fixturesDir, "hello-world.txt.ots");

        DetachedTimestampFile dtf = DetachedTimestampFile.DeserializeFromFile(otsPath);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var provider = new EsploraBlockHeaderProvider(http, new Uri("https://blockstream.info/api/"));

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyFileAsync(dtf, filePath, provider);

        Assert.Equal(TimestampStatus.Verified, result.Status);
        Assert.NotEmpty(result.VerifiedAttestations);
        Assert.All(result.VerifiedAttestations, v =>
            Assert.Equal(TrustCategory.Explorer, v.TrustCategory));
        // hello-world.txt was stamped in 2015, block 358391.
        Assert.Contains(result.VerifiedAttestations, v => v.Height == 358391UL);
    }

    private static string LocateFixturesDir()
    {
        // Test fixtures live in the unit-tests project; navigate from the test binary
        // up to the tests directory and into the unit project's fixture tree.
        string baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null && dir.Name != "net-opentimestamps")
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root above {baseDir}.");
        }

        return Path.Combine(
            dir.FullName,
            "tests",
            "OpenTimestamps.Tests",
            "fixtures",
            "python-opentimestamps");
    }
}

public sealed class NetworkFixture
{
    public bool SkipNetwork { get; } =
        string.Equals(Environment.GetEnvironmentVariable("OTS_SKIP_NETWORK"), "1", StringComparison.Ordinal);
}
