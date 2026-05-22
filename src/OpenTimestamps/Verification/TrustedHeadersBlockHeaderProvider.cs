using System.Text.Json;

namespace OpenTimestamps.Verification;

/// <summary>
/// Block-header provider backed by caller-supplied data the caller has
/// independently validated (e.g. a checkpoint header file, a `headers.dat`
/// dump from a Bitcoin Core node the caller controls, or an SPV chain).
/// </summary>
/// <remarks>
/// <strong>Trust category: <see cref="TrustCategory.TrustedHeaders"/>.</strong>
/// As trustless as the caller's own validation of the headers it loaded.
/// </remarks>
public sealed class TrustedHeadersBlockHeaderProvider : BlockHeaderProvider
{
    private readonly IReadOnlyDictionary<ulong, BlockHeader> _headers;
    private readonly string _name;

    /// <param name="headers">Pre-validated headers keyed by block height.</param>
    /// <param name="name">A human-readable name for the source (used in result reporting).</param>
    public TrustedHeadersBlockHeaderProvider(
        IReadOnlyDictionary<ulong, BlockHeader> headers, string name = "trusted-headers")
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentException.ThrowIfNullOrEmpty(name);

        foreach (var kvp in headers)
        {
            if (kvp.Value.Height != kvp.Key)
            {
                throw new ArgumentException(
                    $"Header for key {kvp.Key} reports height {kvp.Value.Height}; mismatch.",
                    nameof(headers));
            }

            kvp.Value.Validate();
        }

        _headers = headers;
        _name = name;
    }

    public override TrustCategory TrustCategory => TrustCategory.TrustedHeaders;

    public override string Name => _name;

    public override Task<BlockHeader> GetHeaderAsync(
        ulong height, CancellationToken cancellationToken = default)
    {
        if (!_headers.TryGetValue(height, out BlockHeader? header))
        {
            throw new KeyNotFoundException(
                $"No trusted header for block height {height}.");
        }

        return Task.FromResult(header);
    }

    /// <summary>
    /// Load a trusted-headers provider from a JSON file of the form
    /// <c>{ "height": { "merkle_root": "&lt;hex big-endian&gt;", "time": &lt;unix-seconds&gt; } }</c>.
    /// </summary>
    /// <remarks>
    /// The <c>merkle_root</c> field is the display-order (big-endian) hex
    /// reported by Bitcoin Core's <c>getblockheader</c> or by Esplora. This
    /// loader reverses the bytes internally so they match the
    /// <see cref="BlockHeader.MerkleRoot"/> internal-byte-order convention.
    /// </remarks>
    public static TrustedHeadersBlockHeaderProvider FromJsonFile(
        string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string json = File.ReadAllText(path);
        return FromJson(json, name ?? Path.GetFileName(path));
    }

    /// <summary>
    /// Parse the JSON document described in <see cref="FromJsonFile"/> from a string.
    /// </summary>
    public static TrustedHeadersBlockHeaderProvider FromJson(string json, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var headers = new Dictionary<ulong, BlockHeader>();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Trusted-headers JSON must be a top-level object.");
        }

        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (!ulong.TryParse(prop.Name, out ulong height))
            {
                throw new FormatException(
                    $"Trusted-headers JSON key is not a block height: '{prop.Name}'.");
            }

            JsonElement body = prop.Value;
            if (body.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException(
                    $"Trusted-headers JSON value at height {height} is not an object.");
            }

            string merkleHex = body.GetProperty("merkle_root").GetString()
                ?? throw new FormatException(
                    $"Trusted-headers JSON at height {height} missing 'merkle_root'.");
            long unix = body.GetProperty("time").GetInt64();

            if (merkleHex.Length != 64)
            {
                throw new FormatException(
                    $"merkle_root at height {height} must be 64 hex chars; got {merkleHex.Length}.");
            }

            byte[] merkle = Convert.FromHexString(merkleHex);
            Array.Reverse(merkle);  // big-endian → internal byte order

            headers[height] = new BlockHeader(
                height,
                merkle,
                DateTimeOffset.FromUnixTimeSeconds(unix));
        }

        return new TrustedHeadersBlockHeaderProvider(headers, name);
    }
}
