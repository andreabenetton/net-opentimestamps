using OpenTimestamps;
using OpenTimestamps.Calendars;
using OpenTimestamps.Stamping;

namespace OpenTimestamps.Cli.Commands;

internal static class StampCommand
{
    public const string Usage =
        "usage: ots stamp <file>... [--calendar <url>]... [--quorum N] [--output <file.ots>]\n" +
        "       --output is honoured only when stamping a single file; batch invocations\n" +
        "       always write each file's proof next to it as <file>.ots.";

    public static async Task<int> RunAsync(string[] args, HttpClient http, CancellationToken ct)
    {
        var parser = new ArgParser("stamp", args)
            .Option("--calendar")
            .Option("--quorum")
            .Option("--output");
        parser.Parse();

        if (parser.Positionals.Count < 1)
        {
            throw new CliUsageException("stamp", Usage);
        }

        IReadOnlyList<string> filePaths = parser.Positionals;
        foreach (string p in filePaths)
        {
            if (!File.Exists(p))
            {
                Console.Error.WriteLine($"ots stamp: file not found: {p}");
                return ExitCode.OperationFailed;
            }
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

        string? explicitOutput = parser.GetOption("--output");
        if (explicitOutput is not null && filePaths.Count > 1)
        {
            Console.Error.WriteLine(
                "ots stamp: --output is honoured only when stamping a single file; " +
                "batch invocations write each file's proof next to it.");
            return ExitCode.UsageError;
        }

        // Precompute output paths and refuse if any already exists.
        var outputPaths = new string[filePaths.Count];
        for (int i = 0; i < filePaths.Count; i++)
        {
            outputPaths[i] = (filePaths.Count == 1 && explicitOutput is not null)
                ? explicitOutput
                : filePaths[i] + ".ots";

            if (File.Exists(outputPaths[i]))
            {
                Console.Error.WriteLine($"ots stamp: refusing to overwrite existing {outputPaths[i]}");
                return ExitCode.OperationFailed;
            }
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
        IReadOnlyList<DetachedTimestampFile> dtfs;
        try
        {
            dtfs = await svc.StampManyAsync(filePaths, calendars, quorum, ct).ConfigureAwait(false);
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

        for (int i = 0; i < dtfs.Count; i++)
        {
            dtfs[i].SerializeToFile(outputPaths[i]);
            Console.Out.WriteLine($"Stamped {filePaths[i]} -> {outputPaths[i]}");
            Console.Out.WriteLine(
                $"  file digest: {Convert.ToHexString(dtfs[i].FileDigest).ToLowerInvariant()}");
        }

        // Calendar pending list is identical across all DTFs in a batch
        // (they share the merkle root the calendar replied to). Print once.
        if (dtfs.Count > 0)
        {
            var seenUris = new HashSet<string>();
            foreach (var (_, att) in dtfs[0].Timestamp.AllAttestations())
            {
                if (att is Attestations.PendingAttestation p && seenUris.Add(p.Uri))
                {
                    Console.Out.WriteLine($"  pending calendar: {p.Uri}");
                }
            }
        }

        Console.Out.WriteLine(
            "Run `ots upgrade <proof>` later (typically a few hours) to merge in the Bitcoin attestation.");
        return ExitCode.Success;
    }
}
