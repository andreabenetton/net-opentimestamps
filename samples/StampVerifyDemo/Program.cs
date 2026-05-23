using OpenTimestamps;
using OpenTimestamps.Calendars;
using OpenTimestamps.Stamping;
using OpenTimestamps.Verification;

namespace OpenTimestamps.Samples.StampVerifyDemo;

/// <summary>
/// End-to-end demo: stamp a file, save the .ots, upgrade later, verify
/// against Bitcoin. Run with a path to any file you want to stamp.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrEmpty(args[0]))
        {
            Console.Error.WriteLine("usage: StampVerifyDemo <path-to-file>");
            return 1;
        }

        string filePath = args[0];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"file not found: {filePath}");
            return 1;
        }

        // One HttpClient for the whole demo, per CLAUDE.md.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // === STAMP ===
        Console.WriteLine($"Stamping {filePath}...");
        var calendars = DefaultCalendars.Aggregators
            .Select(uri => new CalendarClient(http, new Uri(uri)))
            .ToList();

        var stamp = new StampService();
        DetachedTimestampFile dtf = await stamp.StampFileAsync(filePath, calendars);

        string otsPath = filePath + ".ots";
        dtf.SerializeToFile(otsPath);
        Console.WriteLine($"  Wrote {otsPath} ({new FileInfo(otsPath).Length} bytes)");
        Console.WriteLine("  Note: the proof is PENDING — calendars need ~1-3 hours to anchor it.");

        // === UPGRADE (no-op so soon after stamping; shown for the API shape) ===
        Console.WriteLine("Attempting upgrade (will be a no-op if calendars haven't anchored yet)...");
        var upgrade = new UpgradeService(
            CalendarWhitelist.Default,
            uri => new CalendarClient(http, uri));
        UpgradeResult upgradeResult = await upgrade.UpgradeAsync(dtf);
        Console.WriteLine($"  Resolved: {upgradeResult.Resolved.Count}");
        Console.WriteLine($"  Still pending: {upgradeResult.StillPending.Count}");
        Console.WriteLine($"  Errors: {upgradeResult.Errors.Count}");
        if (upgradeResult.ChangedAnything)
        {
            dtf.SerializeToFile(otsPath);
            Console.WriteLine($"  Re-wrote {otsPath} with upgrades merged.");
        }

        // === VERIFY ===
        // Explorer: not trustless. For production / compliance, point a
        // BitcoinCoreRpcBlockHeaderProvider at your own node instead.
        Console.WriteLine("Verifying against Bitcoin (via Esplora — Explorer trust)...");
        var provider = new EsploraBlockHeaderProvider(
            http, new Uri("https://blockstream.info/api/"));

        var verify = new VerificationService();
        VerificationResult result = await verify.VerifyFileAsync(dtf, filePath, provider);

        Console.WriteLine($"  Status: {result.Status}");
        Console.WriteLine($"  Pending attestations: {result.PendingAttestations.Count}");
        Console.WriteLine($"  Bitcoin attestations: {result.BitcoinAttestations.Count}");
        Console.WriteLine($"  Verified attestations: {result.VerifiedAttestations.Count}");

        foreach (VerifiedAttestation v in result.VerifiedAttestations)
        {
            Console.WriteLine(
                $"    Block {v.Height} ({v.BlockTime:u}) via {v.ProviderName} [{v.TrustCategory}]");
        }

        foreach (string w in result.Warnings)
        {
            Console.WriteLine($"  ! {w}");
        }

        return result.Status switch
        {
            TimestampStatus.Verified => 0,
            TimestampStatus.Anchored => 0,  // anchored but provider couldn't reach a block
            TimestampStatus.Incomplete => 0,  // freshly stamped; not anchored yet
            _ => 1,
        };
    }
}
