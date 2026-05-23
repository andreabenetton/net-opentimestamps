namespace OpenTimestamps.Verification;

/// <summary>
/// Source of Litecoin block headers for verification.
/// </summary>
/// <remarks>
/// Litecoin block headers are byte-shape-identical to Bitcoin's (80-byte
/// header with version, prev hash, merkle root, time, bits, nonce). The
/// returned <see cref="BlockHeader"/> uses the same record shape; the chain
/// identity is established by which provider supplied it, surfaced to the
/// caller via <see cref="VerifiedAttestation.Chain"/>.
/// </remarks>
public abstract class LitecoinBlockHeaderProvider
{
    /// <summary>The trust category this provider belongs to.</summary>
    public abstract TrustCategory TrustCategory { get; }

    /// <summary>Short human-readable name used in result objects and logs.</summary>
    public abstract string Name { get; }

    /// <summary>Fetch the header for the Litecoin block at <paramref name="height"/>.</summary>
    /// <param name="height">Litecoin block height.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public abstract Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default);
}
