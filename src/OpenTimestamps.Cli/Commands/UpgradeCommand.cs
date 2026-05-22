using OpenTimestamps;
using OpenTimestamps.Calendars;
using OpenTimestamps.Stamping;

namespace OpenTimestamps.Cli.Commands;

internal static class UpgradeCommand
{
    public const string Usage = "usage: ots upgrade <proof.ots> [--allow-calendar <pattern>]...";

    public static async Task<int> RunAsync(string[] args, HttpClient http, CancellationToken ct)
    {
        var parser = new ArgParser("upgrade", args)
            .Option("--allow-calendar")
            .Flag("--no-backup");
        parser.Parse();

        if (parser.Positionals.Count != 1)
        {
            throw new CliUsageException("upgrade", Usage);
        }

        string proofPath = parser.Positionals[0];
        if (!File.Exists(proofPath))
        {
            Console.Error.WriteLine($"ots upgrade: file not found: {proofPath}");
            return ExitCode.OperationFailed;
        }

        DetachedTimestampFile dtf;
        try
        {
            dtf = DetachedTimestampFile.DeserializeFromFile(proofPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ots upgrade: failed to parse {proofPath}: {ex.Message}");
            return ExitCode.OperationFailed;
        }

        IReadOnlyList<string> extra = parser.GetOptions("--allow-calendar");
        var patterns = new List<string>(CalendarWhitelist.DefaultPatterns);
        patterns.AddRange(extra);
        var whitelist = new CalendarWhitelist(patterns);

        var svc = new UpgradeService(whitelist, uri => new CalendarClient(http, uri));
        UpgradeResult result;
        try
        {
            result = await svc.UpgradeAsync(dtf, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ots upgrade: {ex.Message}");
            return ExitCode.OperationFailed;
        }

        foreach (string resolved in result.Resolved)
        {
            Console.Out.WriteLine($"Upgraded from calendar: {resolved}");
        }

        foreach (string still in result.StillPending)
        {
            Console.Out.WriteLine($"Still pending: {still}");
        }

        foreach (string skip in result.Skipped)
        {
            Console.Out.WriteLine($"Skipped (whitelist): {skip}");
        }

        foreach (string err in result.Errors)
        {
            Console.Out.WriteLine($"Error: {err}");
        }

        if (result.ChangedAnything)
        {
            if (!parser.HasFlag("--no-backup"))
            {
                File.Copy(proofPath, proofPath + ".bak", overwrite: true);
            }

            dtf.SerializeToFile(proofPath);
            Console.Out.WriteLine($"Wrote upgraded proof to {proofPath}.");
            return ExitCode.Success;
        }

        Console.Out.WriteLine("No new attestations available.");
        return result.Errors.Count > 0 ? ExitCode.OperationFailed : ExitCode.Success;
    }
}
