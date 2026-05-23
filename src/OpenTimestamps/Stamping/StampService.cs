using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTimestamps.Calendars;
using OpenTimestamps.Ops;

namespace OpenTimestamps.Stamping;

/// <summary>
/// Builds a fresh detached timestamp proof for a file by hashing it,
/// applying a privacy nonce, and submitting the commitment to one or more
/// calendar servers.
/// </summary>
/// <remarks>
/// <para>
/// The privacy nonce is mandatory. The reference flow is:
/// <c>file_digest → OpAppend(random16) → OpSHA256 → commitment</c>. Without the
/// per-stamp nonce, two unrelated stamps could be cross-linked via their
/// shared aggregator path. Do not expose a public stamping API that omits this
/// step.
/// </para>
/// </remarks>
public sealed class StampService
{
    /// <summary>Length of the privacy nonce, matching the reference.</summary>
    public const int NonceLength = 16;

    private readonly Func<byte[]> _nonceProvider;
    private readonly ILogger _logger;

    /// <param name="nonceProvider">
    /// Source of nonce bytes. Defaults to a cryptographically secure RNG. Tests
    /// may inject a deterministic source.
    /// </param>
    /// <param name="logger">Optional <see cref="ILogger"/> for structured diagnostics; defaults to <see cref="NullLogger"/>.</param>
    public StampService(Func<byte[]>? nonceProvider = null, ILogger? logger = null)
    {
        _nonceProvider = nonceProvider ?? GenerateSecureNonce;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Stamp a file on disk using the supplied calendars. Hashes with SHA-256.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is null or empty, or other argument validation fails (see <see cref="StampDigestAsync"/>).</exception>
    /// <exception cref="IOException">Reading <paramref name="filePath"/> failed.</exception>
    /// <exception cref="AggregateException">Fewer than <paramref name="quorum"/> calendars accepted the stamp; inner exceptions hold each calendar failure.</exception>
    public async Task<DetachedTimestampFile> StampFileAsync(
        string filePath,
        IEnumerable<CalendarClient> calendars,
        int quorum = DefaultCalendars.DefaultStampQuorum,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var fileHashOp = new OpSha256();
        byte[] digest = fileHashOp.HashFile(filePath);
        return await StampDigestAsync(digest, fileHashOp, calendars, quorum, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stamp an arbitrary byte buffer using the supplied calendars. Hashes with SHA-256.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException">Argument validation fails (see <see cref="StampDigestAsync"/>).</exception>
    /// <exception cref="AggregateException">Fewer than <paramref name="quorum"/> calendars accepted the stamp.</exception>
    public async Task<DetachedTimestampFile> StampBytesAsync(
        byte[] data,
        IEnumerable<CalendarClient> calendars,
        int quorum = DefaultCalendars.DefaultStampQuorum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var fileHashOp = new OpSha256();
        byte[] digest = fileHashOp.Call(data);
        return await StampDigestAsync(digest, fileHashOp, calendars, quorum, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stamp a pre-computed digest. The chosen <paramref name="fileHashOp"/>
    /// must be the operation that produced <paramref name="digest"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="digest"/>'s length doesn't match <paramref name="fileHashOp"/>'s
    /// digest length, or <paramref name="calendars"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="quorum"/> is less than 1 or greater than the calendar count.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Fewer than <paramref name="quorum"/> calendars accepted the stamp; the
    /// inner exceptions hold per-calendar failures (typically <see cref="CalendarException"/>).
    /// </exception>
    public async Task<DetachedTimestampFile> StampDigestAsync(
        byte[] digest,
        CryptOp fileHashOp,
        IEnumerable<CalendarClient> calendars,
        int quorum = DefaultCalendars.DefaultStampQuorum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(fileHashOp);
        ArgumentNullException.ThrowIfNull(calendars);
        if (digest.Length != fileHashOp.DigestLength)
        {
            throw new ArgumentException(
                $"Digest length {digest.Length} does not match {fileHashOp.Name} digest length " +
                $"{fileHashOp.DigestLength}.",
                nameof(digest));
        }

        CalendarClient[] calendarList = [.. calendars];
        if (calendarList.Length == 0)
        {
            throw new ArgumentException("At least one calendar is required to stamp.", nameof(calendars));
        }

        if (quorum < 1 || quorum > calendarList.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quorum),
                quorum,
                $"Quorum must be between 1 and {calendarList.Length}.");
        }

        // Privacy nonce path: file_digest → OpAppend(nonce) → OpSHA256 → commitment.
        byte[] nonce = _nonceProvider();
        if (nonce.Length != NonceLength)
        {
            throw new InvalidOperationException(
                $"Nonce provider returned {nonce.Length} bytes; expected {NonceLength}.");
        }

        var nonceAppend = new OpAppend(nonce);
        byte[] noncedMsg = nonceAppend.Call(digest);

        var commitSha256 = new OpSha256();
        byte[] commitment = commitSha256.Call(noncedMsg);

        // Submit to all calendars in parallel; bail with the first quorum successes.
        Task<(CalendarClient Calendar, Timestamp? Response, Exception? Error)>[] tasks =
            calendarList.Select(async cal =>
            {
                try
                {
                    Timestamp response = await cal
                        .SubmitDigestAsync(commitment, cancellationToken)
                        .ConfigureAwait(false);
                    return (Calendar: cal, Response: (Timestamp?)response, Error: (Exception?)null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (Calendar: cal, Response: (Timestamp?)null, Error: (Exception?)ex);
                }
            }).ToArray();

        var responses = new List<(CalendarClient Calendar, Timestamp Response)>();
        var errors = new List<Exception>();
        foreach (Task<(CalendarClient Calendar, Timestamp? Response, Exception? Error)> t in tasks)
        {
            var outcome = await t.ConfigureAwait(false);
            if (outcome.Response is not null)
            {
                responses.Add((outcome.Calendar, outcome.Response));
            }
            else if (outcome.Error is not null)
            {
                errors.Add(outcome.Error);
            }
        }

        if (responses.Count < quorum)
        {
            _logger.LogError(
                "Stamp quorum not met: {Accepted}/{Total} accepted, quorum {Quorum}; {Errors} errors",
                responses.Count, calendarList.Length, quorum, errors.Count);
            throw new AggregateException(
                $"Only {responses.Count} of {calendarList.Length} calendars accepted the stamp; " +
                $"quorum was {quorum}.",
                errors);
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Stamp succeeded with quorum {Quorum} but {Errors} calendar(s) failed",
                quorum, errors.Count);
        }

        _logger.LogDebug(
            "Stamp accepted by {Accepted}/{Total} calendars",
            responses.Count, calendarList.Length);

        // Build the local tree.
        var root = new Timestamp(digest);
        var appendedTs = new Timestamp(noncedMsg);
        root.Ops[nonceAppend] = appendedTs;
        var commitTs = new Timestamp(commitment);
        appendedTs.Ops[commitSha256] = commitTs;

        foreach ((_, Timestamp response) in responses)
        {
            commitTs.Merge(response);
        }

        return new DetachedTimestampFile(fileHashOp, root);
    }

    /// <summary>
    /// Batch-stamp many files in a single calendar round-trip.
    /// </summary>
    /// <remarks>
    /// Each input file is hashed (SHA-256), nonced with its own fresh
    /// privacy nonce, then folded into a merkle tree whose root is submitted
    /// to calendars. The returned DTFs each verify independently against the
    /// same calendar-supplied attestation tree.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="filePaths"/> is empty, or <paramref name="calendars"/> is empty.</exception>
    /// <exception cref="AggregateException">Fewer than <paramref name="quorum"/> calendars accepted the stamp.</exception>
    public async Task<IReadOnlyList<DetachedTimestampFile>> StampManyAsync(
        IReadOnlyList<string> filePaths,
        IEnumerable<CalendarClient> calendars,
        int quorum = DefaultCalendars.DefaultStampQuorum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(calendars);
        if (filePaths.Count == 0)
        {
            throw new ArgumentException("At least one file path is required.", nameof(filePaths));
        }

        CalendarClient[] calendarList = [.. calendars];
        if (calendarList.Length == 0)
        {
            throw new ArgumentException("At least one calendar is required to stamp.", nameof(calendars));
        }

        if (quorum < 1 || quorum > calendarList.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quorum),
                quorum,
                $"Quorum must be between 1 and {calendarList.Length}.");
        }

        // Per-file: digest -> nonce -> commitment. Track each step's
        // Timestamp so we can later splice the merkle-aggregation path onto
        // the commitment node.
        var fileHashOps = new OpSha256[filePaths.Count];
        var rootTimestamps = new Timestamp[filePaths.Count];
        var commitTips = new Timestamp[filePaths.Count];
        var commitments = new byte[filePaths.Count][];

        for (int i = 0; i < filePaths.Count; i++)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePaths[i]);

            var fileHashOp = new OpSha256();
            byte[] digest = fileHashOp.HashFile(filePaths[i]);

            byte[] nonce = _nonceProvider();
            if (nonce.Length != NonceLength)
            {
                throw new InvalidOperationException(
                    $"Nonce provider returned {nonce.Length} bytes; expected {NonceLength}.");
            }

            var nonceAppend = new OpAppend(nonce);
            byte[] noncedMsg = nonceAppend.Call(digest);
            var commitSha256 = new OpSha256();
            byte[] commitment = commitSha256.Call(noncedMsg);

            var rootTs = new Timestamp(digest);
            var noncedTs = new Timestamp(noncedMsg);
            rootTs.Ops[nonceAppend] = noncedTs;
            var commitTs = new Timestamp(commitment);
            noncedTs.Ops[commitSha256] = commitTs;

            fileHashOps[i] = fileHashOp;
            rootTimestamps[i] = rootTs;
            commitTips[i] = commitTs;
            commitments[i] = commitment;
        }

