using System.Net;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Calendars;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;
using OpenTimestamps.Stamping;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests;

/// <summary>
/// End-to-end flow test: stamp → verify (incomplete) → upgrade → verify (verified)
/// using fake calendars and a TrustedHeaders block-header provider.
/// </summary>
public sealed class EndToEndFlowTests
{
    [Fact]
    public async Task Stamp_Then_Upgrade_Then_Verify_Succeeds()
    {
        // Deterministic nonce so the commitment is reproducible.
        byte[] nonce = new byte[16];
        for (int i = 0; i < nonce.Length; i++)
        {
            nonce[i] = (byte)(0xA0 + i);
        }

        byte[] fileBytes = "hello world"u8.ToArray();
        byte[] fileDigest = new OpSha256().Call(fileBytes);
        byte[] noncedMsg = new OpAppend(nonce).Call(fileDigest);
        byte[] commitment = new OpSha256().Call(noncedMsg);

        const string calendarUri = "https://alice.btc.calendar.opentimestamps.org";
        const ulong blockHeight = 100;

        // The upgrade response chains one more SHA-256 op from the commitment
        // and attaches a Bitcoin block-header attestation at the resulting msg.
        byte[] anchoredMsg = new OpSha256().Call(commitment);

        byte[] stampResponse = BuildStampResponse(commitment, calendarUri);
        byte[] upgradeResponse = BuildUpgradeResponse(commitment, anchoredMsg, blockHeight);

        var handler = new FakeRoutingHandler();
        handler.WhenPost("https://fake.example.com/digest", stampResponse);
        // The upgrade GET URL is derived from the PendingAttestation URI plus the commitment hex.
        string upgradeUrl = $"{calendarUri}/timestamp/{Convert.ToHexString(commitment).ToLowerInvariant()}";
        handler.WhenGet(upgradeUrl, upgradeResponse);

        using var http = new HttpClient(handler);

        // --- Step 1: stamp.
        var stampSvc = new StampService(() => nonce);
        DetachedTimestampFile stamped = await stampSvc.StampDigestAsync(
            fileDigest,
            new OpSha256(),
            new[] { new CalendarClient(http, new Uri("https://fake.example.com/")) },
            quorum: 1);

        // --- Step 2: verify before upgrade → Incomplete.
        var verifySvc = new VerificationService();
        var headersBefore = new Dictionary<ulong, BlockHeader>
        {
            [blockHeight] = new BlockHeader(blockHeight, anchoredMsg, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)),
        };
        var headerProvider = new TrustedHeadersBlockHeaderProvider(headersBefore, "fake-headers");

        VerificationResult resultBefore = await verifySvc.VerifyAsync(stamped, fileBytes, headerProvider);
        Assert.Equal(TimestampStatus.Incomplete, resultBefore.Status);
        Assert.NotEmpty(resultBefore.PendingAttestations);

        // --- Step 3: upgrade.
        var customWhitelist = new CalendarWhitelist(
            [.. CalendarWhitelist.DefaultPatterns, calendarUri]);
        var upgradeSvc = new UpgradeService(
            customWhitelist,
            uri => new CalendarClient(http, uri));

        UpgradeResult upgrade = await upgradeSvc.UpgradeAsync(stamped);
        Assert.True(upgrade.ChangedAnything);
        Assert.Single(upgrade.Resolved);
        Assert.Contains(calendarUri, upgrade.Resolved);

        // --- Step 4: verify after upgrade → Verified.
        VerificationResult resultAfter = await verifySvc.VerifyAsync(stamped, fileBytes, headerProvider);
        Assert.Equal(TimestampStatus.Verified, resultAfter.Status);
        Assert.Single(resultAfter.VerifiedAttestations);
        Assert.Equal(blockHeight, resultAfter.VerifiedAttestations[0].Height);
        Assert.Equal(TrustCategory.TrustedHeaders, resultAfter.VerifiedAttestations[0].TrustCategory);

        // --- Step 5: parse-then-serialize round-trip is still byte-identical.
        byte[] bytes = stamped.SerializeToArray();
        DetachedTimestampFile reparsed = DetachedTimestampFile.DeserializeFromArray(bytes);
        Assert.Equal(bytes, reparsed.SerializeToArray());
    }

    private static byte[] BuildStampResponse(byte[] commitment, string calendarUri)
    {
        var ts = new Timestamp(commitment);
        ts.Attestations.Add(new PendingAttestation(calendarUri));
        using var ms = new MemoryStream();
        ts.Serialize(new OtsWriter(ms));
        return ms.ToArray();
    }

    private static byte[] BuildUpgradeResponse(byte[] commitment, byte[] anchoredMsg, ulong blockHeight)
    {
        // Tree: commitment --OpSHA256--> anchoredMsg (with BitcoinBlockHeaderAttestation)
        var root = new Timestamp(commitment);
        var child = new Timestamp(anchoredMsg);
        child.Attestations.Add(new BitcoinBlockHeaderAttestation(blockHeight));
        root.Ops[new OpSha256()] = child;

        using var ms = new MemoryStream();
        root.Serialize(new OtsWriter(ms));
        return ms.ToArray();
    }

    private sealed class FakeRoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _post = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _get = new(StringComparer.Ordinal);

        public void WhenPost(string url, byte[] body) => _post[url] = body;

        public void WhenGet(string url, byte[] body) => _get[url] = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string key = request.RequestUri!.ToString();
            Dictionary<string, byte[]> table = request.Method == HttpMethod.Post ? _post : _get;

            if (!table.TryGetValue(key, out byte[]? body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }
}
