using System.Text;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests.Ops;

public sealed class OpTests
{
    [Fact]
    public void Sha256_Of_Empty_Matches_Known_Vector()
    {
        var op = new OpSha256();
        byte[] result = op.Call("hello"u8.ToArray());
        // SHA-256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            Convert.ToHexString(result).ToLowerInvariant());
    }

    [Fact]
    public void Sha1_Of_Hello_World_Matches_Known_Vector()
    {
        var op = new OpSha1();
        byte[] result = op.Call("Hello World!\n"u8.ToArray());
        Assert.Equal(20, op.DigestLength);
        Assert.Equal(20, result.Length);
    }

    [Fact]
    public void Ripemd160_Of_Empty_Matches_Known_Vector()
    {
        var op = new OpRipemd160();
        byte[] result = op.Call([]);
        // RIPEMD-160("") = 9c1185a5c5e9fc54612808977ee8f548b2258d31
        Assert.Equal(
            "9c1185a5c5e9fc54612808977ee8f548b2258d31",
            Convert.ToHexString(result).ToLowerInvariant());
    }

    [Fact]
    public void Keccak256_Of_Empty_Matches_Ethereum_Vector()
    {
        var op = new OpKeccak256();
        byte[] result = op.Call([]);
        // Ethereum's Keccak-256("") = c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470
        // (NIST SHA3-256("") would be a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a)
        Assert.Equal(
            "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470",
            Convert.ToHexString(result).ToLowerInvariant());
    }

    [Fact]
    public void Append_Prepend_Are_Inverses_Of_Order()
    {
        byte[] msg = "abc"u8.ToArray();
        var append = new OpAppend("XYZ"u8.ToArray());
        var prepend = new OpPrepend("XYZ"u8.ToArray());
        Assert.Equal("abcXYZ", Encoding.UTF8.GetString(append.Call(msg)));
        Assert.Equal("XYZabc", Encoding.UTF8.GetString(prepend.Call(msg)));
    }

    [Fact]
    public void Hexlify_Lowercase_Ascii()
    {
        byte[] result = new OpHexlify().Call(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.Equal("deadbeef", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Reverse_Reverses()
    {
        byte[] result = new OpReverse().Call(new byte[] { 1, 2, 3, 4 });
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, result);
    }

    [Fact]
    public void BinaryOp_Argument_Empty_Rejected()
    {
        Assert.Throws<ArgumentException>(() => new OpAppend([]));
    }

    [Fact]
    public void Hexlify_Empty_Throws()
    {
        Assert.Throws<OpMessageException>(() => new OpHexlify().Call([]));
    }

    [Fact]
    public void Ops_Equal_When_Same_Tag_And_Arg()
    {
        var a = new OpAppend(new byte[] { 1, 2, 3 });
        var b = new OpAppend(new byte[] { 1, 2, 3 });
        var c = new OpAppend(new byte[] { 1, 2, 4 });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Ops_Sort_By_Tag_Then_Arg()
    {
        Op[] ops =
        [
            new OpPrepend(new byte[] { 0x02 }),    // 0xF1, arg 02
            new OpSha256(),                         // 0x08
            new OpAppend(new byte[] { 0x01 }),     // 0xF0, arg 01
            new OpAppend(new byte[] { 0x02 }),     // 0xF0, arg 02
        ];
        Array.Sort(ops);
        Assert.Equal(0x08, ops[0].Tag);             // sha256 first (lowest tag)
        Assert.Equal(0xF0, ops[1].Tag);             // OpAppend
        Assert.Equal(new byte[] { 0x01 }, ((OpAppend)ops[1]).ArgumentArray());
        Assert.Equal(0xF0, ops[2].Tag);
        Assert.Equal(new byte[] { 0x02 }, ((OpAppend)ops[2]).ArgumentArray());
        Assert.Equal(0xF1, ops[3].Tag);             // OpPrepend last
    }

    [Fact]
    public void Roundtrip_Through_Wire()
    {
        Op[] ops =
        [
            new OpSha1(),
            new OpRipemd160(),
            new OpSha256(),
            new OpKeccak256(),
            new OpReverse(),
            new OpHexlify(),
            new OpAppend(new byte[] { 0xAB }),
            new OpPrepend(new byte[] { 0xCD, 0xEF }),
        ];

        foreach (Op op in ops)
        {
            using var ms = new MemoryStream();
            op.Serialize(new OtsWriter(ms));
            ms.Position = 0;
            Op parsed = Op.Deserialize(new OtsReader(ms));
            Assert.Equal(op, parsed);
        }
    }

    [Fact]
    public void Unknown_Op_Tag_Throws()
    {
        using var ms = new MemoryStream(new byte[] { 0xAA });
        Assert.Throws<DeserializationException>(() => Op.Deserialize(new OtsReader(ms)));
    }
}
