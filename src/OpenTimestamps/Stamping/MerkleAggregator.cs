using OpenTimestamps.Ops;

namespace OpenTimestamps.Stamping;

/// <summary>
/// Builds a balanced SHA-256 binary merkle tree over N leaf commitments and
/// returns each leaf's <see cref="Timestamp"/> path to the shared root.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="StampService"/>'s batch-stamp flow: each input file
/// (after the per-file privacy-nonce step) becomes one merkle leaf; the
/// merkle root is what we submit to calendars; each file's
/// <c>.ots</c> records its leaf-to-root path so it verifies independently.
/// </para>
/// <para>
/// Pairing convention: leaves at indices <c>(2k, 2k+1)</c> combine into the
/// parent at level <c>k</c>. From the left leaf we walk up with
/// <see cref="OpAppend"/>(right) then <see cref="OpSha256"/>; from the right
/// leaf we walk up with <see cref="OpPrepend"/>(left) then <see cref="OpSha256"/>.
/// For an odd leaf count, the last leaf is paired with itself (Bitcoin's
/// merkle-tree odd-count rule). Mirrors the Python reference's
/// <c>core/calendar.py</c> aggregator.
/// </para>
/// </remarks>
public static class MerkleAggregator
{
    /// <summary>
    /// Aggregate <paramref name="leaves"/> into a single merkle root and produce
    /// one <see cref="Timestamp"/> per leaf walking the path to that root.
    /// </summary>
    /// <param name="leaves">
    /// Per-file commitments (typically SHA-256 outputs after the privacy nonce
    /// step). Must contain at least one entry. Each commitment must be 32 bytes
    /// (SHA-256 output length).
    /// </param>
    /// <returns>
    /// <c>RootDigest</c> = the merkle root (also the commitment submitted to
    /// calendars). <c>LeafTimestamps[i]</c> = a fresh <see cref="Timestamp"/>
    /// rooted at <c>leaves[i]</c> with ops climbing to the merkle root; their
    /// terminal nodes are all the same logical root (modulo identity), so any
    /// attestation merged into one is visible to all after caller-side
    /// re-pointing or re-merge.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="leaves"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="leaves"/> is empty or any leaf is not 32 bytes.
    /// </exception>
    public static MerkleAggregationResult Aggregate(IReadOnlyList<byte[]> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        if (leaves.Count == 0)
        {
            throw new ArgumentException("At least one leaf is required.", nameof(leaves));
        }

        foreach (byte[] leaf in leaves)
        {
            if (leaf is null || leaf.Length != 32)
            {
                throw new ArgumentException(
                    "Every leaf commitment must be 32 bytes (SHA-256 output).",
                    nameof(leaves));
            }
        }

        // The leaf timestamps we hand back. Each starts as a bare node at
        // the leaf commitment; we will hook ops onto each as we walk up.
        var leafTimestamps = new Timestamp[leaves.Count];
        // Track the "current tip" of each leaf's path. As we collapse the
        // tree, this tip advances toward the root.
        var tips = new Timestamp[leaves.Count];
        for (int i = 0; i < leaves.Count; i++)
        {
            var ts = new Timestamp(leaves[i]);
            leafTimestamps[i] = ts;
            tips[i] = ts;
        }

        if (leaves.Count == 1)
        {
            // Single-file batch: the leaf IS the root. No ops to attach.
            return new MerkleAggregationResult(
                rootDigest: (byte[])leaves[0].Clone(),
                leafTimestamps: leafTimestamps,
                rootTimestamp: leafTimestamps[0]);
        }

        // levelLeaves: the commitments at the current level. As we collapse,
        // this halves (rounded up for odd counts).
        var level = new List<byte[]>(leaves.Count);
        foreach (byte[] leaf in leaves)
        {
            level.Add(leaf);
        }

        // levelTips: parallel to level — the Timestamp tip whose ops we hook
        // the next pair operation onto. Multiple input leaves can share the
        // same tip after pairing, so this is a list of references.
        var levelTips = new List<Timestamp>(leaves.Count);
        foreach (Timestamp t in tips)
        {
            levelTips.Add(t);
        }

        while (level.Count > 1)
        {
            var nextLevel = new List<byte[]>((level.Count + 1) / 2);
            var nextTips = new List<Timestamp>((level.Count + 1) / 2);

            for (int i = 0; i < level.Count; i += 2)
            {
                byte[] left = level[i];
                byte[] right = (i + 1 < level.Count) ? level[i + 1] : left;
                Timestamp leftTip = levelTips[i];
                Timestamp rightTip = (i + 1 < level.Count) ? levelTips[i + 1] : leftTip;

                // Walk left tip UP: OpAppend(right) -> OpSha256.
                var leftAppend = new OpAppend(right);
                byte[] leftAppended = leftAppend.Call(left);
                var leftAppendedTs = new Timestamp(leftAppended);
                leftTip.Ops[leftAppend] = leftAppendedTs;
                var leftHash = new OpSha256();
                byte[] parent = leftHash.Call(leftAppended);
                var parentTs = new Timestamp(parent);
                leftAppendedTs.Ops[leftHash] = parentTs;

                // Walk right tip UP: OpPrepend(left) -> OpSha256, converging
                // on the SAME parent Timestamp instance so a single root
                // attestation reaches every leaf.
                if (!ReferenceEquals(rightTip, leftTip))
                {
                    var rightPrepend = new OpPrepend(left);
                    byte[] rightPrepended = rightPrepend.Call(right);
                    var rightPrependedTs = new Timestamp(rightPrepended);
                    rightTip.Ops[rightPrepend] = rightPrependedTs;
                    var rightHash = new OpSha256();
                    // Same hash output as left side; same merge into parentTs.
                    rightPrependedTs.Ops[rightHash] = parentTs;
                }

                nextLevel.Add(parent);
                nextTips.Add(parentTs);
            }

            level = nextLevel;
            levelTips = nextTips;
        }

        return new MerkleAggregationResult(
            rootDigest: level[0],
            leafTimestamps: leafTimestamps,
            rootTimestamp: levelTips[0]);
    }
}

/// <summary>
/// Output of <see cref="MerkleAggregator.Aggregate(IReadOnlyList{byte[]})"/>.
/// </summary>
public sealed class MerkleAggregationResult
{
    internal MerkleAggregationResult(
        byte[] rootDigest,
        IReadOnlyList<Timestamp> leafTimestamps,
        Timestamp rootTimestamp)
    {
        RootDigest = rootDigest;
        LeafTimestamps = leafTimestamps;
        RootTimestamp = rootTimestamp;
    }

    /// <summary>The merkle root; the commitment submitted to calendars.</summary>
    public byte[] RootDigest { get; }

    /// <summary>
    /// One <see cref="Timestamp"/> per input leaf, in input order. Each carries
    /// the ops climbing from its leaf commitment to the shared root.
    /// </summary>
    public IReadOnlyList<Timestamp> LeafTimestamps { get; }

    /// <summary>
    /// The shared root <see cref="Timestamp"/> instance reachable from every
    /// leaf's path. Attach calendar attestations here BEFORE merging each
    /// leaf into a per-file tree, so the attestation propagates to every
    /// file's proof via copy.
    /// </summary>
    public Timestamp RootTimestamp { get; }
}
