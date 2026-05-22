namespace OpenTimestamps.Cli;

/// <summary>
/// Tiny POSIX-style argument parser for the four <c>ots</c> commands. We avoid
/// taking a dependency on the still-beta <c>System.CommandLine</c> package.
/// </summary>
internal sealed class ArgParser
{
    private readonly string _command;
    private readonly Queue<string> _args;
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly List<string> _positionals = [];

    public ArgParser(string command, IEnumerable<string> args)
    {
        _command = command;
        _args = new Queue<string>(args);
    }

    /// <summary>Register a known flag (no value).</summary>
    public ArgParser Flag(string name)
    {
        _flags.Add(name);
        return this;
    }

    /// <summary>Register a known option (takes a value, may repeat).</summary>
    public ArgParser Option(string name)
    {
        _options[name] = [];
        return this;
    }

    public void Parse()
    {
        while (_args.Count > 0)
        {
            string a = _args.Dequeue();
            if (a == "--")
            {
                while (_args.Count > 0)
                {
                    _positionals.Add(_args.Dequeue());
                }

                break;
            }

            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                string name;
                string? inline = null;
                int eq = a.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    name = a[..eq];
                    inline = a[(eq + 1)..];
                }
                else
                {
                    name = a;
                }

                if (_flags.Contains(name))
                {
                    if (inline is not null)
                    {
                        throw new CliUsageException(_command, $"Flag {name} does not take a value.");
                    }

                    _flags.Remove(name); // sentinel: set when present
                    _flags.Add(name + ".present");
                    _flags.Add(name);
                    continue;
                }

                if (_options.TryGetValue(name, out List<string>? list))
                {
                    string value;
                    if (inline is not null)
                    {
                        value = inline;
                    }
                    else if (_args.Count == 0)
                    {
                        throw new CliUsageException(_command, $"Option {name} requires a value.");
                    }
                    else
                    {
                        value = _args.Dequeue();
                    }

                    list.Add(value);
                    continue;
                }

                throw new CliUsageException(_command, $"Unknown option {name}.");
            }

            _positionals.Add(a);
        }
    }

    public IReadOnlyList<string> Positionals => _positionals;

    public bool HasFlag(string name) => _flags.Contains(name + ".present");

    public string? GetOption(string name) =>
        _options.TryGetValue(name, out List<string>? list) && list.Count > 0
            ? list[^1]
            : null;

    public IReadOnlyList<string> GetOptions(string name) =>
        _options.TryGetValue(name, out List<string>? list) ? list : [];
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string command, string message) : base(message)
    {
        Command = command;
    }

    public string Command { get; }
}
