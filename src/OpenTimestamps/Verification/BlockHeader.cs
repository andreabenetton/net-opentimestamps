namespace OpenTimestamps.Verification;

/// <summary>
/// The fields of a Bitcoin block header relevant to OpenTimestamps verification.
/// </summary>
/// <param name="Height">Block height.</param>
/// <param name="MerkleRoot">
/// The block's <c>hashMerkleRoot</c> in <em>internal</em> byte order
/// (little-endian). Block explorers usually display this big-endian; callers
/// constructing this record from explorer JSON must reverse the bytes.
/// </param>
/// <param name="Time">The block's <c>nTime</c> as a UTC instant.</param>
public sealed record BlockHeader(ulong Height, byte[] MerkleRoot, DateTimeOffset Time)
{
    /// <summary>Validates basic shape invariants and returns the record.</summary>
    public BlockHeader Validate()
    {
        if (MerkleRoot is null || MerkleRoot.Length != 32)
        {
            throw new ArgumentException(
                $"MerkleRoot must be 32 bytes; got {MerkleRoot?.Length ?? -1}.");
        }

        return this;
    }
}
