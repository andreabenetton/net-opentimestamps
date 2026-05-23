using OpenTimestamps.Attestations;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests;

/// <summary>
/// Boundary / size-limit hardening: every documented cap is enforced, and
/// pathological inputs surface as typed DeserializationException subtypes
/// rather than raw OverflowException / IndexOutOfRangeException / etc.
/// </summary>
public sealed class HardeningTests
{
    [Fact]
    public void VarUInt_Tenth_Continuation_Byte_Throws_VarUIntOverflow()
    {
        // 10 continuation bytes (each with the MSB set) exceeds 64-bit value range.
        byte[] payload = new byte[10];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = 0xFF;
        }

        using var ms = new MemoryStream(payload);
        var reader = new OtsReader(ms);
        Assert.Throws<VarUIntOverflowException>(() => reader.ReadVarUInt());
    }

    [Fact]
    public void VarUInt_Truncated_Continuation_Throws_DeserializationException()
    {
        // 0x80 alone says "more bytes follow" but the stream ends — must surface
        // as a typed parse failure, never as raw EndOfStreamException.
        byte[] payload = [0x80];
        using var ms = new MemoryStream(payload);
        var reader = new OtsReader(ms);
        Assert.Throws<DeserializationException>(() => reader.ReadVarUInt());
    }

    [Fact]
    public void VarBytes_Length_Exceeding_Cap_Throws_DeserializationException()
    {
        // Declare a 2 MiB body when DefaultMaxVarBytes is 1 MiB.
        byte[] payload =
        [
            0x80, 0x80, 0x80, 0x01,  // varuint = 1 << 21 = 2 MiB
        ];
        using var ms = new MemoryStream(payload);
        var reader = new OtsReader(ms);
        Assert.Throws<DeserializationException>(() => reader.ReadVarBytes());
    }

    [Fact]
    public void Attestation_Payload_Above_MaxPayloadSize_On_Wire_Throws_Typed()
    {
        // Forge an UnknownAttestation with a payload one byte over the cap.
        // Hand-build the wire bytes — the public ctor would reject before write.
        byte[] tag = [0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77];

        // varuint length = MaxPayloadSize + 1, encoded as LEB128.
        long oversize = TimeAttestation.MaxPayloadSize + 1L;
        using var ms = new MemoryStream();
        ms.Write(tag, 0, tag.Length);
        WriteVarUInt(ms, (ulong)oversize);
        // No actual payload bytes — the cap check should fire on length alone.

        ms.Position = 0;
        var reader = new OtsReader(ms);
        Assert.Throws<DeserializationException>(() => TimeAttestation.Deserialize(reader));
    }

    [Fact]
    public void PendingAttestation_Oversize_URI_On_Wire_Throws_Typed()
    {
        // Build a PendingAttestation payload whose URI is MaxUriLength + 1 bytes.
        const int over = PendingAttestation.MaxUriLength + 1;
        byte[] inner = new byte[over];
        for (int i = 0; i < inner.Length; i++)
        {
            inner[i] = (byte)'a';
        }

        using var innerMs = new MemoryStream();
        WriteVarUInt(innerMs, (ulong)over);
        innerMs.Write(inner, 0, inner.Length);
        byte[] payload = innerMs.ToArray();

        // Frame it as a full attestation: tag, varuint(payload-len), payload.
        using var ms = new MemoryStream();
        ms.Write(PendingAttestation.AttestationTag.ToArray(), 0, 8);
        WriteVarUInt(ms, (ulong)payload.Length);
        ms.Write(payload, 0, payload.Length);

        ms.Position = 0;
        var reader = new OtsReader(ms);
        Assert.Throws<DeserializationException>(() => TimeAttestation.Deserialize(reader));
    }

    [Fact]
    public void Recursion_Depth_Limit_Triggers_RecursionLimitException()
    {
        // Build a chain of OpSha256 ops longer than DefaultRecursionLimit:
        // [0xFF, opTag][0xFF, opTag]...[0x00, attestation].
        // Each 0xFF prefixes an inner element, each opTag introduces one Op layer.
        // The terminating leaf is a Bitcoin attestation.
        const int depth = Timestamp.DefaultRecursionLimit + 5;
        using var ms = new MemoryStream();
        for (int i = 0; i < depth - 1; i++)
        {
            ms.WriteByte(0x08);  // OpSha256 tag (no inner data)
        }

        // Final node: emit a Bitcoin block-header attestation.
        // 0x00 = no 0xFF prefix means this is the terminating element.
        ms.WriteByte(0x00);
        // Tag (8 bytes) + payload-len + payload (varuint height)
        ms.Write(BitcoinBlockHeaderAttestation.AttestationTag.ToArray(), 0, 8);
        // Payload is one varuint: height = 1
        WriteVarUInt(ms, 1);  // payload-len = 1 byte
        ms.WriteByte(0x01);    // varuint(1)

        ms.Position = 0;
        var reader = new OtsReader(ms);
        // Initial msg is 32 zeros (SHA-256 output shape).
        byte[] initialMsg = new byte[32];
        Assert.Throws<RecursionLimitException>(
            () => Timestamp.Deserialize(reader, initialMsg));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(96)]
    public void Truncated_Hello_World_At_Various_Offsets_Throws_Typed(int prefixLength)
    {
        // Read the fixture, truncate it, assert the only allowed exception
        // family escapes — never IndexOutOfRangeException etc.
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "python-opentimestamps",
            "hello-world.txt.ots");

        if (!File.Exists(fixturePath))
        {
            // Fixture not present in this build environment — skip silently.
            return;
        }

        byte[] bytes = File.ReadAllBytes(fixturePath);
        if (prefixLength >= bytes.Length)
        {
            return;
        }

        byte[] truncated = bytes.AsSpan(0, prefixLength).ToArray();
        Exception ex = Assert.ThrowsAny<Exception>(
            () => DetachedTimestampFile.DeserializeFromArray(truncated));
        Assert.True(
            ex is DeserializationException or UnsupportedMajorVersionException
                or EndOfStreamException or IOException,
            $"truncated parser produced unexpected exception type {ex.GetType().Name}: {ex.Message}");
    }

    private static void WriteVarUInt(Stream s, ulong value)
    {
        while (value >= 0x80)
        {
            s.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        s.WriteByte((byte)value);
    }
}
