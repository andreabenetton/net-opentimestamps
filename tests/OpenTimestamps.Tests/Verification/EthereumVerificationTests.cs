using System.Security.Cryptography;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Ops;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class EthereumVerificationTests
{
    [Fact]
    public async Task Verified_When_Commitment_Matches_Ethereum_Transactions_Root()
    {
        byte[] seed = "world"u8.ToArray();
        byte[] digest = SHA256.HashData(seed);

        DetachedTimestampFile dtf = BuildDtfWithEthereumAttRootedAt(digest, 18_000_000UL);
        var provider = new FixedEthereumProvider(digest);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyMultiChainAsync(
            dtf, seed, new VerifyOptions { EthereumProvider = provider });

        Assert.Equal(TimestampStatus.Verified, result.Status);
        VerifiedAttestation v = Assert.Single(result.VerifiedAttestations);
        Assert.Equal(ChainId.Ethereum, v.Chain);
        Assert.Equal(18_000_000UL, v.Height);
        Assert.Equal(TrustCategory.Explorer, v.TrustCategory);
        Assert.Single(result.EthereumAttestations);
    }

    [Fact]
    public async Task Mismatch_Reports_Anchored_With_Warning()
    {
        byte[] seed = "world"u8.ToArray();
        byte[] digest = SHA256.HashData(seed);
        byte[] wrong = new byte[32];
        wrong[1] = 0xCC;

        DetachedTimestampFile dtf = BuildDtfWithEthereumAttRootedAt(digest, 18_000_000UL);
        var provider = new FixedEthereumProvider(wrong);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyMultiChainAsync(
            dtf, seed, new VerifyOptions { EthereumProvider = provider });

        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.Single(result.EthereumAttestations);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("Ethereum", StringComparison.Ordinal)
                 && w.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_Ethereum_Provider_Means_Ethereum_Attestation_Tracked_But_Not_Verified()
    {
        byte[] seed = "world"u8.ToArray();
        byte[] digest = SHA256.HashData(seed);
        DetachedTimestampFile dtf = BuildDtfWithEthereumAttRootedAt(digest, 1UL);

        var svc = new VerificationService();
        VerificationResult result = await svc.VerifyMultiChainAsync(
            dtf, seed, new VerifyOptions());

        Assert.Equal(TimestampStatus.Anchored, result.Status);
        Assert.Single(result.EthereumAttestations);
        Assert.Empty(result.VerifiedAttestations);
    }

    private static DetachedTimestampFile BuildDtfWithEthereumAttRootedAt(byte[] digest, ulong height)
    {
        var ts = new Timestamp(digest);
        ts.Attestations.Add(new EthereumBlockHeaderAttestation(height));
        return new DetachedTimestampFile(new OpSha256(), ts);
    }

    private sealed class FixedEthereumProvider : EthereumBlockHeaderProvider
    {
        private readonly byte[] _root;

        public FixedEthereumProvider(byte[] root)
        {
            _root = root;
        }

        public override TrustCategory TrustCategory => TrustCategory.Explorer;

        public override string Name => "fixed-eth";

        public override Task<BlockHeader> GetHeaderAsync(
            ulong height, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new BlockHeader(height, (byte[])_root.Clone(),
                                DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)));
    }
}
