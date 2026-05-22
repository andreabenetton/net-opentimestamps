using OpenTimestamps.Attestations;

namespace OpenTimestamps.Verification;

/// <summary>
/// A verified Bitcoin attestation: the block height, its merkle root, the
/// block time, and which provider (and trust category) sourced the header.
/// </summary>
/// <param name="Height">Bitcoin block height of the anchoring block.</param>
/// <param name="BlockTime">
/// The block's <c>nTime</c>, in UTC. The data this proof commits to is
/// asserted by Bitcoin consensus to have existed at or before this block.
/// </param>
/// <param name="ProviderName">The provider that supplied the block header.</param>
/// <param name="TrustCategory">The trust category of that provider.</param>
public sealed record VerifiedAttestation(
    ulong Height,
    DateTimeOffset BlockTime,
    string ProviderName,
    TrustCategory TrustCategory);

/// <summary>
/// Outcome of a verification run.
/// </summary>
public sealed class VerificationResult
{
    public VerificationResult(
        TimestampStatus status,
        IReadOnlyList<VerifiedAttestation> verifiedAttestations,
        IReadOnlyList<BitcoinBlockHeaderAttestation> bitcoinAttestations,
        IReadOnlyList<PendingAttestation> pendingAttestations,
        IReadOnlyList<UnknownAttestation> unknownAttestations,
        IReadOnlyList<string> warnings)
    {
        Status = status;
        VerifiedAttestations = verifiedAttestations;
        BitcoinAttestations = bitcoinAttestations;
        PendingAttestations = pendingAttestations;
        UnknownAttestations = unknownAttestations;
        Warnings = warnings;
    }

    public TimestampStatus Status { get; }

    /// <summary>Bitcoin attestations for which a block header was successfully verified.</summary>
    public IReadOnlyList<VerifiedAttestation> VerifiedAttestations { get; }

    /// <summary>All Bitcoin attestations present in the proof, verified or not.</summary>
    public IReadOnlyList<BitcoinBlockHeaderAttestation> BitcoinAttestations { get; }

    /// <summary>Pending calendar attestations present in the proof.</summary>
    public IReadOnlyList<PendingAttestation> PendingAttestations { get; }

    /// <summary>Attestations whose type tag was not recognised, preserved verbatim from the proof.</summary>
    public IReadOnlyList<UnknownAttestation> UnknownAttestations { get; }

    /// <summary>Non-fatal warnings encountered during verification (e.g. one attestation failed but another succeeded).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// The earliest verified block time. <c>null</c> if no attestation was verified.
    /// </summary>
    public DateTimeOffset? EarliestVerifiedTime =>
        VerifiedAttestations.Count == 0
            ? null
            : VerifiedAttestations.Min(a => a.BlockTime);
}
