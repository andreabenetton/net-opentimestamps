using System.Security.Cryptography;
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

    /// <param name="nonceProvider">
    /// Source of nonce bytes. Defaults to a cryptographically secure RNG. Tests
    /// may inject a deterministic source.
    /// </param>
    public StampService(Func<byte[]>? nonceProvider = null)
    {
        _nonceProvider = nonceProvider ?? GenerateSecureNonce;
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
            throw new AggregateException(
                $"Only {responses.Count} of {calendarList.Length} calendars accepted the stamp; " +
                $"quorum was {quorum}.",
                errors);
        }

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

    private static byte[] GenerateSecureNonce() => RandomNumberGenerator.GetBytes(NonceLength);
}
