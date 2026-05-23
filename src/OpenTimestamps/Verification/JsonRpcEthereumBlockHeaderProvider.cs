using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenTimestamps.Verification;

/// <summary>
/// Ethereum block-header provider backed by an Ethereum JSON-RPC endpoint
/// (e.g. <c>https://cloudflare-eth.com</c> or a self-hosted geth/erigon).
/// </summary>
/// <remarks>
/// <para>
/// Calls <c>eth_getBlockByNumber</c> and returns the block's
/// <c>transactionsRoot</c> as the <see cref="BlockHeader.MerkleRoot"/>.
/// This is the field the OTS Ethereum attestation commits to per the Python
/// reference's <c>EthereumBlockHeaderAttestation</c>.
/// </para>
/// <para>
/// <strong>Trust category: <see cref="TrustCategory.Explorer"/></strong>, even
/// when pointed at a self-hosted node. Reason: post-Merge Ethereum, the OTS
/// commitment is to a header field whose authenticity is no longer
/// cryptographically PoW-secured at the block level. The verification is
/// advisory; treat results as informational rather than evidentiary. See
/// <c>docs/verification-model.md</c> for the longer discussion.
/// </para>
/// </remarks>
public sealed class JsonRpcEthereumBlockHeaderProvider : EthereumBlockHeaderProvider
{
    private readonly HttpClient _http;
    private readonly Uri _rpcEndpoint;
    private readonly string? _authHeader;
    private readonly ILogger _logger;

    public JsonRpcEthereumBlockHeaderProvider(
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
            throw new ArgumentException("Ethereum RPC endpoint must be absolute.", nameof(rpcEndpoint));
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

    public override TrustCategory TrustCategory => TrustCategory.Explorer;

    public override string Name => _rpcEndpoint.Host;

    public override async Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("eth_getBlockByNumber height={Height} via {Endpoint}", height, _rpcEndpoint);

        // Ethereum RPC expects "0x"-prefixed lowercase hex for the block number.
        string heightHex = "0x" + height.ToString("x", CultureInfo.InvariantCulture);
        string body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = "ots",
            method = "eth_getBlockByNumber",
            @params = new object[] { heightHex, false },
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
        response.EnsureSuccessStatusCode();

        await using Stream s = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument doc = await JsonDocument
            .ParseAsync(s, default, cancellationToken)
            .ConfigureAwait(false);

        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("error", out JsonElement err) && err.ValueKind != JsonValueKind.Null)
        {
            string message = err.TryGetProperty("message", out JsonElement msgEl)
                ? msgEl.GetString() ?? err.GetRawText()
                : err.GetRawText();
            throw new InvalidOperationException(
                $"Ethereum RPC eth_getBlockByNumber returned error: {message}");
        }

        if (!root.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Ethereum RPC returned no block for height {height} (perhaps not yet mined?).");
        }

        if (!result.TryGetProperty("transactionsRoot", out JsonElement txRootEl)
            || txRootEl.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Ethereum block JSON missing 'transactionsRoot' string field.");
        }

        if (!result.TryGetProperty("timestamp", out JsonElement tsEl)
            || tsEl.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Ethereum block JSON missing 'timestamp' string field.");
        }

        string txRootHex = TrimHex(txRootEl.GetString()!);
        if (txRootHex.Length != 64)
        {
            throw new InvalidOperationException(
                $"Ethereum transactionsRoot has unexpected length: '{txRootHex}'.");
        }

        // Ethereum natively reports header fields in big-endian (display order)
        // — no reversal needed; OTS commits to the same byte order the RPC
        // returns. (Contrast Bitcoin's merkleroot which is display-reversed.)
        byte[] txRoot = Convert.FromHexString(txRootHex);

        string tsHex = TrimHex(tsEl.GetString()!);
        long unix = long.Parse(tsHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var time = DateTimeOffset.FromUnixTimeSeconds(unix);

        return new BlockHeader(height, txRoot, time).Validate();
    }

    private static string TrimHex(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
}
