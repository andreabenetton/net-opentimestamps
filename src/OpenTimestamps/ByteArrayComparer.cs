namespace OpenTimestamps;

/// <summary>
/// Equality and hashing for byte arrays compared content-wise.
/// </summary>
internal sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
{
    public static ByteArrayEqualityComparer Instance { get; } = new();

    public bool Equals(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return x.AsSpan().SequenceEqual(y);
    }

    public int GetHashCode(byte[] obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var hash = new HashCode();
        hash.AddBytes(obj);
        return hash.ToHashCode();
    }
}
