using System.Net;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Calendars;
using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests.Calendars;

public sealed class CalendarClientTests
{
    private static readonly Uri CalendarBase = new("https://fake.calendar.example/");

    [Fact]
    public async Task SubmitDigest_Rejects_OverSize_Commitment()
    {
        using var http = new HttpClient(new ConstantHandler(HttpStatusCode.OK, []));
        var client = new CalendarClient(http, CalendarBase);

        byte[] tooLong = new byte[CalendarClient.MaxCommitmentSize + 1];
        await Assert.ThrowsAsync<ArgumentException>(() => client.SubmitDigestAsync(tooLong));
    }

    [Fact]
    public async Task SubmitDigest_Rejects_Empty_Commitment()
    {
        using var http = new HttpClient(new ConstantHandler(HttpStatusCode.OK, []));
        var client = new CalendarClient(http, CalendarBase);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SubmitDigestAsync([]));
    }

    [Fact]
    public async Task SubmitDigest_HttpError_Surfaces_CalendarException_With_Status()
    {
        var handler = new ConstantHandler(HttpStatusCode.BadRequest, "bad digest"u8.ToArray());
        using var http = new HttpClient(handler);
        var client = new CalendarClient(http, CalendarBase);

        var ex = await Assert.ThrowsAsync<CalendarException>(
            () => client.SubmitDigestAsync(new byte[32]));
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task SubmitDigest_Rejects_Response_Exceeding_Size_Cap()
    {
        // Calendar reports a body larger than the documented cap. The client
        // must refuse to read it rather than silently truncate.
        byte[] oversized = new byte[CalendarClient.MaxResponseSize + 100];
        var handler = new ConstantHandler(HttpStatusCode.OK, oversized);
        using var http = new HttpClient(handler);
        var client = new CalendarClient(http, CalendarBase);

        await Assert.ThrowsAsync<CalendarException>(() => client.SubmitDigestAsync(new byte[32]));
    }

    [Fact]
    public async Task SubmitDigest_Parses_Pending_Response_Into_Timestamp()
    {
        byte[] commitment = new byte[32];
        for (int i = 0; i < commitment.Length; i++)
        {
            commitment[i] = (byte)i;
        }

        // Construct a valid partial timestamp body (no magic, no version).
        byte[] body = SerializePartialTimestamp(commitment, "https://x.calendar.opentimestamps.org");

        var handler = new ConstantHandler(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        var client = new CalendarClient(http, CalendarBase);

        Timestamp result = await client.SubmitDigestAsync(commitment);
        Assert.Equal(commitment, result.MsgArray());
        Assert.Single(result.Attestations);
        var pending = Assert.IsType<PendingAttestation>(result.Attestations.Single());
        Assert.Equal("https://x.calendar.opentimestamps.org", pending.Uri);
    }

    [Fact]
    public async Task GetTimestamp_404_Returns_Null()
    {
        var handler = new ConstantHandler(HttpStatusCode.NotFound, []);
        using var http = new HttpClient(handler);
        var client = new CalendarClient(http, CalendarBase);

        Timestamp? result = await client.GetTimestampAsync(new byte[32]);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTimestamp_500_Surfaces_CalendarException()
    {
        var handler = new ConstantHandler(HttpStatusCode.InternalServerError, "boom"u8.ToArray());
        using var http = new HttpClient(handler);
        var client = new CalendarClient(http, CalendarBase);

        var ex = await Assert.ThrowsAsync<CalendarException>(
            () => client.GetTimestampAsync(new byte[32]));
        Assert.Equal(500, ex.HttpStatus);
    }

    [Fact]
    public async Task GetTimestamp_200_Parses_Body_With_Commitment_As_Initial_Msg()
    {
        byte[] commitment = new byte[32];
        commitment[0] = 0xAA;
        byte[] body = SerializePartialTimestamp(commitment, "https://y.calendar.opentimestamps.org");

        var handler = new ConstantHandler(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        var client = new CalendarClient(http, CalendarBase);

        Timestamp? result = await client.GetTimestampAsync(commitment);
        Assert.NotNull(result);
        Assert.Equal(commitment, result!.MsgArray());
    }

    private static byte[] SerializePartialTimestamp(byte[] commitment, string calendarUri)
    {
        var ts = new Timestamp(commitment);
        ts.Attestations.Add(new PendingAttestation(calendarUri));
        using var ms = new MemoryStream();
        ts.Serialize(new OtsWriter(ms));
        return ms.ToArray();
    }

    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;

        public ConstantHandler(HttpStatusCode status, byte[] body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(_body),
            });
        }
    }
}
