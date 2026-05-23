namespace OpenTimestamps;

/// <summary>
/// Raised when <see cref="Timestamp.Merge(Timestamp)"/> is invoked with a
/// timestamp whose <see cref="Timestamp.Msg"/> differs from the receiver's.
/// Two timestamps can only be merged if they are proofs for the same
/// commitment.
/// </summary>
public sealed class TimestampMergeException : InvalidOperationException
{
    public TimestampMergeException(string message) : base(message)
    {
    }

    public TimestampMergeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
