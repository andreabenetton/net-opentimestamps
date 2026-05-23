namespace OpenTimestamps.Calendars;

/// <summary>
/// Raised when interaction with an OpenTimestamps calendar server fails for a
/// reason specific to the calendar protocol (rejected digest, over-size
/// response, malformed body, etc.).
/// </summary>
public sealed class CalendarException : Exception
{
    public CalendarException(string message) : base(message)
    {
    }

    public CalendarException(string message, int httpStatus) : base(message)
    {
        HttpStatus = httpStatus;
    }

    public CalendarException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public CalendarException(string message, int httpStatus, Exception? innerException)
        : base(message, innerException)
    {
        HttpStatus = httpStatus;
    }

    /// <summary>HTTP status code from the calendar response, if applicable.</summary>
    public int? HttpStatus { get; }
}
