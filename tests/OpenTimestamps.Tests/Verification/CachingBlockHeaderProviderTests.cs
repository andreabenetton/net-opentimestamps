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

    [Fact]
    public async Task LRU_Evicts_Least_Recently_Used_When_Over_Cap()
    {
        var inner = new CountingProvider();
        var cache = new CachingBlockHeaderProvider(inner, maxEntries: 3);

        await cache.GetHeaderAsync(1UL);  // [1]
        await cache.GetHeaderAsync(2UL);  // [2,1]
        await cache.GetHeaderAsync(3UL);  // [3,2,1]
        await cache.GetHeaderAsync(1UL);  // touch 1 → [1,3,2]
        await cache.GetHeaderAsync(4UL);  // [4,1,3,2] over cap → evict 2 → [4,1,3]

        Assert.Equal(3, cache.CachedCount);
        Assert.Equal(4, inner.CallCount);

        // Hitting 2 again is a miss (was evicted)
        await cache.GetHeaderAsync(2UL);
        Assert.Equal(5, inner.CallCount);

        // Hitting 1 again is a hit (still in cache)
        await cache.GetHeaderAsync(1UL);
        Assert.Equal(5, inner.CallCount);
    }

    [Fact]
    public async Task Faulted_Fetch_Is_Not_Cached_Retry_Reruns_Inner()
    {
        var inner = new CountingProvider { FailUntilCallCount = 2 };
        var cache = new CachingBlockHeaderProvider(inner);

        // First call fails; the cache must NOT remember the failure.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetHeaderAsync(500UL));

        // Second call: same height, inner succeeds this time.
        BlockHeader ok = await cache.GetHeaderAsync(500UL);
        Assert.Equal(500UL, ok.Height);
        Assert.Equal(2, inner.CallCount);

        // Third call: now cached, no further inner hit.
        await cache.GetHeaderAsync(500UL);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Fifty_Parallel_Callers_Same_Height_Triggers_Exactly_One_Inner_Fetch()
    {
        var inner = new CountingProvider(delay: TimeSpan.FromMilliseconds(20));
        var cache = new CachingBlockHeaderProvider(inner);

        Task<BlockHeader>[] tasks = new Task<BlockHeader>[50];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = cache.GetHeaderAsync(800_000UL);
        }

        BlockHeader[] results = await Task.WhenAll(tasks);
        Assert.Equal(1, inner.CallCount);
        Assert.All(results, r => Assert.Equal(800_000UL, r.Height));
    }

    private sealed class CountingProvider : BlockHeaderProvider
    {
        private readonly TimeSpan _delay;
        public int CallCount;
        public int FailUntilCallCount;

        public CountingProvider(TimeSpan delay = default)
        {
            _delay = delay;
        }

        public override TrustCategory TrustCategory => TrustCategory.TrustedHeaders;

        public override string Name => "counting";

        public override async Task<BlockHeader> GetHeaderAsync(
            ulong height, CancellationToken cancellationToken = default)
        {
            int n = Interlocked.Increment(ref CallCount);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }

            if (n < FailUntilCallCount)
            {
                throw new InvalidOperationException($"injected failure on call {n}");
            }

            byte[] merkle = new byte[32];
            return new BlockHeader(height, merkle, DateTimeOffset.UnixEpoch);
        }
    }
}
