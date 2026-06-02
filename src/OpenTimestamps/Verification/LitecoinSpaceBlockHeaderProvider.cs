using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenTimestamps.Verification;

/// <summary>
/// Litecoin block-header provider against an Esplora-compatible Litecoin
/// explorer API (e.g. <c>https://litecoinspace.org/api/</c>).
/// </summary>
/// <remarks>
/// <strong>Trust category: <see cref="TrustCategory.Explorer"/>.</strong> The
/// caller is relying on a third party to report the correct merkle root.
/// Run your own Litecoin Core node and add an RPC provider when correctness
/// matters.
/// </remarks>
public sealed class LitecoinSpaceBlockHeaderProvider : LitecoinBlockHeaderProvider
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly ILogger _logger;

    public LitecoinSpaceBlockHeaderProvider(
        HttpClient httpClient, Uri baseUri, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Litecoin explorer base URI must be absolute.", nameof(baseUri));
        }

        _http = httpClient;
        _baseUri = NormalizeBase(baseUri);
        _logger = logger ?? NullLogger.Instance;
    }

    public override TrustCategory TrustCategory => TrustCategory.Explorer;

    public override string Name => _baseUri.Host;

    public override async Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("Fetching Litecoin block at height {Height} from {Host}", height, _baseUri.Host);
        string hash = await GetBlockHashAtHeightAsync(height, cancellationToken).ConfigureAwait(false);
        return await GetBlockAsync(height, hash, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetBlockHashAtHeightAsync(
        ulong height, CancellationToken cancellationToken)
    {
        var uri = new Uri(_baseUri, $"block-height/{height.ToString(CultureInfo.InvariantCulture)}");
        using HttpResponseMessage response = await _http
            .GetAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        BoundedHttpResponseReader.EnsureSuccessOrThrow(response, "Litecoin explorer block-hash query");

        string body = await BoundedHttpResponseReader
            .ReadStringBoundedAsync(response.Content, BoundedHttpResponseReader.TextCap, cancellationToken)
            .ConfigureAwait(false);

        string hash = body.Trim();
        if (hash.Length != 64 || !IsHex(hash))
        {
            throw new BlockHeaderProviderException(
                $"Litecoin explorer returned non-hex block hash for height {height}: '{hash}'.");
        }

        return hash;
    }

    private async Task<BlockHeader> GetBlockAsync(
        ulong height, string hash, CancellationToken cancellationToken)
    {
        var uri = new Uri(_baseUri, $"block/{hash}");
        using HttpResponseMessage response = await _http
            .GetAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        BoundedHttpResponseReader.EnsureSuccessOrThrow(response, "Litecoin explorer block query");

        using JsonDocument doc = await BoundedHttpResponseReader
            .ParseBoundedJsonAsync(response.Content, BoundedHttpResponseReader.JsonCap, cancellationToken)
            .ConfigureAwait(false);

        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("merkle_root", out JsonElement merkleEl)
            || merkleEl.ValueKind != JsonValueKind.String)
        {
            throw new BlockHeaderProviderException(
                "Litecoin explorer block JSON missing 'merkle_root' string field.");
        }

        if (!root.TryGetProperty("timestamp", out JsonElement tsEl)
            || tsEl.ValueKind != JsonValueKind.Number)
        {
            throw new BlockHeaderProviderException(
                "Litecoin explorer block JSON missing 'timestamp' number field.");
        }

        string merkleHex = merkleEl.GetString()!;
        if (merkleHex.Length != 64 || !IsHex(merkleHex))
        {
            throw new BlockHeaderProviderException(
                $"Litecoin explorer returned malformed merkle_root: '{merkleHex}'.");
        }

        // Explorer reports merkle root in big-endian display order; reverse to
        // match the on-wire internal byte order the attestation commits to.
        byte[] merkleBigEndian = Convert.FromHexString(merkleHex);
        Array.Reverse(merkleBigEndian);

        long unix = tsEl.GetInt64();
        var time = DateTimeOffset.FromUnixTimeSeconds(unix);

        return new BlockHeader(height, merkleBigEndian, time).Validate();
    }

    private static Uri NormalizeBase(Uri baseUri)
    {
        string s = baseUri.OriginalString;
        return s.EndsWith('/') ? baseUri : new Uri(s + "/");
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
