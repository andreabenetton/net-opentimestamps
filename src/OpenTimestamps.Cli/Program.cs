using System.Reflection;
using OpenTimestamps.Cli;
using OpenTimestamps.Cli.Commands;

string assemblyVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(30),
};
http.DefaultRequestHeaders.UserAgent.ParseAdd($"net-opentimestamps-cli/{assemblyVersion}");

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintGlobalHelp();
    return args.Length == 0 ? ExitCode.UsageError : ExitCode.Success;
}

if (args[0] is "-V" or "--version")
{
    Console.Out.WriteLine($"ots {assemblyVersion}");
    return ExitCode.Success;
}

string command = args[0];
string[] rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "info" => InfoCommand.Run(rest),
        "verify" => await VerifyCommand.RunAsync(rest, http, cts.Token).ConfigureAwait(false),
        "stamp" => await StampCommand.RunAsync(rest, http, cts.Token).ConfigureAwait(false),
        "upgrade" => await UpgradeCommand.RunAsync(rest, http, cts.Token).ConfigureAwait(false),
        "help" or "-h" or "--help" => Help(),
        _ => UnknownCommand(command),
    };
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine($"ots {ex.Command}: {ex.Message}");
    return ExitCode.UsageError;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("ots: cancelled.");
    return ExitCode.OperationFailed;
}

static int Help()
{
    PrintGlobalHelp();
    return ExitCode.Success;
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"ots: unknown command '{cmd}'.");
    PrintGlobalHelp();
    return ExitCode.UsageError;
}

static void PrintGlobalHelp()
{
    var lines = new[]
    {
        "ots — OpenTimestamps client",
        string.Empty,
        "Commands:",
        "  ots stamp   <file> [--calendar URL]... [--quorum N] [--output PATH]",
        "  ots verify  <file> [--proof PATH] [--explorer URL | --bitcoin-rpc URL [--rpc-user U --rpc-password P]]",
        "  ots upgrade <proof.ots> [--allow-calendar PATTERN]... [--no-backup]",
        "  ots info    <proof.ots>",
        string.Empty,
        "Exit codes:",
        "  0 — success / verified",
        "  1 — operation failed (parse error, network error, calendar refused)",
        "  2 — proof structurally valid but verification did not succeed",
        "  3 — usage / argument error",
    };

    foreach (string line in lines)
    {
        Console.Out.WriteLine(line);
    }
}

internal partial class Program
{
}
