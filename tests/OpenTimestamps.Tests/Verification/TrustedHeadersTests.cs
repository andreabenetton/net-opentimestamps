using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class TrustedHeadersTests
{
    [Fact]
    public async Task Returns_Header_For_Known_Height()
    {
        byte[] merkle = new byte[32];
        merkle[0] = 0x42;
        var headers = new Dictionary<ulong, BlockHeader>
        {
            [123] = new BlockHeader(123, merkle, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)),
        };

        var provider = new TrustedHeadersBlockHeaderProvider(headers);
        BlockHeader h = await provider.GetHeaderAsync(123);

        Assert.Equal(123UL, h.Height);
        Assert.Equal(0x42, h.MerkleRoot[0]);
        Assert.Equal(TrustCategory.TrustedHeaders, provider.TrustCategory);
    }

    [Fact]
    public async Task Missing_Height_Throws()
    {
        var provider = new TrustedHeadersBlockHeaderProvider(new Dictionary<ulong, BlockHeader>());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.GetHeaderAsync(123));
    }

    [Fact]
    public void Rejects_Mismatched_Height_In_Header()
    {
        byte[] merkle = new byte[32];
        var headers = new Dictionary<ulong, BlockHeader>
        {
            [123] = new BlockHeader(999, merkle, DateTimeOffset.UnixEpoch),
        };

        Assert.Throws<ArgumentException>(() => new TrustedHeadersBlockHeaderProvider(headers));
    }

    [Fact]
    public async Task FromJson_Parses_Big_Endian_Merkle_Root()
    {
        // Display-order (big-endian) hex
        const string bigEndianHex = "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899";
        string json = $$"""
            {
                "100": {
                    "merkle_root": "{{bigEndianHex}}",
                    "time": 1700000000
                }
            }
            """;
        var provider = TrustedHeadersBlockHeaderProvider.FromJson(json, "test");

        BlockHeader h = await provider.GetHeaderAsync(100);
        // FromJson should reverse to internal byte order.
        byte[] expected = Convert.FromHexString(bigEndianHex);
        Array.Reverse(expected);
        Assert.Equal(expected, h.MerkleRoot);
    }

    [Fact]
    public void FromJson_Rejects_Non_Object_Root()
    {
        Assert.Throws<FormatException>(() =>
            TrustedHeadersBlockHeaderProvider.FromJson("[]", "test"));
    }

    [Fact]
    public void FromJson_Rejects_Non_Hex_Merkle_Root()
    {
        const string json = """
            {
                "100": { "merkle_root": "notahexstring", "time": 1700000000 }
            }
            """;
        Assert.Throws<FormatException>(() =>
            TrustedHeadersBlockHeaderProvider.FromJson(json, "test"));
    }
}
