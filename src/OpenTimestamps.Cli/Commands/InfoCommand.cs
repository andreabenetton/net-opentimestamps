using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Ops;

namespace OpenTimestamps.Cli.Commands;

internal static class InfoCommand
{
    public const string Usage = "usage: ots info <file.ots>";

    public static int Run(string[] args)
    {
        var parser = new ArgParser("info", args);
        parser.Parse();
        if (parser.Positionals.Count != 1)
        {
            throw new CliUsageException("info", Usage);
        }

        string path = parser.Positionals[0];
        DetachedTimestampFile dtf;
        try
        {
            dtf = DetachedTimestampFile.DeserializeFromFile(path);
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"ots info: file not found: {path}");
            return ExitCode.OperationFailed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"ots info: cannot read {path}: {ex.Message}");
            return ExitCode.OperationFailed;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ots info: failed to parse {path}: {ex.Message}");
            return ExitCode.OperationFailed;
        }

        Console.Out.WriteLine($"File hash op: {dtf.FileHashOp.Name}");
        Console.Out.WriteLine($"File digest:  {Convert.ToHexString(dtf.FileDigest).ToLowerInvariant()}");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Proof tree:");
        DumpTimestamp(dtf.Timestamp, indent: 2);
        return ExitCode.Success;
    }

    private static void DumpTimestamp(Timestamp ts, int indent)
    {
        string pad = new(' ', indent);
        Console.Out.WriteLine($"{pad}msg = {Convert.ToHexString(ts.Msg).ToLowerInvariant()}");
        foreach (TimeAttestation att in ts.Attestations.OrderBy(a => a))
        {
            Console.Out.WriteLine($"{pad}# {DescribeAttestation(att)}");
        }

        foreach (KeyValuePair<Op, Timestamp> kvp in ts.Ops.OrderBy(static k => k.Key))
        {
            Console.Out.WriteLine($"{pad}-> {kvp.Key}");
            DumpTimestamp(kvp.Value, indent + 4);
        }
    }

    private static string DescribeAttestation(TimeAttestation att) => att switch
    {
        PendingAttestation p => $"pending calendar {p.Uri}",
        BitcoinBlockHeaderAttestation b => $"bitcoin block {b.Height}",
        LitecoinBlockHeaderAttestation l => $"litecoin block {l.Height}",
        EthereumBlockHeaderAttestation e => $"ethereum block {e.Height}",
        UnknownAttestation u => $"unknown attestation tag=0x{Convert.ToHexString(u.Tag).ToLowerInvariant()} " +
                                $"payload={u.Payload.Length} bytes",
        _ => att.ToString() ?? "(unknown)",
    };
}
