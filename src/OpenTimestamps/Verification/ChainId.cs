namespace OpenTimestamps.Verification;

/// <summary>
/// Discriminator for which chain a verified attestation anchors to.
/// </summary>
public enum ChainId
{
    /// <summary>The Bitcoin blockchain.</summary>
    Bitcoin,

    /// <summary>The Litecoin blockchain.</summary>
    Litecoin,

    /// <summary>The Ethereum blockchain (post-Merge: advisory; see docs/verification-model.md).</summary>
    Ethereum,
}
