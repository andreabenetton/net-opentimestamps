using System.Security.Cryptography;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Ops;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class LitecoinVerificationTests
{
    [Fact]
    public async Task Verified_When_Commitment_Matches_Litecoin_Merkle_Root()
    {
        // Build a proof rooted at SHA256("hello") with a Litecoin attestation
        // directly on the root node. The "merkle root" the provider returns
        // must equal that root's commitment for the verification to succeed.
        byte[] seed = "hello"u8.ToArray();
        byte[] digest = SHA256.HashData(seed);

        DetachedTimestampFile dtf = BuildDtfWithLitecoinAttRootedAt(digest, 2_500_000UL);
        var provider = new FixedLitecoinProvider(digest);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyMultiChainAsync(
            dtf,
            fileBytes: seed,
            new VerifyOptions { LitecoinProvider = provider });

        Assert.Equal(TimestampStatus.Verified, result.Status);
        VerifiedAttestation v = Assert.Single(result.VerifiedAttestations);
        Assert.Equal(ChainId.Litecoin, v.Chain);
        Assert.Equal(2_500_000UL, v.Height);
        Assert.Equal("fixed", v.ProviderName);
        Assert.Equal(TrustCategory.Explorer, v.TrustCategory);
        Assert.Single(result.LitecoinAttestations);
    }

    [Fact]
    public async Task Mismatch_Reports_Anchored_With_Warning_Not_Verified()
    {
        byte[] seed = "hello"u8.ToArray();
        byte[] digest = SHA256.HashData(seed);

        // Provider returns a *different* merkle root than what the proof commits to.
        byte[] wrong = new byte[32];
        wrong[0] = 0xAA;

        DetachedTimestampFile dtf = BuildDtfWithLitecoinAttRootedAt(digest, 2_500_000UL);
        var provider = new FixedLitecoinProvider(wrong);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyMultiChainAsync(
            dtf, seed, new VerifyOptions { LitecoinProvider = provider });

        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.Empty(result.VerifiedAttestations);
        Assert.Single(result.LitecoinAttestations);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("Litecoin", StringComparison.Ordinal)
                 && w.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_Litecoin_Provider_Means_Litecoin_Skipped_Status_Anchored()
    {
        byte[] seed = "hello"u8.ToArray();
        byte[] digest = SHA256.HashData(seed);
        DetachedTimestampFile dtf = BuildDtfWithLitecoinAttRootedAt(digest, 1UL);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyMultiChainAsync(
            dtf, seed, new VerifyOptions());

        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.Single(result.LitecoinAttestations);
        Assert.Empty(result.VerifiedAttestations);
    }

    private static DetachedTimestampFile BuildDtfWithLitecoinAttRootedAt(byte[] digest, ulong height)
    {
        var ts = new Timestamp(digest);
        ts.Attestations.Add(new LitecoinBlockHeaderAttestation(height));
        return new DetachedTimestampFile(new OpSha256(), ts);
    }

    private sealed class FixedLitecoinProvider : LitecoinBlockHeaderProvider
    {
        private readonly byte[] _merkle;

        public FixedLitecoinProvider(byte[] merkle)
        {
            _merkle = merkle;
        }

        public override TrustCategory TrustCategory => TrustCategory.Explorer;

        public override string Name => "fixed";

        public override Task<BlockHeader> GetHeaderAsync(
            ulong height, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new BlockHeader(height, (byte[])_merkle.Clone(),
                                DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)));
    }
}
