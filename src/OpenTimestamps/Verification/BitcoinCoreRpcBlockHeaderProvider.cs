using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenTimestamps.Verification;

/// <summary>
/// Block-header provider backed by a Bitcoin Core JSON-RPC endpoint.
/// </summary>
/// <remarks>
/// <strong>Trust category: <see cref="TrustCategory.LocalNode"/>.</strong>
/// Trustless given that the node fully validates the chain it returns headers
/// for. The caller is responsible for pointing this at a real, honest node
/// they control — the provider does not police that on its own.
/// </remarks>
public sealed class BitcoinCoreRpcBlockHeaderProvider : BlockHeaderProvider
{
    private readonly HttpClient _http;
    private readonly Uri _rpcEndpoint;
    private readonly string? _authHeader;
    private readonly ILogger _logger;

    public BitcoinCoreRpcBlockHeaderProvider(
        HttpClient httpClient,
        Uri rpcEndpoint,
        string? username = null,
        string? password = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(rpcEndpoint);
        if (!rpcEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Bitcoin Core RPC endpoint must be absolute.", nameof(rpcEndpoint));
        }

        _http = httpClient;
        _rpcEndpoint = rpcEndpoint;
        _logger = logger ?? NullLogger.Instance;

        if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password))
        {
            string raw = $"{username ?? string.Empty}:{password ?? string.Empty}";
            _authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }
    }

    public override TrustCategory TrustCategory => TrustCategory.LocalNode;

    public override string Name => _rpcEndpoint.Host;

    public override async Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default)
    {
        string hash = await CallStringAsync("getblockhash", [height], cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument header = await CallJsonAsync("getblockheader", [hash, true], cancellationToken)
            .ConfigureAwait(false);

        JsonElement root = header.RootElement;
        if (!root.TryGetProperty("merkleroot", out JsonElement merkleEl)
            || !root.TryGetProperty("time", out JsonElement timeEl))
        {
            throw new BlockHeaderProviderException(
                "Bitcoin Core RPC getblockheader response missing merkleroot/time fields.");
        }

        string merkleHex = merkleEl.GetString()!;
        byte[] merkle = Convert.FromHexString(merkleHex);
        // Bitcoin Core reports merkleroot in big-endian display order; reverse for internal order.
        Array.Reverse(merkle);

        long unix = timeEl.GetInt64();
        var time = DateTimeOffset.FromUnixTimeSeconds(unix);

        return new BlockHeader(height, merkle, time).Validate();
    }

    private async Task<string> CallStringAsync(
        string method, object[] parameters, CancellationToken cancellationToken)
    {
        using JsonDocument doc = await CallJsonAsync(method, parameters, cancellationToken)
            .ConfigureAwait(false);
        return doc.RootElement.GetString()
            ?? throw new BlockHeaderProviderException(
                $"Bitcoin Core RPC {method} returned a non-string result.");
    }

    private async Task<JsonDocument> CallJsonAsync(
        string method, object[] parameters, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Bitcoin Core RPC {Method} @ {Endpoint}", method, _rpcEndpoint);
        string body = JsonSerializer.Serialize(new
        {
            jsonrpc = "1.0",
            id = method,
            method,
            @params = parameters,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, _rpcEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (_authHeader is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeader);
        }

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        BoundedHttpResponseReader.EnsureSuccessOrThrow(response, $"Bitcoin Core RPC {method}");

        using JsonDocument doc = await BoundedHttpResponseReader
            .ParseBoundedJsonAsync(response.Content, BoundedHttpResponseReader.JsonCap, cancellationToken)
            .ConfigureAwait(false);

        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("error", out JsonElement err) && err.ValueKind != JsonValueKind.Null)
        {
            string message = err.TryGetProperty("message", out JsonElement msgEl)
                ? msgEl.GetString() ?? err.GetRawText()
                : err.GetRawText();
            throw new BlockHeaderProviderException(
                $"Bitcoin Core RPC {method} returned error: {message}");
        }

        if (!root.TryGetProperty("result", out JsonElement result))
        {
            throw new BlockHeaderProviderException(
                $"Bitcoin Core RPC {method} returned no 'result' field.");
        }

        // Re-parse so the returned document is just the result subtree. The
        // outer doc's bounded ParseBoundedJsonAsync already enforces the cap;
        // this re-parse is over an in-memory string strictly smaller than that
        // cap, so no second cap needed.
        return JsonDocument.Parse(result.GetRawText());
    }
}
