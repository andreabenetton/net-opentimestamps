namespace OpenTimestamps.Serialization;

/// <summary>
/// Writes the OpenTimestamps wire format to a <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the serialization helpers in <c>opentimestamps/core/serialize.py</c>.
/// Not thread-safe. The writer does not own the underlying stream.
/// </para>
/// <para>
/// <strong>Advanced.</strong> Most consumers should not need this type directly —
/// <see cref="DetachedTimestampFile.Serialize(Stream)"/> and the
/// <c>SerializeTo*</c> helpers handle the file framing. <see cref="OtsWriter"/>
/// is exposed for callers extending the library with custom attestation
/// payloads or experimenting with non-default framing.
/// </para>
/// </remarks>
public sealed class OtsWriter
{
    private readonly Stream _stream;

    public OtsWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Stream must be writable.", nameof(stream));
        }

        _stream = stream;
    }

    /// <summary>Writes a single byte.</summary>
    public void WriteUInt8(byte value) => _stream.WriteByte(value);

    /// <summary>Writes raw bytes verbatim (no length prefix).</summary>
    public void WriteBytes(ReadOnlySpan<byte> value) => _stream.Write(value);

    /// <summary>Writes raw bytes verbatim (no length prefix).</summary>
    public void WriteBytes(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _stream.Write(value, 0, value.Length);
    }

    /// <summary>
    /// Writes a little-endian base-128 (LEB128) unsigned integer.
    /// </summary>
    public void WriteVarUInt(ulong value)
    {
        if (value == 0)
        {
            _stream.WriteByte(0);
            return;
        }

        while (value != 0)
        {
            byte b = (byte)(value & 0x7Fu);
            if (value > 0x7Fu)
            {
                b |= 0x80;
            }

            _stream.WriteByte(b);
            if (value <= 0x7Fu)
            {
                break;
            }

            value >>= 7;
        }
    }

    /// <summary>Writes a length-prefixed byte string (varuint length, then raw bytes).</summary>
    public void WriteVarBytes(ReadOnlySpan<byte> value)
    {
        WriteVarUInt((ulong)value.Length);
        _stream.Write(value);
    }

    /// <summary>Writes a length-prefixed byte string (varuint length, then raw bytes).</summary>
    public void WriteVarBytes(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteVarBytes(value.AsSpan());
    }

    /// <summary>Writes a boolean (true → 0xff, false → 0x00).</summary>
    public void WriteBool(bool value) => _stream.WriteByte(value ? (byte)0xFF : (byte)0x00);
}
