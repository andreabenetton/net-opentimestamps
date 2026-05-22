using OpenTimestamps.Attestations;
using OpenTimestamps.Calendars;

namespace OpenTimestamps.Stamping;

/// <summary>
/// Polls calendar servers to upgrade pending attestations into confirmed
/// Bitcoin block-header attestations.
/// </summary>
public sealed class UpgradeService
{
    private readonly CalendarWhitelist _whitelist;
    private readonly Func<Uri, CalendarClient> _clientFactory;

    /// <param name="whitelist">URI whitelist used to decide which calendars to contact for upgrade.</param>
    /// <param name="clientFactory">Factory that builds a <see cref="CalendarClient"/> per URI.</param>
    public UpgradeService(CalendarWhitelist whitelist, Func<Uri, CalendarClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        ArgumentNullException.ThrowIfNull(clientFactory);
        _whitelist = whitelist;
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// Attempt to upgrade every pending attestation in <paramref name="dtf"/> by
    /// polling its calendar. Returns true if at least one pending attestation
    /// was resolved to a Bitcoin attestation.
    /// </summary>
    public async Task<UpgradeResult> UpgradeAsync(
        DetachedTimestampFile dtf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dtf);

        var resolved = new List<string>();
        var skipped = new List<string>();
        var stillPending = new List<string>();
        var errors = new List<string>();

        // Pending attestations are not removed on successful merge (we preserve
        // history), so we track which (commitment, uri) pairs have already been
        // visited this call to avoid retrying them in the inner loop.
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool anyResolved;
        do
        {
            anyResolved = false;
            // Snapshot each pending leaf and its msg, since we may mutate the tree
            // while iterating.
            var todo = new List<(byte[] Msg, PendingAttestation Pending, Timestamp Node)>();
            foreach (Timestamp node in dtf.Timestamp.AllNodes())
            {
                foreach (TimeAttestation att in node.Attestations)
                {
                    if (att is PendingAttestation p)
                    {
                        todo.Add((node.MsgArray(), p, node));
                    }
                }
            }

            foreach ((byte[] msg, PendingAttestation pending, Timestamp node) in todo)
            {
                string key = Convert.ToHexString(msg) + "\0" + pending.Uri;
                if (!visited.Add(key))
                {
                    continue;
                }

                if (!_whitelist.IsAllowed(pending.Uri))
                {
                    skipped.Add($"{pending.Uri} (not on whitelist)");
                    continue;
                }

                Uri baseUri;
                try
                {
                    baseUri = new Uri(pending.Uri, UriKind.Absolute);
                }
                catch (UriFormatException ex)
                {
                    errors.Add($"{pending.Uri}: invalid URI ({ex.Message})");
                    continue;
                }

                CalendarClient client = _clientFactory(baseUri);
                Timestamp? upgrade;
                try
                {
                    upgrade = await client.GetTimestampAsync(msg, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{pending.Uri}: {ex.Message}");
                    continue;
                }

                if (upgrade is null)
                {
                    stillPending.Add(pending.Uri);
                    continue;
                }

                node.Merge(upgrade);
                resolved.Add(pending.Uri);
                anyResolved = true;
            }
        }
        while (anyResolved);

        return new UpgradeResult(resolved, stillPending, skipped, errors);
    }
}

/// <summary>Outcome of an <see cref="UpgradeService.UpgradeAsync"/> call.</summary>
public sealed class UpgradeResult
{
    public UpgradeResult(
        IReadOnlyList<string> resolved,
        IReadOnlyList<string> stillPending,
        IReadOnlyList<string> skipped,
        IReadOnlyList<string> errors)
    {
        Resolved = resolved;
        StillPending = stillPending;
        Skipped = skipped;
        Errors = errors;
    }

    /// <summary>Calendar URIs for which a new (Bitcoin or deeper) timestamp tree was merged in.</summary>
    public IReadOnlyList<string> Resolved { get; }

    /// <summary>Calendar URIs that still have only a pending attestation (404 from calendar).</summary>
    public IReadOnlyList<string> StillPending { get; }

    /// <summary>Calendar URIs that were not contacted because they failed the whitelist check.</summary>
    public IReadOnlyList<string> Skipped { get; }

    /// <summary>Calendar URIs whose request failed.</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool ChangedAnything => Resolved.Count > 0;
}
