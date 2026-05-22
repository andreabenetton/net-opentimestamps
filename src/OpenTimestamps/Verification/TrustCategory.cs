namespace OpenTimestamps.Verification;

/// <summary>
/// How much the caller is trusting the source that produced a block header.
/// Every <see cref="BlockHeaderProvider"/> declares its category, and
/// verification results carry that category forward to the API surface so
/// callers can decide whether the verification meets their security bar.
/// </summary>
public enum TrustCategory
{
    /// <summary>
    /// Bitcoin Core (or another fully-validating node the user controls).
    /// Trustless given the node is honest.
    /// </summary>
    LocalNode,

    /// <summary>
    /// User-supplied block-header data — e.g. a static <c>headers.dat</c>
    /// mirror or an SPV header chain the caller has independently validated.
    /// </summary>
    TrustedHeaders,

    /// <summary>
    /// Public block explorer (Esplora, Blockstream.info, etc.).
    /// <strong>Not trustless</strong>; the caller is relying on a third party
    /// to report the correct merkle root.
    /// </summary>
    Explorer,
}