        // Single-file fast path: no merkle aggregation needed.
        if (filePaths.Count == 1)
        {
            byte[] rootCommitment = commitments[0];
            await SubmitToCalendarsAndMergeAsync(
                rootCommitment, commitTips[0], calendarList, quorum, cancellationToken)
                .ConfigureAwait(false);
            return [new DetachedTimestampFile(fileHashOps[0], rootTimestamps[0])];
        }

        // Build a merkle tree over the per-file commitments. The aggregator
        // returns a SHARED RootTimestamp reachable from every leaf's path.
        MerkleAggregationResult merkle = MerkleAggregator.Aggregate(commitments);

        // Submit the merkle root to calendars and merge their responses onto
        // the SHARED RootTimestamp BEFORE we splice into per-file trees.
        // The subsequent Merge calls then copy the attestation along into
        // every file's tree.
        await SubmitToCalendarsAndMergeAsync(
            merkle.RootDigest, merkle.RootTimestamp, calendarList, quorum, cancellationToken)
            .ConfigureAwait(false);

        // Splice the merkle-leaf trees onto each commit tip. Now that the
        // calendar attestation lives on the shared root, each merge copies
        // it down into the per-file tree.
        for (int i = 0; i < filePaths.Count; i++)
        {
            commitTips[i].Merge(merkle.LeafTimestamps[i]);
        }

