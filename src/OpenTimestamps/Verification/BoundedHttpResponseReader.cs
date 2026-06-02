using System.Text;
using System.Text.Json;

namespace OpenTimestamps.Verification;

/// <summary>
/// Helpers that read block-header-provider HTTP responses with a strict byte
/// cap on the body. Mirrors the <c>CalendarClient</c> response-size discipline:
/// no legitimate header / hash response exceeds these caps, and an unbounded
/// read against a hostile (or merely buggy) endpoint is a DoS surface we close
/// at the boundary.
/// </summary>
internal static class BoundedHttpResponseReader
{
    /// <summary>Cap for JSON block-header responses. Real payloads are &lt; 4 KB.</summary>
    internal const int JsonCap = 32 * 1024;

    /// <summary>Cap for plain-text endpoints (e.g. Esplora's block-hash route returns ~64 chars).</summary>
    internal const int TextCap = 256;

    /// <summary>
    /// Throws <see cref="BlockHeaderProviderException"/> if the response is
    /// non-2xx. Use in place of <c>response.EnsureSuccessStatusCode()</c> so
    /// the boundary exception type is uniform across providers.
    /// </summary>
    internal static void EnsureSuccessOrThrow(HttpResponseMessage response, string operation)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int status = (int)response.StatusCode;
        throw new BlockHeaderProviderException(
            $"{operation} failed with HTTP {status} {response.ReasonPhrase}",
            status,
            innerException: null);
    }

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> from the content stream. Throws
    /// <see cref="BlockHeaderProviderException"/> the moment the cap is
    /// exceeded — no full read of an oversize body.
    /// </summary>
    internal static async Task<byte[]> ReadBoundedAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maxBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        await using Stream s = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        // +1 byte so we can detect "would have been one too many" without
        // already having paid the allocation for the oversize body.
        byte[] buffer = new byte[maxBytes + 1];
        int total = 0;
        while (true)
        {
            int n = await s
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            total += n;
            if (total > maxBytes)
            {
                throw new BlockHeaderProviderException(
                    $"Response body exceeded {maxBytes}-byte cap.");
            }
        }

        if (total == buffer.Length - 1)
        {
            // Wholly filled the legitimate portion. Trim to exact length.
            byte[] exact = new byte[total];
            Array.Copy(buffer, exact, total);
            return exact;
        }

        byte[] result = new byte[total];
        Array.Copy(buffer, result, total);
        return result;
    }

    internal static async Task<string> ReadStringBoundedAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBoundedAsync(content, maxBytes, cancellationToken)
            .ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> and parses as JSON. Malformed
    /// JSON surfaces as <see cref="BlockHeaderProviderException"/>, not raw
    /// <c>JsonException</c>.
    /// </summary>
    internal static async Task<JsonDocument> ParseBoundedJsonAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBoundedAsync(content, maxBytes, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new BlockHeaderProviderException(
                "Provider response was not valid JSON.", ex);
        }
    }
}
