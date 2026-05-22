using System.Collections.Concurrent;

namespace OpenTimestamps.Verification;

/// <summary>
/// In-memory caching decorator for any <see cref="BlockHeaderProvider"/>.
/// Lookups for the same block height are served from the cache after the first
/// call.
/// </summary>
/// <remarks>
/// <para>
/// The caching wrapper inherits the inner provider's <see cref="TrustCategory"/>:
/// caching does not change trust assumptions. A confirmed block at a given
/// height is, modulo a chain reorganisation that re-orders blocks, immutable —
/// caching is safe.
/// </para>
/// <para>
/// This implementation has no expiry; it's an in-memory cache for the lifetime
/// of the instance. Callers that want bounded memory should hold one of these
/// per verification batch and discard it when done, or supply a smaller
/// <c>maxEntries</c> at construction.
/// </para>
/// </remarks>
public sealed class CachingBlockHeaderProvider : BlockHeaderProvider
{
    private readonly BlockHeaderProvider _inner;
    private readonly ConcurrentDictionary<ulong, Task<BlockHeader>> _cache = new();
    private readonly int _maxEntries;

    /// <param name="inner">The provider to delegate uncached lookups to.</param>
    /// <param name="maxEntries">
    /// Soft cap on the number of cached entries. When exceeded, the cache is
    /// cleared in one shot (a simple strategy — verification doesn't reuse
    /// random heights, so realistic working sets are small). Defaults to 4096.
    /// </param>
    public CachingBlockHeaderProvider(BlockHeaderProvider inner, int maxEntries = 4096)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        _inner = inner;
        _maxEntries = maxEntries;
    }

    public override TrustCategory TrustCategory => _inner.TrustCategory;

    public override string Name => $"{_inner.Name} (cached)";

    /// <summary>Number of entries currently held in the cache.</summary>
    public int CachedCount => _cache.Count;

    public override Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default)
    {
        // Cache the Task itself so concurrent first-callers share a single fetch.
        Task<BlockHeader> task = _cache.GetOrAdd(
            height,
            (h, state) => state.inner.GetHeaderAsync(h, state.token),
            (inner: _inner, token: cancellationToken));

        if (_cache.Count > _maxEntries)
        {
            _cache.Clear();
            _cache[height] = task;
        }

        return task;
    }
}
