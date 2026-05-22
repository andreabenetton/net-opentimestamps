using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class CachingBlockHeaderProviderTests
{
    [Fact]
    public async Task Caches_Result_After_First_Call()
    {
        var inner = new CountingProvider();
        var cache = new CachingBlockHeaderProvider(inner);

        BlockHeader h1 = await cache.GetHeaderAsync(100UL);
        BlockHeader h2 = await cache.GetHeaderAsync(100UL);

        Assert.Equal(1, inner.CallCount);
        Assert.Same(h1, h2);
    }

    [Fact]
    public async Task Different_Heights_Hit_Inner()
    {
        var inner = new CountingProvider();
        var cache = new CachingBlockHeaderProvider(inner);

        await cache.GetHeaderAsync(100UL);
        await cache.GetHeaderAsync(101UL);
        await cache.GetHeaderAsync(102UL);

        Assert.Equal(3, inner.CallCount);
        Assert.Equal(3, cache.CachedCount);
    }

    [Fact]
    public async Task Concurrent_First_Callers_Share_Fetch()
    {
        var inner = new CountingProvider(delay: TimeSpan.FromMilliseconds(50));
        var cache = new CachingBlockHeaderProvider(inner);

        Task<BlockHeader>[] tasks =
        [
            cache.GetHeaderAsync(200UL),
            cache.GetHeaderAsync(200UL),
            cache.GetHeaderAsync(200UL),
        ];
        await Task.WhenAll(tasks);

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public void Propagates_Trust_Category()
    {
        var inner = new CountingProvider();
        var cache = new CachingBlockHeaderProvider(inner);
        Assert.Equal(inner.TrustCategory, cache.TrustCategory);
    }

    private sealed class CountingProvider : BlockHeaderProvider
    {
        private readonly TimeSpan _delay;
        public int CallCount;

        public CountingProvider(TimeSpan delay = default)
        {
            _delay = delay;
        }

        public override TrustCategory TrustCategory => TrustCategory.TrustedHeaders;

        public override string Name => "counting";

        public override async Task<BlockHeader> GetHeaderAsync(
            ulong height, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }

            byte[] merkle = new byte[32];
            return new BlockHeader(height, merkle, DateTimeOffset.UnixEpoch);
        }
    }
}
