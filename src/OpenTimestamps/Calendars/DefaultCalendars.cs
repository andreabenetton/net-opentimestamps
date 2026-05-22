namespace OpenTimestamps.Calendars;

/// <summary>
/// Default calendar endpoints used by the reference clients. These are public
/// aggregators run by OpenTimestamps community operators; callers are free to
/// supply their own list.
/// </summary>
public static class DefaultCalendars
{
    /// <summary>Default aggregator URLs used for stamping (in the reference's documented order).</summary>
    public static IReadOnlyList<string> Aggregators { get; } =
    [
        "https://a.pool.opentimestamps.org",
        "https://b.pool.opentimestamps.org",
        "https://a.pool.eternitywall.com",
        "https://ots.btc.catallaxy.com",
    ];

    /// <summary>The minimum number of calendars that must accept a stamp to consider it successful.</summary>
    public const int DefaultStampQuorum = 2;
}
