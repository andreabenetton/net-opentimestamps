using OpenTimestamps.Attestations;

namespace OpenTimestamps.Verification;

/// <summary>
/// Walks a detached timestamp proof and verifies each Bitcoin attestation
/// against block headers supplied by a <see cref="BlockHeaderProvider"/>.
/// </summary>
public sealed class VerificationService
{
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

        return await VerifyParsedAsync(dtf, provider, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<VerificationResult> VerifyParsedAsync(
        DetachedTimestampFile dtf,
        BlockHeaderProvider? provider,
        CancellationToken cancellationToken)
    {
        var verified = new List<VerifiedAttestation>();
        var bitcoinAtts = new List<BitcoinBlockHeaderAttestation>();
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
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        warnings.Add(
                            $"Failed to verify Bitcoin attestation at block {bitcoin.Height} " +
                            $"via {provider.Name}: {ex.Message}");
                    }

                    break;

                case LitecoinBlockHeaderAttestation:
                case EthereumBlockHeaderAttestation:
                    // Not verified by this library; treat as informational.
                    warnings.Add($"Skipping non-Bitcoin attestation: {attestation}.");
                    break;

                case UnknownAttestation u:
                    unknownAtts.Add(u);
                    break;
            }
        }

        TimestampStatus status = verified.Count > 0
            ? TimestampStatus.Verified
            : bitcoinAtts.Count > 0
                ? TimestampStatus.Anchored
                : TimestampStatus.Incomplete;

        return new VerificationResult(
            status,
            verified,
            bitcoinAtts,
            pendingAtts,
            unknownAtts,
            warnings);
    }
}
