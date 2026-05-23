namespace OpenTimestamps.Serialization;

/// <summary>
/// Raised when an .ots stream or attestation payload cannot be parsed.
/// </summary>
public class DeserializationException : Exception
{
    public DeserializationException()
    {
    }

    public DeserializationException(string message) : base(message)
    {
    }

    public DeserializationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when the recursion depth limit is hit while parsing a timestamp tree.
/// </summary>
public sealed class RecursionLimitException : DeserializationException
{
    public RecursionLimitException(string message) : base(message)
    {
    }

    public RecursionLimitException() : base("Reached timestamp recursion depth limit while deserializing")
    {
    }
}

/// <summary>
/// Raised when the major version byte in a detached .ots file is not understood.
/// </summary>
public sealed class UnsupportedMajorVersionException : DeserializationException
{
    public byte ObservedVersion { get; }

    public UnsupportedMajorVersionException(byte observedVersion)
        : base($"Version {observedVersion} detached timestamp files are not supported")
    {
        ObservedVersion = observedVersion;
    }
}

/// <summary>
/// Raised when an operation is applied to a message whose length is outside protocol bounds.
/// </summary>
public sealed class OpMessageException : Exception
{
    public OpMessageException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when a LEB128 varuint on the wire exceeds the 64-bit value range.
/// </summary>
/// <remarks>
/// The .ots wire format permits varuints up to 64 bits; a 10th continuation byte
/// would carry data the receiver cannot represent. Treat this as a malformed
/// input rather than an out-of-bounds condition the caller could meaningfully
/// recover from.
/// </remarks>
public sealed class VarUIntOverflowException : DeserializationException
{
    public VarUIntOverflowException()
        : base("varuint overflows 64 bits.")
    {
    }
}
