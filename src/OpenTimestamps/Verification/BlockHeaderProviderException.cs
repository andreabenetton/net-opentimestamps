namespace OpenTimestamps.Verification;

/// <summary>
/// Thrown when a <see cref="BlockHeaderProvider"/> cannot return a usable
/// block header. Covers both transport-level failures (HTTP non-2xx, response
/// body exceeding the per-provider size cap, malformed JSON) and protocol-level
/// failures (RPC error message, missing or malformed header field).
/// </summary>
/// <remarks>
/// Mirrors the role of <c>CalendarException</c> for calendar interactions:
/// callers catching this one type can disambiguate "the provider is
/// unavailable / lying" from a generic <c>HttpRequestException</c> bubbling up
/// from the BCL HTTP stack.
/// </remarks>
public sealed class BlockHeaderProviderException : Exception
{
    public BlockHeaderProviderException(string message)
        : base(message)
    {
    }

    public BlockHeaderProviderException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public BlockHeaderProviderException(string message, int httpStatus, Exception? innerException)
        : base(message, innerException)
    {
        HttpStatus = httpStatus;
    }

    /// <summary>
    /// HTTP status code, when this exception came from a non-2xx response.
    /// <c>null</c> when the failure was at a different layer (size cap,
    /// malformed JSON, RPC error, missing field).
    /// </summary>
    public int? HttpStatus { get; }
}
