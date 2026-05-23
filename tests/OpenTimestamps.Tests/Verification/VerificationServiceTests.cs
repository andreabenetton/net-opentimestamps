using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Ops;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class VerificationServiceTests
{
    [Fact]
    public async Task File_Hash_Mismatch_Throws_Typed()
    {
        var dtf = BuildAnchoredTimestamp("the original", out _, out _);
        byte[] differentBytes = "not the original"u8.ToArray();

        var svc = new VerificationService();
        var ex = await Assert.ThrowsAsync<FileDigestMismatchException>(
            () => svc.VerifyAsync(dtf, differentBytes, provider: null));

        Assert.Equal(32, ex.ExpectedDigest.Length);
        Assert.Equal(32, ex.ActualDigest.Length);
        Assert.NotEqual(ex.ExpectedDigest, ex.ActualDigest);
    }

    [Fact]
    public async Task No_Provider_With_Bitcoin_Attestation_Reports_Anchored()
    {
        var dtf = BuildAnchoredTimestamp("hello", out byte[] fileBytes, out _);
        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyAsync(dtf, fileBytes, provider: null);

        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.Empty(result.VerifiedAttestations);
        Assert.NotEmpty(result.BitcoinAttestations);
        Assert.Null(result.EarliestVerifiedTime);
    }

    [Fact]
    public async Task Only_Pending_Reports_Incomplete()
    {
        var dtf = BuildPendingOnlyTimestamp(
            "hello", "https://alice.btc.calendar.opentimestamps.org", out byte[] fileBytes);
        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyAsync(dtf, fileBytes, provider: null);

        Assert.Equal(TimestampStatus.Incomplete, result.Status);
        Assert.Single(result.PendingAttestations);
        Assert.Empty(result.BitcoinAttestations);
    }

    [Fact]
    public async Task Merkle_Root_Mismatch_Becomes_Warning_Not_Verified()
    {
        var dtf = BuildAnchoredTimestamp("hello", out byte[] fileBytes, out byte[] anchoredMsg);
        byte[] wrongRoot = new byte[32];
        wrongRoot[0] = 0xFF;  // Definitely not the real merkle root.

        var headers = new Dictionary<ulong, BlockHeader>
        {
            [800000] = new BlockHeader(800000, wrongRoot, DateTimeOffset.UnixEpoch),
        };
        var provider = new TrustedHeadersBlockHeaderProvider(headers);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyAsync(dtf, fileBytes, provider);

        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.Empty(result.VerifiedAttestations);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("merkle root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Provider_Failure_Becomes_Warning_Not_Crash()
    {
        var dtf = BuildAnchoredTimestamp("hello", out byte[] fileBytes, out _);
        var provider = new ThrowingProvider();

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyAsync(dtf, fileBytes, provider);

        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.Empty(result.VerifiedAttestations);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("simulated"));
    }

    [Fact]
    public async Task Litecoin_Attestation_Surfaces_In_Dedicated_List()
    {
        // Behaviour change from pre-MC.1: Litecoin attestations are now
        // first-class. With no Litecoin provider supplied they still appear
        // in result.LitecoinAttestations (counted, but not verified) and
        // contribute to the Anchored status — they no longer pollute
        // Warnings with a "skipping" message.
        byte[] fileBytes = "hello"u8.ToArray();
        byte[] fileDigest = new OpSha256().Call(fileBytes);
        var ts = new Timestamp(fileDigest);
        ts.Attestations.Add(new LitecoinBlockHeaderAttestation(123));
        var dtf = new DetachedTimestampFile(new OpSha256(), ts);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyAsync(dtf, fileBytes, provider: null);

        Assert.Single(result.LitecoinAttestations);
        Assert.Equal(123UL, result.LitecoinAttestations[0].Height);
        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.DoesNotContain(
            result.Warnings,
            w => w.Contains("Skipping non-Bitcoin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Successful_Verify_Surfaces_Trust_Category()
    {
        var dtf = BuildAnchoredTimestamp("hello", out byte[] fileBytes, out byte[] anchoredMsg);
        var headers = new Dictionary<ulong, BlockHeader>
        {
            [800000] = new BlockHeader(800000, anchoredMsg, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)),
        };
        var provider = new TrustedHeadersBlockHeaderProvider(headers, "test-headers");

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyAsync(dtf, fileBytes, provider);

        Assert.Equal(TimestampStatus.Verified, result.Status);
        Assert.Single(result.VerifiedAttestations);
        Assert.Equal(TrustCategory.TrustedHeaders, result.VerifiedAttestations[0].TrustCategory);
        Assert.Equal("test-headers", result.VerifiedAttestations[0].ProviderName);
    }

    /// <summary>
    /// Build a DetachedTimestampFile whose tree is: file_digest --OpSHA256-->
    /// (anchored msg) with a BitcoinBlockHeaderAttestation at height 800000.
    /// </summary>
    private static DetachedTimestampFile BuildAnchoredTimestamp(
        string contents, out byte[] fileBytes, out byte[] anchoredMsg)
    {
        fileBytes = System.Text.Encoding.UTF8.GetBytes(contents);
        byte[] fileDigest = new OpSha256().Call(fileBytes);
        anchoredMsg = new OpSha256().Call(fileDigest);

        var root = new Timestamp(fileDigest);
        var child = new Timestamp(anchoredMsg);
        child.Attestations.Add(new BitcoinBlockHeaderAttestation(800000));
        root.Ops[new OpSha256()] = child;

        return new DetachedTimestampFile(new OpSha256(), root);
    }

    private static DetachedTimestampFile BuildPendingOnlyTimestamp(
        string contents, string calendarUri, out byte[] fileBytes)
    {
        fileBytes = System.Text.Encoding.UTF8.GetBytes(contents);
        byte[] fileDigest = new OpSha256().Call(fileBytes);

        var root = new Timestamp(fileDigest);
        root.Attestations.Add(new PendingAttestation(calendarUri));

        return new DetachedTimestampFile(new OpSha256(), root);
    }

    private sealed class ThrowingProvider : BlockHeaderProvider
    {
        public override TrustCategory TrustCategory => TrustCategory.Explorer;
        public override string Name => "throwing";

        public override Task<BlockHeader> GetHeaderAsync(
            ulong height, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated provider failure");
    }
}
