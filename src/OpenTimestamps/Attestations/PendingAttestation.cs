using System.Buffers;
using System.Text;
using OpenTimestamps.Serialization;

namespace OpenTimestamps.Attestations;

/// <summary>
/// The calendar's promise to anchor the commitment in a future Bitcoin block.
/// </summary>
/// <remarks>
/// A pending attestation does NOT prove anchoring on Bitcoin; it only records
/// which calendar URI the caller should poll later via <c>GET /timestamp/{hex(msg)}</c>
/// to obtain a confirmed Bitcoin attestation.
/// </remarks>
public sealed class PendingAttestation : TimeAttestation
{
    /// <summary>The 8-byte type tag <c>83 df e3 0d 2e f9 0c 8e</c>.</summary>
    public static ReadOnlySpan<byte> AttestationTag => [0x83, 0xDF, 0xE3, 0x0D, 0x2E, 0xF9, 0x0C, 0x8E];

    /// <summary>Maximum URI length permitted by the reference.</summary>
    public const int MaxUriLength = 1000;

    private static readonly SearchValues<byte> AllowedUriBytes = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._/:"u8);

    public PendingAttestation(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        byte[] encoded = Encoding.UTF8.GetBytes(uri);
        ValidateUri(encoded);
        Uri = uri;
    }

    private PendingAttestation(byte[] utf8Uri)
    {
        Uri = Encoding.UTF8.GetString(utf8Uri);
    }

    /// <summary>The calendar URI (e.g. <c>https://alice.btc.calendar.opentimestamps.org</c>).</summary>
    public string Uri { get; }

    public override ReadOnlySpan<byte> Tag => AttestationTag;

    protected override void SerializePayload(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarBytes(Encoding.UTF8.GetBytes(Uri));
    }

    internal static PendingAttestation DeserializePayload(OtsReader reader)
    {
        byte[] uri = reader.ReadVarBytes(MaxUriLength);
        try
        {
            ValidateUri(uri);
        }
        catch (ArgumentException ex)
        {
            throw new DeserializationException($"Invalid URI in PendingAttestation: {ex.Message}");
        }

        return new PendingAttestation(uri);
    }

    private static void ValidateUri(byte[] utf8)
    {
        if (utf8.Length > MaxUriLength)
        {
            throw new ArgumentException(
                $"URI exceeds maximum length of {MaxUriLength} bytes.");
        }

        int idx = utf8.AsSpan().IndexOfAnyExcept(AllowedUriBytes);
        if (idx >= 0)
        {
            throw new ArgumentException(
                $"URI contains disallowed byte 0x{utf8[idx]:x2} at offset {idx}.");
        }
    }

    public override string ToString() => $"pending {Uri}";
}
