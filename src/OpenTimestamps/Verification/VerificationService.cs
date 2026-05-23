using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTimestamps.Attestations;

namespace OpenTimestamps.Verification;

/// <summary>
/// Walks a detached timestamp proof and verifies each Bitcoin attestation
/// against block headers supplied by a <see cref="BlockHeaderProvider"/>.
/// </summary>
public sealed class VerificationService
{
    private readonly ILogger _logger;

    public VerificationService(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Verify <paramref name="dtf"/> against the candidate <paramref name="fileBytes"/>.
    /// </summary>
    /// <param name="dtf">The detached timestamp proof.</param>
    /// <param name="fileBytes">The bytes of the file being claimed.</param>
    /// <param name="provider">
    /// Block-header source. If <c>null</c>, only the file digest is checked and
    /// the result reports <see cref="TimestampStatus.Anchored"/> or
    /// <see cref="TimestampStatus.Incomplete"/>; the Bitcoin attestation's
    /// merkle root match is not checked.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dtf"/> or <paramref name="fileBytes"/> is null.</exception>
    /// <exception cref="FileDigestMismatchException">
    /// The hash of <paramref name="fileBytes"/> under <c>dtf.FileHashOp</c> does
    /// not equal <c>dtf.FileDigest</c>; the proof is not for this file.
    /// </exception>
    public async Task<VerificationResult> VerifyAsync(
        DetachedTimestampFile dtf,
        byte[] fileBytes,
        BlockHeaderProvider? provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dtf);
        ArgumentNullException.ThrowIfNull(fileBytes);

        byte[] expected = dtf.FileDigest.ToArray();
        byte[] actual = dtf.FileHashOp.Call(fileBytes);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new FileDigestMismatchException(expected, actual);
        }

        return await VerifyParsedAsync(dtf, provider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify <paramref name="dtf"/> against the candidate file on disk at
    /// <paramref name="filePath"/>, hashing the file in a streaming fashion.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="dtf"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is null or empty.</exception>
    /// <exception cref="IOException">Reading <paramref name="filePath"/> failed.</exception>
    /// <exception cref="FileDigestMismatchException">
    /// The hash of the file at <paramref name="filePath"/> does not match
    /// <c>dtf.FileDigest</c>; the proof is not for this file.
    /// </exception>
    public async Task<VerificationResult> VerifyFileAsync(
        DetachedTimestampFile dtf,
        string filePath,
        BlockHeaderProvider? provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dtf);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        byte[] expected = dtf.FileDigest.ToArray();
        byte[] actual = dtf.FileHashOp.HashFile(filePath);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new FileDigestMismatchException(expected, actual);
        }

        return await VerifyParsedAsync(
            dtf, provider, litecoin: null, ethereum: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Multi-chain verification: takes optional Bitcoin, Litecoin, and
    /// Ethereum providers via <see cref="VerifyOptions"/>. Bitcoin-only callers
    /// should keep using <see cref="VerifyAsync(DetachedTimestampFile, byte[], BlockHeaderProvider?, CancellationToken)"/>.
    /// </summary>
    public async Task<VerificationResult> VerifyMultiChainAsync(
        DetachedTimestampFile dtf,
        byte[] fileBytes,
        VerifyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dtf);
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentNullException.ThrowIfNull(options);

        byte[] expected = dtf.FileDigest.ToArray();
        byte[] actual = dtf.FileHashOp.Call(fileBytes);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new FileDigestMismatchException(expected, actual);
        }

        return await VerifyParsedAsync(
            dtf,
            options.BitcoinProvider,
            options.LitecoinProvider,
            options.EthereumProvider,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Multi-chain file verification. See <see cref="VerifyMultiChainAsync(DetachedTimestampFile, byte[], VerifyOptions, CancellationToken)"/>.
    /// </summary>
    public async Task<VerificationResult> VerifyFileMultiChainAsync(
        DetachedTimestampFile dtf,
        string filePath,
        VerifyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dtf);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(options);

        byte[] expected = dtf.FileDigest.ToArray();
        byte[] actual = dtf.FileHashOp.HashFile(filePath);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new FileDigestMismatchException(expected, actual);
        }

