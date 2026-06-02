using System.Net;
using System.Text;
using System.Text.Json;
using OpenTimestamps.Verification;
using Xunit;

namespace OpenTimestamps.Tests.Verification;

public sealed class BitcoinCoreRpcProviderTests
{
    private static readonly Uri RpcEndpoint = new("http://localhost:18443/");

    [Fact]
    public async Task Happy_Path_Returns_Merkle_In_Internal_Byte_Order()
    {
        const string blockHash = "00000000000000000007e6b95f9f8a4f6cd1086a31a8a3a5063bb1c8a6f4d9b2";
        // Display order from getblockheader is big-endian; provider reverses it.
        const string merkleBigEndian = "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899";

        var handler = new RpcHandler(method =>
        {
            return method.Method switch
            {
                "getblockhash" => Ok(JsonSerializer.Serialize(new
                {
                    result = blockHash,
                    error = (object?)null,
                })),
                "getblockheader" => Ok(JsonSerializer.Serialize(new
                {
                    result = new
                    {
                        merkleroot = merkleBigEndian,
                        time = 1_700_000_000L,
                    },
                    error = (object?)null,
                })),
                _ => throw new InvalidOperationException($"unexpected method {method.Method}"),
            };
        });

        using var http = new HttpClient(handler);
        var provider = new BitcoinCoreRpcBlockHeaderProvider(http, RpcEndpoint);

        BlockHeader header = await provider.GetHeaderAsync(800000);

        Assert.Equal(800000UL, header.Height);
        Assert.Equal(TrustCategory.LocalNode, provider.TrustCategory);

        byte[] expected = Convert.FromHexString(merkleBigEndian);
        Array.Reverse(expected);
        Assert.Equal(expected, header.MerkleRoot);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), header.Time);
    }

    [Fact]
    public async Task Rpc_Error_Throws()
    {
        var handler = new RpcHandler(method =>
            Ok(JsonSerializer.Serialize(new
            {
                error = new { message = "Block height out of range" },
                result = (string?)null,
            })));

        using var http = new HttpClient(handler);
        var provider = new BitcoinCoreRpcBlockHeaderProvider(http, RpcEndpoint);

        var ex = await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(99_999_999));
        Assert.Contains("out of range", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_Result_Field_Throws()
    {
        var handler = new RpcHandler(method => Ok("""{"error": null}"""));

        using var http = new HttpClient(handler);
        var provider = new BitcoinCoreRpcBlockHeaderProvider(http, RpcEndpoint);

        await Assert.ThrowsAsync<BlockHeaderProviderException>(
            () => provider.GetHeaderAsync(1));
    }

    [Fact]
    public async Task Auth_Header_Included_When_Credentials_Provided()
    {
        string? observedAuth = null;
        var handler = new RpcHandler(req =>
        {
            observedAuth = req.Authorization?.ToString();
            // Return whatever — the test only inspects the request.
            return Ok("""{"result": "00", "error": null}""");
        }, captureRequest: true);

        using var http = new HttpClient(handler);
        var provider = new BitcoinCoreRpcBlockHeaderProvider(http, RpcEndpoint, "alice", "secret");

        // First call: getblockhash — we will fail downstream on getblockheader,
        // but that's fine for this test; we just need one request to fly.
        try
        {
            await provider.GetHeaderAsync(1);
        }
        catch (Exception)
        {
            // expected — fake responses are not a real flow
        }

        Assert.NotNull(observedAuth);
        Assert.StartsWith("Basic ", observedAuth, StringComparison.Ordinal);
        // Basic auth value: base64("alice:secret")
        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret"));
        Assert.EndsWith(expected, observedAuth, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_Auth_Header_When_No_Credentials()
    {
        bool? hadAuth = null;
        var handler = new RpcHandler(req =>
        {
            hadAuth = req.Authorization is not null;
            return Ok("""{"result": "00", "error": null}""");
        }, captureRequest: true);

        using var http = new HttpClient(handler);
        var provider = new BitcoinCoreRpcBlockHeaderProvider(http, RpcEndpoint);

        try
        {
            await provider.GetHeaderAsync(1);
        }
        catch (Exception)
        {
            // ignored
        }

        Assert.False(hadAuth);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class RpcHandler : HttpMessageHandler
    {
        private readonly Func<dynamic, HttpResponseMessage> _withMethod;
        private readonly Func<System.Net.Http.Headers.HttpRequestHeaders, HttpResponseMessage>? _withRequest;

        public RpcHandler(Func<RpcRequest, HttpResponseMessage> withRpcMethod)
        {
            _withMethod = req => withRpcMethod((RpcRequest)req);
        }

        public RpcHandler(
            Func<System.Net.Http.Headers.HttpRequestHeaders, HttpResponseMessage> withRequest,
            bool captureRequest)
        {
            _withMethod = _ => throw new InvalidOperationException("unused");
            _withRequest = withRequest;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_withRequest is not null)
            {
                return _withRequest(request.Headers);
            }

            string body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            string method = doc.RootElement.GetProperty("method").GetString()!;
            return _withMethod(new RpcRequest(method));
        }
    }

    private sealed record RpcRequest(string Method);
}
