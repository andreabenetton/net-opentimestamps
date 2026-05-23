using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class FileBackedHeaderCacheStoreTests
{
    [Fact]
    public void Persists_And_Reloads_Records_Across_Instances()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ots-cache-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var store = new FileBackedHeaderCacheStore(path))
            {
                byte[] m1 = new byte[32]; m1[0] = 0xAA;
                byte[] m2 = new byte[32]; m2[0] = 0xBB;
                store.Put(new BlockHeader(800_000UL, m1, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)));
                store.Put(new BlockHeader(800_001UL, m2, DateTimeOffset.FromUnixTimeSeconds(1_700_000_600)));
                Assert.Equal(2, store.Count);
            }

            using var reloaded = new FileBackedHeaderCacheStore(path);
            Assert.Equal(2, reloaded.Count);

            BlockHeader? h1 = reloaded.TryGet(800_000UL);
            Assert.NotNull(h1);
            Assert.Equal(0xAA, h1!.MerkleRoot[0]);
            Assert.Equal(1_700_000_000L, h1.Time.ToUnixTimeSeconds());

            Assert.Null(reloaded.TryGet(999_999UL));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Skips_Malformed_Lines_Rather_Than_Throwing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ots-cache-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(path, [
                "garbage not json",
                "{}",
                "{\"height\":42,\"merkleRoot\":\"deadbeef\",\"time\":1}",  // too short
                "{\"height\":42,\"merkleRoot\":\"" + new string('0', 64) + "\",\"time\":1}",  // valid
            ]);

            using var store = new FileBackedHeaderCacheStore(path);
            Assert.Equal(1, store.Count);
            Assert.NotNull(store.TryGet(42UL));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Composes_With_CachingBlockHeaderProvider_Write_Through()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ots-cache-{Guid.NewGuid():N}.jsonl");
        try
        {
            var counting = new CountingProvider();

            // First run: inner provider supplies + cache writes through to file.
            using (var store = new FileBackedHeaderCacheStore(path))
            {
                var cache = new CachingBlockHeaderProvider(counting, store: store);
                await cache.GetHeaderAsync(123_456UL);
                Assert.Equal(1, counting.CallCount);
                Assert.Equal(1, store.Count);
            }

            // Second run: fresh process — store hit, inner provider untouched.
            using (var store = new FileBackedHeaderCacheStore(path))
            {
                var cache = new CachingBlockHeaderProvider(counting, store: store);
                BlockHeader h = await cache.GetHeaderAsync(123_456UL);
                Assert.Equal(123_456UL, h.Height);
                Assert.Equal(1, counting.CallCount);  // still 1 — store served the hit
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class CountingProvider : BlockHeaderProvider
    {
        public int CallCount;

        public override TrustCategory TrustCategory => TrustCategory.LocalNode;

        public override string Name => "counting";

        public override Task<BlockHeader> GetHeaderAsync(
            ulong height, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            byte[] merkle = new byte[32];
            merkle[0] = (byte)(height & 0xFF);
            return Task.FromResult(new BlockHeader(height, merkle, DateTimeOffset.UnixEpoch));
        }
    }
}
