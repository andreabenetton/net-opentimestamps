using System.Text.RegularExpressions;

namespace OpenTimestamps.Calendars;

/// <summary>
/// A wildcard-glob URL whitelist. Patterns use <c>*</c> to match any
/// non-separator character sequence in the host name, e.g.
/// <c>https://*.calendar.opentimestamps.org</c>.
/// </summary>
/// <remarks>
/// Mirrors <c>opentimestamps/calendar.py</c>'s <c>UrlWhitelist</c>.
/// </remarks>
public sealed class CalendarWhitelist
{
    /// <summary>The set of default whitelist patterns from the reference client.</summary>
    public static readonly IReadOnlyList<string> DefaultPatterns =
    [
        "https://*.calendar.opentimestamps.org",
        "https://*.calendar.eternitywall.com",
        "https://*.calendar.catallaxy.com",
    ];

    private readonly Regex[] _patterns;

    public CalendarWhitelist(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _patterns = patterns.Select(GlobToRegex).ToArray();
    }

    /// <summary>A whitelist matching only the reference defaults.</summary>
    public static CalendarWhitelist Default { get; } = new(DefaultPatterns);

    /// <summary>Whether <paramref name="uri"/> is permitted by any pattern in this whitelist.</summary>
    public bool IsAllowed(string uri)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        foreach (Regex p in _patterns)
        {
            if (p.IsMatch(uri))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex GlobToRegex(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var sb = new System.Text.StringBuilder();
        sb.Append('^');
        foreach (char ch in pattern)
        {
            if (ch == '*')
            {
                sb.Append("[^/]*");
            }
            else
            {
                sb.Append(Regex.Escape(ch.ToString()));
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
