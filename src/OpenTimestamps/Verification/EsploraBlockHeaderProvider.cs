using System.Globalization;
using System.Text.Json;

namespace OpenTimestamps.Verification;

/// <summary>
/// Block-header provider that queries an Esplora-compatible HTTP API
/// (e.g. <c>https://blockstream.info/api</c> or <c>https://mempool.space/api</c>).
/// </summary>
/// <remarks>
/// <strong>Trust category: <see cref="TrustCategory.Explorer"/>.</strong> The
/// caller is relying on a third party to report the correct merkle root.
/// Prefer a local Bitcoin Core node when correctness matters.
/// </remarks>
public sealed class EsploraBlockHeaderProvider : BlockHeaderProvider
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public EsploraBlockHeaderProvider(HttpClient httpClient, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Esplora base URI must be absolute.", nameof(baseUri));
        }

        _http = httpClient;
        _baseUri = NormalizeBase(baseUri);
    }

    public override TrustCategory TrustCategory => TrustCategory.Explorer;

    public override string Name => _baseUri.Host;

    public override async Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default)
    {
        // Esplora API: GET /block-height/{height} → block hash (text)
        //              GET /block/{hash}          → JSON with merkle_root + timestamp
        string hash = await GetBlockHashAtHeightAsync(height, cancellationToken).ConfigureAwait(false);
        return await GetBlockAsync(height, hash, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetBlockHashAtHeightAsync(
        ulong height, CancellationToken cancellationToken)
    {
        var uri = new Uri(_baseUri, $"block-height/{height.ToString(CultureInfo.InvariantCulture)}");
        string body = await _http
            .GetStringAsync(uri, cancellationToken)
            .ConfigureAwait(false);

        string hash = body.Trim();
        if (hash.Length != 64 || !IsHex(hash))
        {
            throw new InvalidOperationException(
                $"Esplora returned non-hex block hash for height {height}: '{hash}'.");
        }

        return hash;
    }

    private async Task<BlockHeader> GetBlockAsync(
        ulong height, string hash, CancellationToken cancellationToken)
    {
        var uri = new Uri(_baseUri, $"block/{hash}");
        using Stream s = await _http
            .GetStreamAsync(uri, cancellationToken)
            .ConfigureAwait(false);

        using JsonDocument doc = await JsonDocument
            .ParseAsync(s, default, cancellationToken)
            .ConfigureAwait(false);

        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("merkle_root", out JsonElement merkleEl)
            || merkleEl.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Esplora block JSON missing 'merkle_root' string field.");
        }

        if (!root.TryGetProperty("timestamp", out JsonElement tsEl)
            || tsEl.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException(
                "Esplora block JSON missing 'timestamp' number field.");
        }

        string merkleHex = merkleEl.GetString()!;
        if (merkleHex.Length != 64 || !IsHex(merkleHex))
        {
            throw new InvalidOperationException(
                $"Esplora returned malformed merkle_root: '{merkleHex}'.");
        }

        // Esplora reports merkle root in big-endian "display" order. The OTS
        // attestation compares against the header's internal-order bytes, so
        // we reverse here.
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
