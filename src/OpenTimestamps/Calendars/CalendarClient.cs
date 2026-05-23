using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;

namespace OpenTimestamps.Calendars;

/// <summary>
/// HTTP client for a single OpenTimestamps calendar server.
/// </summary>
/// <remarks>
/// Construct one instance per calendar URL; share the underlying
/// <see cref="HttpClient"/> across calls. Per the protocol, the calendar
/// response body is capped at <see cref="MaxResponseSize"/> bytes; submissions
/// are capped at <see cref="MaxCommitmentSize"/> bytes.
/// </remarks>
public sealed class CalendarClient
{
    /// <summary>Maximum digest size accepted by the calendar's <c>/digest</c> endpoint.</summary>
    public const int MaxCommitmentSize = 64;

    /// <summary>Maximum response body length, matching the reference client cap.</summary>
    public const int MaxResponseSize = 10_000;

    /// <summary>The Accept header value sent by reference clients.</summary>
    public const string AcceptHeader = "application/vnd.opentimestamps.v1";

    private static readonly string DefaultUserAgent =
        $"net-opentimestamps/{typeof(CalendarClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly string _userAgent;

    public CalendarClient(HttpClient httpClient, Uri baseUri, string? userAgent = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Calendar base URI must be absolute.", nameof(baseUri));
        }

        _httpClient = httpClient;
        _baseUri = NormalizeBase(baseUri);
        _userAgent = userAgent ?? DefaultUserAgent;
    }

    /// <summary>The base URL of this calendar.</summary>
    public Uri BaseUri => _baseUri;

    /// <summary>
    /// Submit a commitment digest to the calendar's <c>POST /digest</c> endpoint
    /// and return the resulting partial timestamp tree.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="digest"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="digest"/> length is 0 or exceeds <see cref="MaxCommitmentSize"/>.</exception>
    /// <exception cref="CalendarException">
    /// The calendar rejected the request, returned a non-success status, exceeded
    /// the <see cref="MaxResponseSize"/> cap, or returned an unparseable body.
    /// </exception>
    /// <exception cref="HttpRequestException">An HTTP transport-level error occurred.</exception>
    public async Task<Timestamp> SubmitDigestAsync(
        byte[] digest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length == 0 || digest.Length > MaxCommitmentSize)
        {
            throw new ArgumentException(
                $"Digest must be 1..{MaxCommitmentSize} bytes; got {digest.Length}.",
                nameof(digest));
        }

        var endpoint = new Uri(_baseUri, "digest");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(AcceptHeader));
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Content = new ByteArrayContent(digest);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

        byte[] body = await ReadBoundedBodyAsync(response, cancellationToken).ConfigureAwait(false);
        return DeserializePartialTimestamp(digest, body);
    }

    /// <summary>
    /// Look up an existing timestamp for <paramref name="commitment"/> at the
    /// calendar's <c>GET /timestamp/{hex}</c> endpoint. Returns <c>null</c> on
    /// 404 (the calendar has the commitment pending but no Bitcoin attestation yet).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="commitment"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="commitment"/> is empty.</exception>
    /// <exception cref="CalendarException">
    /// The calendar returned a non-success non-404 status, exceeded the size cap,
    /// or returned an unparseable body.
    /// </exception>
    /// <exception cref="HttpRequestException">An HTTP transport-level error occurred.</exception>
    public async Task<Timestamp?> GetTimestampAsync(
        byte[] commitment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        if (commitment.Length == 0)
        {
            throw new ArgumentException("Commitment must be non-empty.", nameof(commitment));
        }

        string hex = Convert.ToHexString(commitment).ToLower(CultureInfo.InvariantCulture);
        var endpoint = new Uri(_baseUri, $"timestamp/{hex}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(AcceptHeader));
        request.Headers.UserAgent.ParseAdd(_userAgent);

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

        byte[] body = await ReadBoundedBodyAsync(response, cancellationToken).ConfigureAwait(false);
        return DeserializePartialTimestamp(commitment, body);
    }

    private static Timestamp DeserializePartialTimestamp(byte[] commitment, byte[] body)
    {
        using var ms = new MemoryStream(body, writable: false);
        var reader = new Serialization.OtsReader(ms);
        Timestamp timestamp = Timestamp.Deserialize(reader, commitment);
        reader.AssertEof();
        return timestamp;
    }

    private static Uri NormalizeBase(Uri baseUri)
    {
        string s = baseUri.OriginalString;
        return s.EndsWith('/') ? baseUri : new Uri(s + "/");
    }

    private static async Task EnsureSuccessOrThrowAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        Exception? readError = null;
        try
        {
            body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Body read failed *after* the non-success status was observed.
            // Surface the failure via the inner exception so it isn't lost; the
            // primary CalendarException still carries the HTTP status, which is
            // the most actionable detail for the caller.
            readError = ex;
        }

        string bodyOrReason = body ?? (readError is null
            ? "(no body)"
            : $"(unreadable: {readError.GetType().Name})");

        throw new CalendarException(
            $"Calendar returned {(int)response.StatusCode} {response.ReasonPhrase}: {bodyOrReason}",
            (int)response.StatusCode,
            readError);
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        long? declared = response.Content.Headers.ContentLength;
        if (declared.HasValue && declared.Value > MaxResponseSize)
        {
            throw new CalendarException(
                $"Calendar response exceeds size cap ({declared.Value} > {MaxResponseSize}).");
        }

        using Stream content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        byte[] buffer = new byte[MaxResponseSize + 1];
        int total = 0;
        while (total <= MaxResponseSize)
        {
            int n = await content
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            total += n;
        }

        if (total > MaxResponseSize)
        {
            throw new CalendarException(
                $"Calendar response exceeds size cap ({total} > {MaxResponseSize}).");
        }

        if (total == buffer.Length)
        {
            throw new CalendarException(
                $"Calendar response exceeds size cap ({MaxResponseSize}).");
        }

        byte[] result = new byte[total];
        Array.Copy(buffer, result, total);
        return result;
    }
}
