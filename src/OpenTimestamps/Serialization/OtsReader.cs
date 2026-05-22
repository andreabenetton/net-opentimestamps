using System.Buffers;

namespace OpenTimestamps.Serialization;

/// <summary>
/// Reads the OpenTimestamps wire format from a <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// Mirrors the deserialization helpers in <c>opentimestamps/core/serialize.py</c>.
/// Not thread-safe. The reader does not own the underlying stream.
/// </remarks>
public sealed class OtsReader
{
    /// <summary>Default maximum payload length for <see cref="ReadVarBytes"/>.</summary>
    public const int DefaultMaxVarBytes = 1 << 20;

    private readonly Stream _stream;

    public OtsReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        }

        _stream = stream;
    }

    /// <summary>Reads a single byte, throwing on EOF.</summary>
    public byte ReadUInt8()
    {
        int b = _stream.ReadByte();
        if (b < 0)
        {
            throw new DeserializationException("Unexpected end of stream while reading uint8.");
        }

        return (byte)b;
    }

    /// <summary>Reads exactly <paramref name="length"/> bytes, throwing on short read.</summary>
    public byte[] ReadBytes(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0)
        {
            return [];
        }

        byte[] buffer = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = _stream.Read(buffer, read, length - read);
            if (n <= 0)
            {
                throw new DeserializationException(
                    $"Unexpected end of stream: requested {length} bytes, got {read}.");
            }

            read += n;
        }

        return buffer;
    }

    /// <summary>
    /// Reads a little-endian base-128 (LEB128) unsigned integer.
    /// </summary>
    /// <remarks>
    /// The reference implementation has no explicit cap. We cap at 63 shift bits
    /// (i.e. effectively 64-bit values) to avoid pathological inputs.
    /// </remarks>
    public ulong ReadVarUInt()
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            byte b = ReadUInt8();
            value |= (ulong)(b & 0x7Fu) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
            if (shift >= 64)
            {
                throw new DeserializationException("varuint overflows 64 bits.");
            }
        }
    }

    /// <summary>
    /// Reads a length-prefixed byte string.
    /// </summary>
    /// <param name="maxLength">Reject lengths exceeding this many bytes.</param>
    /// <param name="minLength">Reject lengths below this many bytes.</param>
    public byte[] ReadVarBytes(int maxLength = DefaultMaxVarBytes, int minLength = 0)
    {
        ulong rawLength = ReadVarUInt();
        if (rawLength > (ulong)maxLength)
        {
            throw new DeserializationException(
                $"varbytes max length exceeded; {rawLength} > {maxLength}.");
        }

        if (rawLength < (ulong)minLength)
        {
            throw new DeserializationException(
                $"varbytes min length not met; {rawLength} < {minLength}.");
        }

        return ReadBytes((int)rawLength);
    }

    /// <summary>Reads a boolean (0xff = true, 0x00 = false).</summary>
    public bool ReadBool()
    {
        byte b = ReadUInt8();
        return b switch
        {
            0xFF => true,
            0x00 => false,
            _ => throw new DeserializationException(
                $"ReadBool expected 0xff or 0x00; got 0x{b:x2}."),
        };
    }

    /// <summary>Reads the next <c>expected.Length</c> bytes and asserts they equal <paramref name="expected"/>.</summary>
    public void AssertMagic(ReadOnlySpan<byte> expected)
    {
        byte[] actual = ReadBytes(expected.Length);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new DeserializationException(
                $"Magic bytes mismatch. Expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}.");
        }
    }

    /// <summary>Throws if the underlying stream has unread data left.</summary>
    public void AssertEof()
    {
        // Try a non-blocking peek when supported, otherwise probe a byte and complain if it succeeded.
        if (_stream.CanSeek)
        {
            if (_stream.Position != _stream.Length)
            {
                long remaining = _stream.Length - _stream.Position;
                throw new DeserializationException($"Expected end of stream; {remaining} bytes remain.");
            }

            return;
        }

        int b = _stream.ReadByte();
        if (b >= 0)
        {
            throw new DeserializationException("Expected end of stream; extra data present.");
        }
    }
}
