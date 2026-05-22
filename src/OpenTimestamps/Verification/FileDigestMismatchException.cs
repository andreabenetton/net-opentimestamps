namespace OpenTimestamps.Verification;

/// <summary>
/// Raised when the candidate file's hash does not match the
/// digest committed inside the detached timestamp file.
/// </summary>
public sealed class FileDigestMismatchException : Exception
{
    public FileDigestMismatchException(byte[] expectedDigest, byte[] actualDigest)
        : base(BuildMessage(expectedDigest, actualDigest))
    {
        ExpectedDigest = (byte[])expectedDigest.Clone();
        ActualDigest = (byte[])actualDigest.Clone();
    }

    /// <summary>The digest committed inside the <c>.ots</c> file.</summary>
    public byte[] ExpectedDigest { get; }

    /// <summary>The digest computed from the candidate file.</summary>
    public byte[] ActualDigest { get; }

    private static string BuildMessage(byte[] expected, byte[] actual) =>
        "File hash does not match the digest committed in the timestamp: " +
        $"expected {Convert.ToHexString(expected).ToLowerInvariant()}, " +
        $"got {Convert.ToHexString(actual).ToLowerInvariant()}.";
}
