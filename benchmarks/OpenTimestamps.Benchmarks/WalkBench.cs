using BenchmarkDotNet.Attributes;
using OpenTimestamps;

namespace OpenTimestamps.Benchmarks;

[MemoryDiagnoser]
public class WalkBench
{
    private DetachedTimestampFile _differentBlockchains = null!;
    private DetachedTimestampFile _twoCalendars = null!;

    [GlobalSetup]
    public void Setup()
    {
        string fixturesDir = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "python-opentimestamps");
        _differentBlockchains = DetachedTimestampFile.DeserializeFromFile(
            Path.Combine(fixturesDir, "different-blockchains.txt.ots"));
        _twoCalendars = DetachedTimestampFile.DeserializeFromFile(
            Path.Combine(fixturesDir, "two-calendars.txt.ots"));
    }

    [Benchmark]
    public int WalkAttestations_DifferentBlockchains()
    {
        int count = 0;
        foreach ((_, _) in _differentBlockchains.Timestamp.AllAttestations())
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int WalkAttestations_TwoCalendars()
    {
        int count = 0;
        foreach ((_, _) in _twoCalendars.Timestamp.AllAttestations())
        {
            count++;
        }
        return count;
    }
}
