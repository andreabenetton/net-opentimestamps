namespace OpenTimestamps.Verification;

/// <summary>
/// Source of Ethereum block headers for verification.
/// </summary>
/// <remarks>
/// <para>
/// Returns a synthetic <see cref="BlockHeader"/> for compatibility with the
/// existing record. For Ethereum, <see cref="BlockHeader.MerkleRoot"/> carries
/// the header field that the OTS Ethereum attestation commits to (Ethash-era:
/// the <c>mixHash</c>). The semantics are advisory post-Merge — see
/// <c>docs/verification-model.md</c>.
/// </para>
/// <para>
/// Concrete implementation ships in a follow-up commit; the abstract class is
/// introduced here so multi-chain verification surfaces are stable from the
/// start.
/// </para>
/// </remarks>
public abstract class EthereumBlockHeaderProvider
{
    /// <summary>The trust category this provider belongs to.</summary>
    public abstract TrustCategory TrustCategory { get; }

    /// <summary>Short human-readable name used in result objects and logs.</summary>
    public abstract string Name { get; }

    /// <summary>Fetch the header for the Ethereum block at <paramref name="height"/>.</summary>
    /// <param name="height">Ethereum block number.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public abstract Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default);
}
