using System.Globalization;
using System.Text.Json;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Verification;

namespace OpenTimestamps.Cli;

internal static class JsonOutput
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
    };

    public static void WriteInfo(DetachedTimestampFile dtf, Stream output)
    {
        using var writer = new Utf8JsonWriter(output, Options);
        writer.WriteStartObject();
        writer.WriteString("file_hash_op", dtf.FileHashOp.Name);
        writer.WriteString("file_digest", Convert.ToHexString(dtf.FileDigest).ToLowerInvariant());

        writer.WriteStartArray("attestations");
        foreach ((byte[] msg, TimeAttestation att) in dtf.Timestamp.AllAttestations())
        {
            writer.WriteStartObject();
            writer.WriteString("commitment", Convert.ToHexString(msg).ToLowerInvariant());
            WriteAttestation(writer, att);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    public static void WriteVerifyResult(
        VerificationResult result, BlockHeaderProvider? provider, string filePath, Stream output)
    {
        using var writer = new Utf8JsonWriter(output, Options);
        writer.WriteStartObject();
        writer.WriteString("file", filePath);
        writer.WriteString("status", result.Status.ToString());

        if (result.EarliestVerifiedTime is { } earliest)
        {
            writer.WriteString(
                "earliest_verified_time",
                earliest.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }

        writer.WriteStartArray("verified_attestations");
        foreach (VerifiedAttestation v in result.VerifiedAttestations)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "bitcoin");
            writer.WriteNumber("height", v.Height);
            writer.WriteString(
                "block_time",
                v.BlockTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            writer.WriteString("provider", v.ProviderName);
            writer.WriteString("trust_category", v.TrustCategory.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("bitcoin_attestations");
        foreach (var b in result.BitcoinAttestations)
        {
            writer.WriteStartObject();
            writer.WriteNumber("height", b.Height);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("pending_attestations");
        foreach (var p in result.PendingAttestations)
        {
            writer.WriteStartObject();
            writer.WriteString("uri", p.Uri);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("unknown_attestations");
        foreach (var u in result.UnknownAttestations)
        {
            writer.WriteStartObject();
            writer.WriteString("tag", Convert.ToHexString(u.Tag).ToLowerInvariant());
            writer.WriteNumber("payload_length", u.Payload.Length);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("warnings");
        foreach (string w in result.Warnings)
        {
            writer.WriteStringValue(w);
        }

        writer.WriteEndArray();

        if (provider is not null)
        {
            writer.WriteStartObject("provider");
            writer.WriteString("name", provider.Name);
            writer.WriteString("trust_category", provider.TrustCategory.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteAttestation(Utf8JsonWriter writer, TimeAttestation att)
    {
        switch (att)
        {
            case PendingAttestation p:
                writer.WriteString("type", "pending");
                writer.WriteString("uri", p.Uri);
                break;

            case BitcoinBlockHeaderAttestation b:
                writer.WriteString("type", "bitcoin");
                writer.WriteNumber("height", b.Height);
                break;

            case LitecoinBlockHeaderAttestation l:
                writer.WriteString("type", "litecoin");
                writer.WriteNumber("height", l.Height);
                break;

            case EthereumBlockHeaderAttestation e:
                writer.WriteString("type", "ethereum");
                writer.WriteNumber("height", e.Height);
                break;

            case UnknownAttestation u:
                writer.WriteString("type", "unknown");
                writer.WriteString("tag", Convert.ToHexString(u.Tag).ToLowerInvariant());
                writer.WriteNumber("payload_length", u.Payload.Length);
                break;

            default:
                writer.WriteString("type", att.GetType().Name);
                break;
        }
    }
}