        return await VerifyParsedAsync(
            dtf,
            options.BitcoinProvider,
            options.LitecoinProvider,
            options.EthereumProvider,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<VerificationResult> VerifyParsedAsync(
        DetachedTimestampFile dtf,
        BlockHeaderProvider? provider,
        CancellationToken cancellationToken)
    {
        return await VerifyParsedAsync(
            dtf, provider, litecoin: null, ethereum: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VerificationResult> VerifyParsedAsync(
        DetachedTimestampFile dtf,
        BlockHeaderProvider? provider,
        LitecoinBlockHeaderProvider? litecoin,
        EthereumBlockHeaderProvider? ethereum,
        CancellationToken cancellationToken)
    {
        var verified = new List<VerifiedAttestation>();
        var bitcoinAtts = new List<BitcoinBlockHeaderAttestation>();
        var litecoinAtts = new List<LitecoinBlockHeaderAttestation>();
        var ethereumAtts = new List<EthereumBlockHeaderAttestation>();
        var pendingAtts = new List<PendingAttestation>();
        var unknownAtts = new List<UnknownAttestation>();
        var warnings = new List<string>();

        foreach ((byte[] msg, TimeAttestation attestation) in dtf.Timestamp.AllAttestations())
        {
            switch (attestation)
            {
                case PendingAttestation p:
                    pendingAtts.Add(p);
                    break;

                case BitcoinBlockHeaderAttestation bitcoin:
                    bitcoinAtts.Add(bitcoin);
                    if (provider is null)
                    {
                        break;
                    }

                    try
                    {
                        BlockHeader header = await provider
                            .GetHeaderAsync(bitcoin.Height, cancellationToken)
                            .ConfigureAwait(false);

                        if (msg.Length != 32)
                        {
                            warnings.Add(
                                $"Bitcoin attestation at block {bitcoin.Height} has wrong commitment length " +
                                $"({msg.Length} bytes); expected 32.");
                            break;
                        }

                        if (!msg.AsSpan().SequenceEqual(header.MerkleRoot))
                        {
                            warnings.Add(
                                $"Bitcoin attestation at block {bitcoin.Height} commitment does not match " +
                                $"merkle root reported by {provider.Name}.");
                            break;
                        }

                        verified.Add(new VerifiedAttestation(
                            bitcoin.Height,
                            header.Time,
                            provider.Name,
                            provider.TrustCategory));
                        _logger.LogInformation(
                            "Verified Bitcoin attestation at block {Height} via {Provider} ({Trust})",
                            bitcoin.Height, provider.Name, provider.TrustCategory);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(
                            ex,
                            "Bitcoin attestation verification failed at block {Height} via {Provider}",
                            bitcoin.Height, provider.Name);
                        warnings.Add(
                            $"Failed to verify Bitcoin attestation at block {bitcoin.Height} " +
                            $"via {provider.Name}: {ex.Message}");
                    }

                    break;

                case LitecoinBlockHeaderAttestation ltc:
                    litecoinAtts.Add(ltc);
                    if (litecoin is null)
                    {
                        // Not verified — caller didn't supply a Litecoin provider.
                        break;
                    }

                    await TryVerifyChainAsync(
                        chain: ChainId.Litecoin,
                        height: ltc.Height,
                        msg: msg,
                        fetch: ct => litecoin.GetHeaderAsync(ltc.Height, ct),
                        providerName: litecoin.Name,
                        providerTrust: litecoin.TrustCategory,
                        verified, warnings,
                        cancellationToken).ConfigureAwait(false);
                    break;

                case EthereumBlockHeaderAttestation eth:
                    ethereumAtts.Add(eth);
                    if (ethereum is null)
                    {
                        break;
                    }

                    await TryVerifyChainAsync(
                        chain: ChainId.Ethereum,
                        height: eth.Height,
                        msg: msg,
                        fetch: ct => ethereum.GetHeaderAsync(eth.Height, ct),
                        providerName: ethereum.Name,
                        providerTrust: ethereum.TrustCategory,
                        verified, warnings,
                        cancellationToken).ConfigureAwait(false);
                    break;

                case UnknownAttestation u:
                    unknownAtts.Add(u);
                    break;
            }
        }

        TimestampStatus status = verified.Count > 0
            ? TimestampStatus.Verified
            : (bitcoinAtts.Count > 0 || litecoinAtts.Count > 0 || ethereumAtts.Count > 0)
                ? TimestampStatus.Anchored
                : TimestampStatus.Incomplete;

        return new VerificationResult(
            status,
            verified,
            bitcoinAtts,
            pendingAtts,
            unknownAtts,
            warnings,
            litecoinAtts,
            ethereumAtts);
    }

    private async Task TryVerifyChainAsync(
        ChainId chain,
        ulong height,
        byte[] msg,
        Func<CancellationToken, Task<BlockHeader>> fetch,
        string providerName,
        TrustCategory providerTrust,
        List<VerifiedAttestation> verified,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            BlockHeader header = await fetch(cancellationToken).ConfigureAwait(false);

            if (msg.Length != 32)
            {
                warnings.Add(
                    $"{chain} attestation at block {height} has wrong commitment length " +
                    $"({msg.Length} bytes); expected 32.");
                return;
            }

            if (!msg.AsSpan().SequenceEqual(header.MerkleRoot))
            {
                warnings.Add(
                    $"{chain} attestation at block {height} commitment does not match " +
                    $"merkle root reported by {providerName}.");
                return;
            }

            verified.Add(new VerifiedAttestation(
                height,
                header.Time,
                providerName,
                providerTrust) { Chain = chain });
            _logger.LogInformation(
                "Verified {Chain} attestation at block {Height} via {Provider} ({Trust})",
                chain, height, providerName, providerTrust);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "{Chain} attestation verification failed at block {Height} via {Provider}",
                chain, height, providerName);
            warnings.Add(
                $"Failed to verify {chain} attestation at block {height} " +
                $"via {providerName}: {ex.Message}");
        }
    }
}
