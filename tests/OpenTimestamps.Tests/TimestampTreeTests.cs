using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests;

public sealed class TimestampTreeTests
{
    [Fact]
    public void Empty_Timestamp_Cannot_Be_Serialized()
    {
        var ts = new Timestamp(new byte[32]);
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => ts.Serialize(new OtsWriter(ms)));
    }

    [Fact]
    public void Merge_Requires_Same_Msg()
    {
        var a = new Timestamp(new byte[] { 1, 2, 3 });
        var b = new Timestamp(new byte[] { 4, 5, 6 });
        var ex = Assert.Throws<TimestampMergeException>(() => a.Merge(b));
        // Still catchable as InvalidOperationException for backward-compatibility.
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void Merge_Unions_Attestations()
    {
        var msg = new byte[] { 1, 2, 3 };
        var a = new Timestamp(msg);
        var b = new Timestamp(msg);
        a.Attestations.Add(new PendingAttestation("https://a.calendar.opentimestamps.org"));
        b.Attestations.Add(new PendingAttestation("https://b.calendar.opentimestamps.org"));
        a.Merge(b);
        Assert.Equal(2, a.Attestations.Count);
    }

    [Fact]
    public void Merge_Combines_Subtrees_Under_Same_Op()
    {
        var msg = new byte[] { 1, 2, 3 };
        var a = new Timestamp(msg);
        var b = new Timestamp(msg);

        var op = new OpAppend(new byte[] { 0xAB });
        byte[] child = op.Call(msg);

        var aChild = new Timestamp(child);
        aChild.Attestations.Add(new PendingAttestation("https://a.calendar.opentimestamps.org"));
        a.Ops[op] = aChild;

        var bChild = new Timestamp(child);
        bChild.Attestations.Add(new PendingAttestation("https://b.calendar.opentimestamps.org"));
        b.Ops[new OpAppend(new byte[] { 0xAB })] = bChild;

        a.Merge(b);
        Assert.Single(a.Ops);
        Assert.Equal(2, a.Ops.Values.Single().Attestations.Count);
    }

    [Fact]
    public void Roundtrip_With_Multiple_Attestations_And_Ops()
    {
        var msg = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var root = new Timestamp(msg);
        root.Attestations.Add(new PendingAttestation("https://a.calendar.opentimestamps.org"));
        root.Attestations.Add(new PendingAttestation("https://b.calendar.opentimestamps.org"));

        var appendOp = new OpAppend(new byte[] { 0x01 });
        var prependOp = new OpPrepend(new byte[] { 0x02 });
        byte[] appendedMsg = appendOp.Call(msg);
        byte[] prependedMsg = prependOp.Call(msg);

        var appendedChild = new Timestamp(appendedMsg);
        appendedChild.Attestations.Add(new BitcoinBlockHeaderAttestation(800000));
        root.Ops[appendOp] = appendedChild;

        var prependedChild = new Timestamp(prependedMsg);
        prependedChild.Attestations.Add(new BitcoinBlockHeaderAttestation(800001));
        root.Ops[prependOp] = prependedChild;

        using var ms = new MemoryStream();
        root.Serialize(new OtsWriter(ms));
        ms.Position = 0;

        Timestamp parsed = Timestamp.Deserialize(new OtsReader(ms), msg);
        Assert.Equal(2, parsed.Attestations.Count);
        Assert.Equal(2, parsed.Ops.Count);

        // Order-independent equality of attestations sets
        Assert.True(parsed.Attestations.SetEquals(root.Attestations));
    }
}
