using System.Net;
using System.Text;
using System.Text.Json;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class JsonRpcEthereumProviderTests
{
    private static readonly Uri RpcEndpoint = new("https://example-eth-rpc.test/");

    [Fact]
    public async Task Happy_Path_Returns_TransactionsRoot_As_MerkleRoot_Big_Endian()
    {
        const string txRoot = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        var handler = new RpcHandler(_ => Ok($$"""
        {
          "jsonrpc": "2.0",
          "id": "ots",
          "result": {
            "transactionsRoot": "0x{{txRoot}}",
            "timestamp": "0x65a4b3c0"
          }
        }
        """));

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        BlockHeader header = await provider.GetHeaderAsync(18_000_000UL);

        Assert.Equal(18_000_000UL, header.Height);
        Assert.Equal(TrustCategory.Explorer, provider.TrustCategory);
        Assert.Equal("example-eth-rpc.test", provider.Name);
        // Ethereum reports natively big-endian; no reversal.
        Assert.Equal(Convert.FromHexString(txRoot), header.MerkleRoot);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(0x65a4b3c0),
            header.Time);
    }

    [Fact]
    public async Task Sends_Hex_Encoded_Block_Number()
    {
        string? requestBody = null;
        var handler = new RpcHandler(body =>
        {
            requestBody = body;
            return Ok($$"""
            {
              "result": {
                "transactionsRoot": "0x{{new string('0', 64)}}",
                "timestamp": "0x1"
              }
            }
            """);
        });

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);
        _ = await provider.GetHeaderAsync(0xDEADBEEF);

        Assert.NotNull(requestBody);
        // Block number serialized as 0x-prefixed lowercase hex.
        Assert.Contains("\"0xdeadbeef\"", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"eth_getBlockByNumber\"", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rpc_Error_Throws_With_Message()
    {
        var handler = new RpcHandler(_ => Ok("""
        {
          "jsonrpc": "2.0",
          "error": { "code": -32602, "message": "block not found" },
          "id": "ots"
        }
        """));

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(99_999_999UL));
        Assert.Contains("block not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_Result_Throws_With_Hint()
    {
        var handler = new RpcHandler(_ => Ok("""{"jsonrpc":"2.0","id":"ots"}"""));

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("no block", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_TransactionsRoot_Throws()
    {
        var handler = new RpcHandler(_ => Ok("""
        {"result": {"timestamp": "0x1"}}
        """));

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("transactionsRoot", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_Timestamp_Throws()
    {
        string body = "{\"result\": {\"transactionsRoot\": \"0x" + new string('0', 64) + "\"}}";
        var handler = new RpcHandler(_ => Ok(body));

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("timestamp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_Length_TransactionsRoot_Throws()
    {
        var handler = new RpcHandler(_ => Ok("""
        {"result": {"transactionsRoot": "0xabcdef", "timestamp": "0x1"}}
        """));

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("unexpected length", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Basic_Auth_Header_Set_When_Credentials_Provided()
    {
        string? authHeader = null;
        string body = "{\"result\": {\"transactionsRoot\": \"0x" + new string('0', 64) + "\", \"timestamp\": \"0x1\"}}";
        var handler = new RpcHandler(_ => Ok(body),
            captureAuth: hdr => authHeader = hdr);

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint, "alice", "secret");
        _ = await provider.GetHeaderAsync(1UL);

        Assert.NotNull(authHeader);
        Assert.StartsWith("Basic ", authHeader, StringComparison.Ordinal);
        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret"));
        Assert.EndsWith(expected, authHeader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_Absolute_RpcEndpoint_Rejected_At_Construction()
    {
        using var http = new HttpClient();
        await Task.CompletedTask;
        Assert.Throws<ArgumentException>(
            () => new JsonRpcEthereumBlockHeaderProvider(http, new Uri("relative", UriKind.Relative)));
    }

    [Fact]
    public async Task Http_Non_2xx_Throws_BlockHeaderProviderException_With_Status()
    {
        var handler = new RpcHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("upstream busy", Encoding.UTF8, "text/plain"),
        });

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Equal(503, ex.HttpStatus);
    }

    [Fact]
    public async Task Oversize_Response_Body_Throws_BlockHeaderProviderException()
    {
        // 33 KB padding past the 32 KB JsonCap. Wrapped as JSON to force the
        // provider through ParseBoundedJsonAsync, which enforces the cap.
        string padded = "{\"result\":{\"transactionsRoot\":\"0x"
            + new string('0', 64)
            + "\",\"timestamp\":\"0x1\",\"_pad\":\""
            + new string('a', 33 * 1024)
            + "\"}}";

        var handler = new RpcHandler(_ => Ok(padded));

        using var http = new HttpClient(handler);
        var provider = new JsonRpcEthereumBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1UL));
        Assert.Contains("cap", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class RpcHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _handler;
        private readonly Action<string?>? _captureAuth;

        public RpcHandler(Func<string, HttpResponseMessage> handler, Action<string?>? captureAuth = null)
        {
            _handler = handler;
            _captureAuth = captureAuth;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _captureAuth?.Invoke(request.Headers.Authorization?.ToString());
            string body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return _handler(body);
        }
    }
}
