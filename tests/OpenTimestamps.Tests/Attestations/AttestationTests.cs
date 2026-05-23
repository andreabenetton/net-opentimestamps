using OpenTimestamps.Attestations;
using OpenTimestamps.Serialization;
using Xunit;

namespace OpenTimestamps.Tests.Attestations;

public sealed class AttestationTests
{
    [Fact]
    public void PendingAttestation_RoundTrip_Preserves_Uri()
    {
        var att = new PendingAttestation("https://alice.btc.calendar.opentimestamps.org");
        using var ms = new MemoryStream();
        att.Serialize(new OtsWriter(ms));
        ms.Position = 0;
        TimeAttestation parsed = TimeAttestation.Deserialize(new OtsReader(ms));
        var p = Assert.IsType<PendingAttestation>(parsed);
        Assert.Equal(att.Uri, p.Uri);
    }

    [Fact]
    public void PendingAttestation_Rejects_Invalid_Uri_Bytes()
    {
        Assert.Throws<ArgumentException>(() => new PendingAttestation("not a calendar uri"));
    }

    [Fact]
    public void PendingAttestation_Rejects_Overlong_Uri()
    {
        string s = new('a', 1001);
        Assert.Throws<ArgumentException>(() => new PendingAttestation(s));
    }

    [Fact]
    public void BitcoinBlockHeaderAttestation_RoundTrip_Preserves_Height()
    {
        var att = new BitcoinBlockHeaderAttestation(800000);
        using var ms = new MemoryStream();
        att.Serialize(new OtsWriter(ms));
        ms.Position = 0;
        TimeAttestation parsed = TimeAttestation.Deserialize(new OtsReader(ms));
        var b = Assert.IsType<BitcoinBlockHeaderAttestation>(parsed);
        Assert.Equal(800000UL, b.Height);
    }

    [Fact]
    public void UnknownAttestation_Round_Trips_Verbatim()
    {
        byte[] tag = new byte[] { 0x99, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22 };
        byte[] payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var unknown = new UnknownAttestation(tag, payload);

        using var ms = new MemoryStream();
        unknown.Serialize(new OtsWriter(ms));
        ms.Position = 0;

        TimeAttestation parsed = TimeAttestation.Deserialize(new OtsReader(ms));
        var u = Assert.IsType<UnknownAttestation>(parsed);
        Assert.Equal(tag, u.Tag.ToArray());
        Assert.Equal(payload, u.PayloadArray());
    }

    [Fact]
    public void Attestations_Sort_By_Tag_Then_Payload()
    {
        TimeAttestation[] atts =
        [
            new BitcoinBlockHeaderAttestation(800001),
            new PendingAttestation("https://b.calendar.opentimestamps.org"),
            new BitcoinBlockHeaderAttestation(800000),
            new PendingAttestation("https://a.calendar.opentimestamps.org"),
        ];
        Array.Sort(atts);
        // Bitcoin tag (0x05...) < Pending tag (0x83...)
        Assert.Equal(800000UL, ((BitcoinBlockHeaderAttestation)atts[0]).Height);
        Assert.Equal(800001UL, ((BitcoinBlockHeaderAttestation)atts[1]).Height);
        Assert.StartsWith("https://a", ((PendingAttestation)atts[2]).Uri);
        Assert.StartsWith("https://b", ((PendingAttestation)atts[3]).Uri);
    }

    [Fact]
    public void LitecoinBlockHeaderAttestation_RoundTrip_Preserves_Height()
    {
        var att = new LitecoinBlockHeaderAttestation(2_500_000);
        using var ms = new MemoryStream();
        att.Serialize(new OtsWriter(ms));
        ms.Position = 0;
        TimeAttestation parsed = TimeAttestation.Deserialize(new OtsReader(ms));
        var l = Assert.IsType<LitecoinBlockHeaderAttestation>(parsed);
        Assert.Equal(2_500_000UL, l.Height);
    }

    [Fact]
    public void EthereumBlockHeaderAttestation_RoundTrip_Preserves_Height()
    {
        var att = new EthereumBlockHeaderAttestation(18_000_000);
        using var ms = new MemoryStream();
        att.Serialize(new OtsWriter(ms));
        ms.Position = 0;
        TimeAttestation parsed = TimeAttestation.Deserialize(new OtsReader(ms));
        var e = Assert.IsType<EthereumBlockHeaderAttestation>(parsed);
        Assert.Equal(18_000_000UL, e.Height);
    }

    [Fact]
    public void Attestation_Hash_Set_Dedupes_Equal_Pending_Atts()
    {
        var set = new HashSet<TimeAttestation>
        {
            new PendingAttestation("https://alice.btc.calendar.opentimestamps.org"),
            new PendingAttestation("https://alice.btc.calendar.opentimestamps.org"),
        };
        Assert.Single(set);
    }
}