        var results = new DetachedTimestampFile[filePaths.Count];
        for (int i = 0; i < filePaths.Count; i++)
        {
            results[i] = new DetachedTimestampFile(fileHashOps[i], rootTimestamps[i]);
        }
        return results;
    }

    private async Task SubmitToCalendarsAndMergeAsync(
        byte[] commitment,
        Timestamp mergeTarget,
        CalendarClient[] calendarList,
        int quorum,
        CancellationToken cancellationToken)
    {
        Task<(CalendarClient Calendar, Timestamp? Response, Exception? Error)>[] tasks =
            calendarList.Select(async cal =>
            {
                try
                {
                    Timestamp response = await cal
                        .SubmitDigestAsync(commitment, cancellationToken)
                        .ConfigureAwait(false);
                    return (Calendar: cal, Response: (Timestamp?)response, Error: (Exception?)null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (Calendar: cal, Response: (Timestamp?)null, Error: (Exception?)ex);
                }
            }).ToArray();

        var responses = new List<Timestamp>();
        var errors = new List<Exception>();
        foreach (Task<(CalendarClient Calendar, Timestamp? Response, Exception? Error)> t in tasks)
        {
            var outcome = await t.ConfigureAwait(false);
            if (outcome.Response is not null)
            {
                responses.Add(outcome.Response);
            }
            else if (outcome.Error is not null)
            {
                errors.Add(outcome.Error);
            }
        }

        if (responses.Count < quorum)
        {
            _logger.LogError(
                "Batch stamp quorum not met: {Accepted}/{Total} accepted, quorum {Quorum}; {Errors} errors",
                responses.Count, calendarList.Length, quorum, errors.Count);
            throw new AggregateException(
                $"Only {responses.Count} of {calendarList.Length} calendars accepted the batch stamp; " +
                $"quorum was {quorum}.",
                errors);
        }

        foreach (Timestamp r in responses)
        {
            mergeTarget.Merge(r);
        }

        _logger.LogDebug(
            "Batch stamp accepted by {Accepted}/{Total} calendars",
            responses.Count, calendarList.Length);
    }

    private static byte[] GenerateSecureNonce() => RandomNumberGenerator.GetBytes(NonceLength);
}
