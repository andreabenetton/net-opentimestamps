namespace OpenTimestamps.Verification;

/// <summary>
/// Bundle of optional per-chain providers passed to
/// <see cref="VerificationService.VerifyMultiChainAsync(OpenTimestamps.DetachedTimestampFile, byte[], VerifyOptions, System.Threading.CancellationToken)"/>
/// and its file overload.
/// </summary>
/// <remarks>
/// Each provider is independent; pass only the ones you have. If you only
/// care about Bitcoin verification, prefer the existing
/// <see cref="VerificationService.VerifyAsync(OpenTimestamps.DetachedTimestampFile, byte[], BlockHeaderProvider?, System.Threading.CancellationToken)"/>
/// overload — this options object exists so multi-chain callers don't need
/// a new positional parameter every time a new chain is supported.
/// </remarks>
public sealed class VerifyOptions
{
    /// <summary>Source of Bitcoin block headers (null = don't verify Bitcoin attestations).</summary>
    public BlockHeaderProvider? BitcoinProvider { get; init; }

    /// <summary>Source of Litecoin block headers (null = don't verify Litecoin attestations).</summary>
    public LitecoinBlockHeaderProvider? LitecoinProvider { get; init; }

    /// <summary>Source of Ethereum block headers (null = don't verify Ethereum attestations).</summary>
    public EthereumBlockHeaderProvider? EthereumProvider { get; init; }
}
