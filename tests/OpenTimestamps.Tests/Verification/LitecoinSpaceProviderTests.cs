using System.Net;
using System.Text;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class LitecoinSpaceProviderTests
{
    private static readonly Uri BaseUri = new("https://example-litecoin-explorer.test/");

    [Fact]
    public async Task Happy_Path_Returns_MerkleRoot_Reversed_To_Internal_Order()
    {
        const string blockHash = "f00a000000000000000000000000000000000000000000000000000000000001";
        const string merkleBigEndian = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var handler = new RouterHandler(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/block-height/2500000", StringComparison.Ordinal))
            {
                return TextOk(blockHash);
            }
            if (path.EndsWith("/block/" + blockHash, StringComparison.Ordinal))
            {
                return JsonOk($$"""
                {
                  "id": "{{blockHash}}",
                  "merkle_root": "{{merkleBigEndian}}",
                  "timestamp": 1700000000
                }
                """);
            }
            throw new InvalidOperationException($"unexpected path {path}");
        });

        using var http = new HttpClient(handler);
        var provider = new LitecoinSpaceBlockHeaderProvider(http, BaseUri);

        BlockHeader header = await provider.GetHeaderAsync(2_500_000UL);

        Assert.Equal(2_500_000UL, header.Height);
        Assert.Equal(TrustCategory.Explorer, provider.TrustCategory);
        Assert.Equal("example-litecoin-explorer.test", provider.Name);

        byte[] expected = Convert.FromHexString(merkleBigEndian);
        Array.Reverse(expected);
        Assert.Equal(expected, header.MerkleRoot);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), header.Time);
    }

    [Fact]
    public async Task Non_Hex_Block_Hash_Throws()
    {
        var handler = new RouterHandler(req =>
            TextOk("not-a-hash"));

        using var http = new HttpClient(handler);
        var provider = new LitecoinSpaceBlockHeaderProvider(http, BaseUri);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("non-hex", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Block_Json_Missing_MerkleRoot_Throws()
    {
        var handler = new RouterHandler(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/block-height/", StringComparison.Ordinal))
            {
                return TextOk(new string('a', 64));
            }
            return JsonOk("""{"timestamp": 1}""");
        });

        using var http = new HttpClient(handler);
        var provider = new LitecoinSpaceBlockHeaderProvider(http, BaseUri);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("merkle_root", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Block_Json_Missing_Timestamp_Throws()
    {
        var handler = new RouterHandler(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/block-height/", StringComparison.Ordinal))
            {
                return TextOk(new string('a', 64));
            }
            return JsonOk($$"""{"merkle_root": "{{new string('0', 64)}}"}""");
        });

        using var http = new HttpClient(handler);
        var provider = new LitecoinSpaceBlockHeaderProvider(http, BaseUri);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("timestamp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Block_Json_Malformed_MerkleRoot_Throws()
    {
        var handler = new RouterHandler(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/block-height/", StringComparison.Ordinal))
            {
                return TextOk(new string('a', 64));
            }
            return JsonOk("""{"merkle_root": "abcdef", "timestamp": 1}""");
        });

        using var http = new HttpClient(handler);
        var provider = new LitecoinSpaceBlockHeaderProvider(http, BaseUri);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("malformed merkle_root", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_Absolute_BaseUri_Rejected_At_Construction()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(
            () => new LitecoinSpaceBlockHeaderProvider(http, new Uri("relative", UriKind.Relative)));
    }

    [Fact]
    public async Task Http_Non_2xx_Throws_BlockHeaderProviderException_With_Status()
    {
        var handler = new RouterHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream", Encoding.UTF8, "text/plain"),
        });

        using var http = new HttpClient(handler);
        var provider = new LitecoinSpaceBlockHeaderProvider(http, BaseUri);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Equal(502, ex.HttpStatus);
    }

    [Fact]
    public async Task Oversize_Json_Body_Throws_BlockHeaderProviderException()
    {
        // Force the second hop (block-by-hash JSON) past the 32 KB cap.
        string fakeHash = new('a', 64);
        string oversizeJson = "{\"merkle_root\":\""
            + new string('0', 64)
            + "\",\"timestamp\":1700000000,\"_pad\":\""
            + new string('x', 33 * 1024)
            + "\"}";
        var handler = new RouterHandler(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/block-height/", StringComparison.Ordinal))
            {
                return TextOk(fakeHash);
            }
            return JsonOk(oversizeJson);
        });

        using var http = new HttpClient(handler);
        var provider = new LitecoinSpaceBlockHeaderProvider(http, BaseUri);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("cap", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage TextOk(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };

    private static HttpResponseMessage JsonOk(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class RouterHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;

        public RouterHandler(Func<HttpRequestMessage, HttpResponseMessage> route)
        {
            _route = route;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_route(request));
    }
}
