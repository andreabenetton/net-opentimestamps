namespace OpenTimestamps.Verification;

/// <summary>
/// A pluggable persistent store for <see cref="BlockHeader"/> records,
/// composed with <see cref="CachingBlockHeaderProvider"/> to survive
/// process restarts.
/// </summary>
/// <remarks>
/// The store is correctness-preserving: it records the merkle root the
/// inner provider observed and replays it on lookup. Trust category is
/// inherited from the inner provider — caching does not change trust
/// assumptions. If the inner provider was an <c>Explorer</c>, the cached
/// entries remain <c>Explorer</c>-trusted forever.
/// </remarks>
public interface IHeaderCacheStore
{
    /// <summary>Look up a previously persisted header. Returns null on miss.</summary>
    BlockHeader? TryGet(ulong height);

    /// <summary>Persist a header so a future process can recall it.</summary>
    void Put(BlockHeader header);
}
