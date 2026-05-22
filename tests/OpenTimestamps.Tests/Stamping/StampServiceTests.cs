using System.Net;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Calendars;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;
using OpenTimestamps.Stamping;
using Xunit;

namespace OpenTimestamps.Tests.Stamping;

public sealed class StampServiceTests
{
    [Fact]
    public async Task Stamping_Merges_Calendar_Responses_Into_Tree()
    {
        // Use a deterministic nonce so the commitment is reproducible.
        byte[] nonce = new byte[16];
        for (int i = 0; i < nonce.Length; i++)
        {
            nonce[i] = (byte)i;
        }

        byte[] fileBytes = "hello"u8.ToArray();
        byte[] fileDigest = new OpSha256().Call(fileBytes);
        byte[] noncedMsg = new OpAppend(nonce).Call(fileDigest);
        byte[] commitment = new OpSha256().Call(noncedMsg);

        // Build two distinct calendar response trees, each rooted at commitment.
        byte[] tree1 = BuildPendingResponse(commitment, "https://a.calendar.opentimestamps.org");
        byte[] tree2 = BuildPendingResponse(commitment, "https://b.calendar.opentimestamps.org");

        var handler = new FakeCalendarHandler(new Dictionary<string, byte[]>
        {
            ["https://a.calendar.example/digest"] = tree1,
            ["https://b.calendar.example/digest"] = tree2,
        });

        using var http = new HttpClient(handler);
        var calendars = new[]
        {
            new CalendarClient(http, new Uri("https://a.calendar.example/")),
            new CalendarClient(http, new Uri("https://b.calendar.example/")),
        };

        var svc = new StampService(() => nonce);
        DetachedTimestampFile dtf = await svc.StampDigestAsync(
            fileDigest, new OpSha256(), calendars, quorum: 2);

        Assert.Equal(fileDigest, dtf.FileDigest.ToArray());

        // The commitment node should carry both pending attestations.
        var allPending = new List<string>();
        foreach ((_, TimeAttestation att) in dtf.Timestamp.AllAttestations())
        {
            if (att is PendingAttestation p)
            {
                allPending.Add(p.Uri);
            }
        }

        Assert.Contains("https://a.calendar.opentimestamps.org", allPending);
        Assert.Contains("https://b.calendar.opentimestamps.org", allPending);
    }

    [Fact]
    public async Task Stamping_Below_Quorum_Throws()
    {
        byte[] nonce = new byte[16];
        var handler = new FakeCalendarHandler([]);   // every request 404s
        using var http = new HttpClient(handler);
        var calendars = new[]
        {
            new CalendarClient(http, new Uri("https://a.calendar.example/")),
            new CalendarClient(http, new Uri("https://b.calendar.example/")),
        };
        var svc = new StampService(() => nonce);

        await Assert.ThrowsAsync<AggregateException>(() =>
            svc.StampBytesAsync("data"u8.ToArray(), calendars, quorum: 2));
    }

    private static byte[] BuildPendingResponse(byte[] commitment, string calendarUri)
    {
        var ts = new Timestamp(commitment);
        ts.Attestations.Add(new PendingAttestation(calendarUri));
        using var ms = new MemoryStream();
        ts.Serialize(new OtsWriter(ms));
        return ms.ToArray();
    }

    private sealed class FakeCalendarHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _responses;

        public FakeCalendarHandler(Dictionary<string, byte[]> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string key = request.RequestUri!.ToString();
            if (!_responses.TryGetValue(key, out byte[]? body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
            return Task.FromResult(resp);
        }
    }
}
