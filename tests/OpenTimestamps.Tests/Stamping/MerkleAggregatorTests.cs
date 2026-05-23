using System.Security.Cryptography;
using OpenTimestamps;
using OpenTimestamps.Ops;
using OpenTimestamps.Stamping;
using Xunit;

namespace OpenTimestamps.Tests.Stamping;

public sealed class MerkleAggregatorTests
{
    [Fact]
    public void Single_Leaf_Returns_Leaf_As_Root_No_Ops()
    {
        byte[] leaf = SHA256.HashData("alpha"u8.ToArray());
        MerkleAggregationResult result = MerkleAggregator.Aggregate([leaf]);

        Assert.Equal(leaf, result.RootDigest);
        Assert.Single(result.LeafTimestamps);
        Assert.Empty(result.LeafTimestamps[0].Ops);
        Assert.Equal(leaf, result.LeafTimestamps[0].MsgArray());
    }

    [Fact]
    public void Two_Leaves_Produce_Sha256_Of_Concatenated_Pair_As_Root()
    {
        byte[] a = SHA256.HashData("a"u8.ToArray());
        byte[] b = SHA256.HashData("b"u8.ToArray());

        MerkleAggregationResult result = MerkleAggregator.Aggregate([a, b]);

        byte[] expected = SHA256.HashData([.. a, .. b]);
        Assert.Equal(expected, result.RootDigest);
        Assert.Equal(2, result.LeafTimestamps.Count);

        // Each leaf's path should walk up to a single common parent
        // (msg = root). Verify both paths converge.
        byte[] leftRoot = WalkToFirstAttestedOrLeafMsg(result.LeafTimestamps[0]);
        byte[] rightRoot = WalkToFirstAttestedOrLeafMsg(result.LeafTimestamps[1]);
        Assert.Equal(expected, leftRoot);
        Assert.Equal(expected, rightRoot);
    }

    [Fact]
    public void Three_Leaves_Bitcoin_Style_Odd_Duplication()
    {
        // Three leaves; the third gets paired with itself at level 0.
        // Level 1: H(L0 || L1), H(L2 || L2)
        // Level 2 (root): H(L1pair || L2pair)
        byte[] l0 = SHA256.HashData("l0"u8.ToArray());
        byte[] l1 = SHA256.HashData("l1"u8.ToArray());
        byte[] l2 = SHA256.HashData("l2"u8.ToArray());

        MerkleAggregationResult result = MerkleAggregator.Aggregate([l0, l1, l2]);

        byte[] left = SHA256.HashData([.. l0, .. l1]);
        byte[] right = SHA256.HashData([.. l2, .. l2]);
        byte[] expectedRoot = SHA256.HashData([.. left, .. right]);

        Assert.Equal(expectedRoot, result.RootDigest);
        Assert.Equal(3, result.LeafTimestamps.Count);
    }

    [Fact]
    public void Four_Leaves_Build_Balanced_Tree()
    {
        byte[] l0 = SHA256.HashData("l0"u8.ToArray());
        byte[] l1 = SHA256.HashData("l1"u8.ToArray());
        byte[] l2 = SHA256.HashData("l2"u8.ToArray());
        byte[] l3 = SHA256.HashData("l3"u8.ToArray());

        MerkleAggregationResult result = MerkleAggregator.Aggregate([l0, l1, l2, l3]);

        byte[] left = SHA256.HashData([.. l0, .. l1]);
        byte[] right = SHA256.HashData([.. l2, .. l3]);
        byte[] expectedRoot = SHA256.HashData([.. left, .. right]);

        Assert.Equal(expectedRoot, result.RootDigest);
    }

    [Fact]
    public void Each_Leaf_Path_Replays_To_Same_Root()
    {
        // Strong property: walk each leaf's op chain from the bottom; the
        // final msg must equal the announced root.
        byte[] a = SHA256.HashData("a"u8.ToArray());
        byte[] b = SHA256.HashData("b"u8.ToArray());
        byte[] c = SHA256.HashData("c"u8.ToArray());
        byte[] d = SHA256.HashData("d"u8.ToArray());
        byte[] e = SHA256.HashData("e"u8.ToArray());

        MerkleAggregationResult result = MerkleAggregator.Aggregate([a, b, c, d, e]);
        foreach (Timestamp leaf in result.LeafTimestamps)
        {
            byte[] terminal = ReplayToTerminal(leaf);
            Assert.Equal(result.RootDigest, terminal);
        }
    }

    [Fact]
    public void Rejects_Empty_Leaf_List()
    {
        Assert.Throws<ArgumentException>(() => MerkleAggregator.Aggregate(Array.Empty<byte[]>()));
    }

    [Fact]
    public void Rejects_Wrong_Length_Leaf()
    {
        byte[] tooShort = new byte[16];
        Assert.Throws<ArgumentException>(
            () => MerkleAggregator.Aggregate([tooShort]));
    }

    /// <summary>
    /// Walk down a leaf's path repeatedly applying ops to its msg, ending
    /// at the terminal node's msg (which should be the merkle root).
    /// </summary>
    private static byte[] ReplayToTerminal(Timestamp leaf)
    {
        Timestamp cur = leaf;
        byte[] msg = cur.MsgArray();
        while (cur.Ops.Count > 0)
        {
            (Op op, Timestamp next) = cur.Ops.First();
            msg = op.Call(msg);
            Assert.Equal(next.MsgArray(), msg);
            cur = next;
        }
        return msg;
    }

    private static byte[] WalkToFirstAttestedOrLeafMsg(Timestamp leaf)
    {
        Timestamp cur = leaf;
        while (cur.Ops.Count > 0)
        {
            cur = cur.Ops.Values.First();
        }
        return cur.MsgArray();
    }
}
