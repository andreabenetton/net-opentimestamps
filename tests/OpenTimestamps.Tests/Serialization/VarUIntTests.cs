using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests.Serialization;

public sealed class VarUIntTests
{
    [Theory]
    [InlineData(0UL, new byte[] { 0x00 })]
    [InlineData(1UL, new byte[] { 0x01 })]
    [InlineData(127UL, new byte[] { 0x7F })]
    [InlineData(128UL, new byte[] { 0x80, 0x01 })]
    [InlineData(255UL, new byte[] { 0xFF, 0x01 })]
    [InlineData(256UL, new byte[] { 0x80, 0x02 })]
    [InlineData(16_383UL, new byte[] { 0xFF, 0x7F })]
    [InlineData(16_384UL, new byte[] { 0x80, 0x80, 0x01 })]
    public void Encodes_Known_Vectors(ulong value, byte[] expected)
    {
        using var ms = new MemoryStream();
        new OtsWriter(ms).WriteVarUInt(value);
        Assert.Equal(expected, ms.ToArray());
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(127UL)]
    [InlineData(128UL)]
    [InlineData(16_383UL)]
    [InlineData(16_384UL)]
    [InlineData(1_000_000UL)]
    [InlineData(ulong.MaxValue / 2)]
    [InlineData(ulong.MaxValue - 1)]
    public void RoundTrips(ulong value)
    {
        using var ms = new MemoryStream();
        new OtsWriter(ms).WriteVarUInt(value);
        ms.Position = 0;
        ulong actual = new OtsReader(ms).ReadVarUInt();
        Assert.Equal(value, actual);
    }

    [Fact]
    public void Reading_Truncated_Throws()
    {
        // 0xff (continuation set) with no follow-up byte should throw, not loop forever.
        using var ms = new MemoryStream(new byte[] { 0xFF });
        Assert.Throws<DeserializationException>(() => new OtsReader(ms).ReadVarUInt());
    }

    [Fact]
    public void VarBytes_Enforces_MaxLength()
    {
        // Length prefix says 10 but max=5 → reject.
        var data = new byte[] { 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var ms = new MemoryStream(data);
        Assert.Throws<DeserializationException>(() => new OtsReader(ms).ReadVarBytes(maxLength: 5));
    }

    [Fact]
    public void VarBytes_Enforces_MinLength()
    {
        var data = new byte[] { 0x00 };
        using var ms = new MemoryStream(data);
        Assert.Throws<DeserializationException>(
            () => new OtsReader(ms).ReadVarBytes(maxLength: 10, minLength: 1));
    }
}
