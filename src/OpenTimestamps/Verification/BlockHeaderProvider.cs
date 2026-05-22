namespace OpenTimestamps.Verification;

/// <summary>
/// Source of Bitcoin block headers. Implementations <strong>must</strong>
/// declare their <see cref="TrustCategory"/> so that verification results
/// expose the trust assumption to the caller.
/// </summary>
public abstract class BlockHeaderProvider
{
    /// <summary>The trust category of this provider.</summary>
    public abstract TrustCategory TrustCategory { get; }

    /// <summary>A human-readable identifier (used in CLI output and logs).</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Fetch the block header at <paramref name="height"/>. Throws if the
    /// header is unavailable.
    /// </summary>
    public abstract Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default);
}
