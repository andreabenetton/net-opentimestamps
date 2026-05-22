using OpenTimestamps;
using OpenTimestamps.Verification;

namespace OpenTimestamps.Cli.Commands;

internal static class VerifyCommand
{
    public const string Usage =
        "usage: ots verify <file> [--proof <file.ots>] [--explorer <url> | --bitcoin-rpc <url> [--rpc-user U --rpc-password P]]";

    public static async Task<int> RunAsync(string[] args, HttpClient http, CancellationToken ct)
    {
        var parser = new ArgParser("verify", args)
            .Option("--proof")
            .Option("--explorer")
            .Option("--bitcoin-rpc")
            .Option("--rpc-user")
            .Option("--rpc-password");
        parser.Parse();

        if (parser.Positionals.Count != 1)
        {
            throw new CliUsageException("verify", Usage);
        }

        string filePath = parser.Positionals[0];
        string proofPath = parser.GetOption("--proof") ?? filePath + ".ots";

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"ots verify: file not found: {filePath}");
            return ExitCode.OperationFailed;
        }

        if (!File.Exists(proofPath))
        {
            Console.Error.WriteLine($"ots verify: proof not found: {proofPath}");
            return ExitCode.OperationFailed;
        }

        DetachedTimestampFile dtf;
        try
        {
            dtf = DetachedTimestampFile.DeserializeFromFile(proofPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ots verify: failed to parse {proofPath}: {ex.Message}");
            return ExitCode.OperationFailed;
        }

        BlockHeaderProvider? provider = BuildProvider(parser, http);
        if (provider is null && (parser.GetOption("--explorer") is not null
                                  || parser.GetOption("--bitcoin-rpc") is not null))
        {
            // Build error already reported by BuildProvider via exception.
            return ExitCode.UsageError;
        }

        var svc = new VerificationService();
        VerificationResult result;
        try
        {
            result = await svc.VerifyFileAsync(dtf, filePath, provider, ct).ConfigureAwait(false);
        }
        catch (FileDigestMismatchException ex)
        {
            Console.Error.WriteLine($"ots verify: {ex.Message}");
            return ExitCode.VerificationFailed;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ots verify: {ex.Message}");
            return ExitCode.OperationFailed;
        }

        return PrintAndExit(result, provider, filePath);
    }

    private static BlockHeaderProvider? BuildProvider(ArgParser parser, HttpClient http)
    {
        string? explorer = parser.GetOption("--explorer");
        string? rpc = parser.GetOption("--bitcoin-rpc");

        if (explorer is not null && rpc is not null)
        {
            throw new CliUsageException("verify",
                "Specify at most one of --explorer or --bitcoin-rpc.");
        }

        if (explorer is not null)
        {
            return new EsploraBlockHeaderProvider(http, new Uri(explorer, UriKind.Absolute));
        }

        if (rpc is not null)
        {
            return new BitcoinCoreRpcBlockHeaderProvider(
                http,
                new Uri(rpc, UriKind.Absolute),
                parser.GetOption("--rpc-user"),
                parser.GetOption("--rpc-password"));
        }

        return null;
    }

    private static int PrintAndExit(VerificationResult result, BlockHeaderProvider? provider, string filePath)
    {
        switch (result.Status)
        {
            case TimestampStatus.Incomplete:
                Console.Out.WriteLine($"INCOMPLETE: {filePath} is not yet anchored in Bitcoin.");
                foreach (var p in result.PendingAttestations)
                {
                    Console.Out.WriteLine($"  pending calendar: {p.Uri}");
                }

                Console.Out.WriteLine("Try `ots upgrade <proof>` later to resolve pending attestations.");
                return ExitCode.VerificationFailed;

            case TimestampStatus.Anchored:
                Console.Out.WriteLine(
                    $"ANCHORED: {filePath} contains Bitcoin attestations but no block-header source " +
                    "was configured. Re-run with --explorer or --bitcoin-rpc to verify against headers.");
                foreach (var b in result.BitcoinAttestations)
                {
                    Console.Out.WriteLine($"  bitcoin block {b.Height}");
                }

                return ExitCode.VerificationFailed;

            case TimestampStatus.Verified:
                {
                    DateTimeOffset? earliest = result.EarliestVerifiedTime;
                    Console.Out.WriteLine(
                        $"VERIFIED: {filePath} existed at or before " +
                        $"{earliest!.Value.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC.");
                    foreach (var v in result.VerifiedAttestations)
                    {
                        Console.Out.WriteLine(
                            $"  bitcoin block {v.Height} time {v.BlockTime.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC " +
                            $"(source: {v.ProviderName}, trust: {v.TrustCategory})");
                    }

                    foreach (string w in result.Warnings)
                    {
                        Console.Out.WriteLine($"  warning: {w}");
                    }

                    if (provider?.TrustCategory == TrustCategory.Explorer)
                    {
                        Console.Out.WriteLine(
                            "  note: verification used a block explorer (Explorer trust category); " +
                            "for fully trustless verification, use a Bitcoin Core node.");
                    }

                    return ExitCode.Success;
                }

            default:
                return ExitCode.OperationFailed;
        }
    }
}
