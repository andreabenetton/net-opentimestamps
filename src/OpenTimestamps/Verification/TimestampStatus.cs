namespace OpenTimestamps.Verification;

/// <summary>
/// The overall verification status of a detached timestamp file.
/// </summary>
public enum TimestampStatus
{
    /// <summary>
    /// The proof contains only pending calendar attestations; no Bitcoin
    /// anchor has been observed yet. The file's existence is not yet
    /// timestamped on-chain.
    /// </summary>
    Incomplete,

    /// <summary>
    /// The proof contains at least one Bitcoin block-header attestation, but
    /// no <see cref="BlockHeaderProvider"/> was supplied to verify against,
    /// so the merkle-root match was not checked.
    /// </summary>
    Anchored,

    /// <summary>
    /// At least one Bitcoin block-header attestation was verified against
    /// a block header from a configured <see cref="BlockHeaderProvider"/>.
    /// </summary>
    Verified,
}
