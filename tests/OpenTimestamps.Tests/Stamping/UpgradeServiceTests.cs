using System.Net;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Calendars;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;
using OpenTimestamps.Stamping;
using Xunit;

namespace OpenTimestamps.Tests.Stamping;

public sealed class UpgradeServiceTests
{
    [Fact]
    public async Task Off_Whitelist_Pending_Uri_Is_Skipped_Not_Contacted()
    {
        var handler = new TrackingHandler();
        using var http = new HttpClient(handler);

        DetachedTimestampFile dtf = BuildPendingDtf("https://malicious.example.com");
        // Use the default whitelist — does NOT contain malicious.example.com.
        var svc = new UpgradeService(
            CalendarWhitelist.Default,
            uri => new CalendarClient(http, uri));

        UpgradeResult result = await svc.UpgradeAsync(dtf);

        Assert.Empty(result.Resolved);
        Assert.Single(result.Skipped);
        Assert.Contains("malicious.example.com", result.Skipped[0]);
        Assert.Equal(0, handler.RequestCount);  // never contacted
    }

    [Fact]
    public async Task Still_Pending_404_Reports_StillPending_No_Mutation()
    {
        var handler = new NotFoundHandler();
        using var http = new HttpClient(handler);

        const string uri = "https://alice.btc.calendar.opentimestamps.org";
        DetachedTimestampFile dtf = BuildPendingDtf(uri);
        int originalAttestations = dtf.Timestamp.Attestations.Count;
        int originalOps = dtf.Timestamp.Ops.Count;

        var svc = new UpgradeService(
            CalendarWhitelist.Default,
            u => new CalendarClient(http, u));

        UpgradeResult result = await svc.UpgradeAsync(dtf);

        Assert.Empty(result.Resolved);
        Assert.Single(result.StillPending);
        Assert.False(result.ChangedAnything);
        // Original tree must be untouched.
        Assert.Equal(originalAttestations, dtf.Timestamp.Attestations.Count);
        Assert.Equal(originalOps, dtf.Timestamp.Ops.Count);
    }

    [Fact]
    public async Task Calendar_5xx_Captured_As_Error_Not_Crash()
    {
        var handler = new ConstantHandler(HttpStatusCode.InternalServerError);
        using var http = new HttpClient(handler);

        DetachedTimestampFile dtf = BuildPendingDtf("https://alice.btc.calendar.opentimestamps.org");

        var svc = new UpgradeService(
            CalendarWhitelist.Default,
            uri => new CalendarClient(http, uri));

        UpgradeResult result = await svc.UpgradeAsync(dtf);

        Assert.Single(result.Errors);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public async Task Visited_Pairs_Are_Not_Refetched_Within_One_Call()
    {
        // After a successful merge the pending attestation is preserved on the
        // node. The visited-set must prevent the outer loop from re-issuing
        // the same GET. (This is the regression that the e2e test surfaced.)
        const string uri = "https://alice.btc.calendar.opentimestamps.org";
        byte[] commitment = new byte[32];
        commitment[0] = 0x42;

        byte[] upgradeBody = BuildUpgradeBody(commitment);
        var handler = new CountingResponseHandler(HttpStatusCode.OK, upgradeBody);

        using var http = new HttpClient(handler);
        DetachedTimestampFile dtf = BuildPendingDtfWithExactMsg(commitment, uri);

        var svc = new UpgradeService(
            CalendarWhitelist.Default,
            u => new CalendarClient(http, u));

        UpgradeResult result = await svc.UpgradeAsync(dtf);

        Assert.True(result.ChangedAnything);
        Assert.Equal(1, handler.RequestCount);   // exactly one fetch, no loop
    }

    private static DetachedTimestampFile BuildPendingDtf(string calendarUri)
    {
        byte[] digest = new byte[32];
        var ts = new Timestamp(digest);
        ts.Attestations.Add(new PendingAttestation(calendarUri));
        return new DetachedTimestampFile(new OpSha256(), ts);
    }

    private static DetachedTimestampFile BuildPendingDtfWithExactMsg(byte[] msg, string calendarUri)
    {
        var ts = new Timestamp(msg);
        ts.Attestations.Add(new PendingAttestation(calendarUri));
        return new DetachedTimestampFile(new OpSha256(), ts);
    }

    private static byte[] BuildUpgradeBody(byte[] commitment)
    {
        // commitment --OpSHA256--> child with BitcoinBlockHeaderAttestation(100)
        var root = new Timestamp(commitment);
        var child = new Timestamp(new OpSha256().Call(commitment));
        child.Attestations.Add(new BitcoinBlockHeaderAttestation(100));
        root.Ops[new OpSha256()] = child;

        using var ms = new MemoryStream();
        root.Serialize(new OtsWriter(ms));
        return ms.ToArray();
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public int RequestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RequestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public ConstantHandler(HttpStatusCode status)
        {
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent("boom"u8.ToArray()),
            });
    }

    private sealed class CountingResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;
        public int RequestCount;

        public CountingResponseHandler(HttpStatusCode status, byte[] body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RequestCount);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(_body),
            });
        }
    }
}
