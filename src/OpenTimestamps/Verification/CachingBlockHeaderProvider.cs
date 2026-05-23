namespace OpenTimestamps.Verification;

/// <summary>
/// In-memory caching decorator for any <see cref="BlockHeaderProvider"/>.
/// Lookups for the same block height are served from the cache after the first
/// call; concurrent first-callers for the same height share a single underlying
/// fetch.
/// </summary>
/// <remarks>
/// <para>
/// The caching wrapper inherits the inner provider's <see cref="TrustCategory"/>:
/// caching does not change trust assumptions. A confirmed block at a given
/// height is, modulo a chain reorganisation that re-orders blocks, immutable —
/// caching is safe.
/// </para>
/// <para>
/// When the cache exceeds <c>maxEntries</c>, the least-recently-used entry is
/// evicted (LRU). Faulted lookups are not cached — if the inner fetch throws,
/// the entry is removed so the next caller retries.
/// </para>
/// <para>
/// This implementation has no time-based expiry; it's an in-memory cache for
/// the lifetime of the instance.
/// </para>
/// </remarks>
public sealed class CachingBlockHeaderProvider : BlockHeaderProvider
{
    private readonly BlockHeaderProvider _inner;
    private readonly int _maxEntries;
    private readonly object _lock = new();
    private readonly Dictionary<ulong, LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _lru = new();

    /// <param name="inner">The provider to delegate uncached lookups to.</param>
    /// <param name="maxEntries">
    /// Cap on the number of cached entries; LRU eviction once exceeded.
    /// Defaults to 8192 (≈800 days of Bitcoin blocks).
    /// </param>
    public CachingBlockHeaderProvider(BlockHeaderProvider inner, int maxEntries = 8192)
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
    public int CachedCount
    {
        get
        {
            lock (_lock)
            {
                return _index.Count;
            }
        }
    }

    public override async Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default)
    {
        Task<BlockHeader> task;
        lock (_lock)
        {
            if (_index.TryGetValue(height, out LinkedListNode<Entry>? hit))
            {
                // Touch: move to most-recently-used end (front).
                _lru.Remove(hit);
                _lru.AddFirst(hit);
                task = hit.Value.Task;
            }
            else
            {
                // Start the fetch; share the Task so concurrent first-callers
                // for the same height don't both hit the inner provider.
                task = _inner.GetHeaderAsync(height, cancellationToken);
                var entry = new Entry(height, task);
                LinkedListNode<Entry> node = _lru.AddFirst(entry);
                _index[height] = node;

                while (_index.Count > _maxEntries && _lru.Last is { } tail)
                {
                    _lru.RemoveLast();
                    _index.Remove(tail.Value.Height);
                }
            }
        }

        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            // Negative caching is a bug magnet: the next caller deserves a
            // fresh attempt at a transient failure. Cancellations are also
            // dropped here (the caller's own ct cancelled mid-await), which
            // is also the right call — a cancelled fetch leaves no result.
            EvictIfStillHolding(height, task);
            throw;
        }
    }

    private void EvictIfStillHolding(ulong height, Task<BlockHeader> ourTask)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(height, out LinkedListNode<Entry>? node)
                && ReferenceEquals(node.Value.Task, ourTask))
            {
                _lru.Remove(node);
                _index.Remove(height);
            }
        }
    }

    private readonly record struct Entry(ulong Height, Task<BlockHeader> Task);
}
