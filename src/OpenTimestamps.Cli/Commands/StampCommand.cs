using OpenTimestamps;
using OpenTimestamps.Calendars;
using OpenTimestamps.Stamping;

namespace OpenTimestamps.Cli.Commands;

internal static class StampCommand
{
    public const string Usage =
        "usage: ots stamp <file> [--calendar <url>]... [--quorum N] [--output <file.ots>]";

    public static async Task<int> RunAsync(string[] args, HttpClient http, CancellationToken ct)
    {
        var parser = new ArgParser("stamp", args)
            .Option("--calendar")
            .Option("--quorum")
            .Option("--output");
        parser.Parse();

        if (parser.Positionals.Count != 1)
        {
            throw new CliUsageException("stamp", Usage);
        }

        string filePath = parser.Positionals[0];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"ots stamp: file not found: {filePath}");
            return ExitCode.OperationFailed;
        }

        IReadOnlyList<string> calendarUris = parser.GetOptions("--calendar").Count > 0
            ? parser.GetOptions("--calendar")
            : DefaultCalendars.Aggregators;

        if (!int.TryParse(parser.GetOption("--quorum") ?? DefaultCalendars.DefaultStampQuorum.ToString(),
                          out int quorum))
        {
            throw new CliUsageException("stamp", "--quorum requires a positive integer.");
        }

        if (quorum < 1)
        {
            throw new CliUsageException("stamp", "--quorum must be >= 1.");
        }

        string outputPath = parser.GetOption("--output") ?? filePath + ".ots";
        if (File.Exists(outputPath))
        {
            Console.Error.WriteLine($"ots stamp: refusing to overwrite existing {outputPath}");
            return ExitCode.OperationFailed;
        }

        var calendars = new List<CalendarClient>(calendarUris.Count);
        foreach (string uri in calendarUris)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            {
                Console.Error.WriteLine($"ots stamp: invalid calendar URI: {uri}");
                return ExitCode.UsageError;
            }

            calendars.Add(new CalendarClient(http, parsed));
        }

        var svc = new StampService();
        DetachedTimestampFile dtf;
        try
        {
            dtf = await svc.StampFileAsync(filePath, calendars, quorum, ct).ConfigureAwait(false);
        }
        catch (AggregateException ex)
        {
            Console.Error.WriteLine($"ots stamp: {ex.Message}");
            foreach (Exception inner in ex.InnerExceptions)
            {
                Console.Error.WriteLine($"  - {inner.Message}");
            }

            return ExitCode.OperationFailed;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ots stamp: {ex.Message}");
            return ExitCode.OperationFailed;
        }

        dtf.SerializeToFile(outputPath);
        Console.Out.WriteLine($"Stamped {filePath} -> {outputPath}");
        Console.Out.WriteLine(
            $"File digest: {Convert.ToHexString(dtf.FileDigest).ToLowerInvariant()}");
        foreach (var (msg, att) in dtf.Timestamp.AllAttestations())
        {
            if (att is Attestations.PendingAttestation p)
            {
                Console.Out.WriteLine($"  pending calendar: {p.Uri}");
            }
        }

        Console.Out.WriteLine(
            "Run `ots upgrade <proof>` later (typically a few hours) to merge in the Bitcoin attestation.");
        return ExitCode.Success;
    }
}
