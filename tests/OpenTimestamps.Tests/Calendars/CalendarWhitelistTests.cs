using OpenTimestamps.Calendars;
using Xunit;

namespace OpenTimestamps.Tests.Calendars;

public sealed class CalendarWhitelistTests
{
    [Theory]
    [InlineData("https://alice.btc.calendar.opentimestamps.org", true)]
    [InlineData("https://b.calendar.opentimestamps.org", true)]
    [InlineData("https://finney.calendar.eternitywall.com", true)]
    [InlineData("https://ots.calendar.catallaxy.com", true)]
    [InlineData("http://alice.btc.calendar.opentimestamps.org", false)]   // http, not https
    [InlineData("https://malicious.example.com", false)]
    [InlineData("https://calendar.opentimestamps.org", false)]            // missing subdomain
    public void Default_Patterns_Allow_Or_Deny(string uri, bool allowed)
    {
        Assert.Equal(allowed, CalendarWhitelist.Default.IsAllowed(uri));
    }

    [Fact]
    public void Custom_Pattern_Allows_Local_Calendar()
    {
        var wl = new CalendarWhitelist(["http://localhost:8080"]);
        Assert.True(wl.IsAllowed("http://localhost:8080"));
        Assert.False(wl.IsAllowed("http://localhost:8081"));
    }

    [Fact]
    public void Wildcards_Do_Not_Match_Path_Separators()
    {
        var wl = new CalendarWhitelist(["https://*.example.com"]);
        Assert.True(wl.IsAllowed("https://api.example.com"));
        // Disallow path injection — wildcard should not match across '/'
        Assert.False(wl.IsAllowed("https://api.example.com/foo"));
    }
}
