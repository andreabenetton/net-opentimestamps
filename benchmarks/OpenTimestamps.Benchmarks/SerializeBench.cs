using BenchmarkDotNet.Attributes;
using OpenTimestamps;

namespace OpenTimestamps.Benchmarks;

[MemoryDiagnoser]
public class SerializeBench
{
    private DetachedTimestampFile _helloWorld = null!;
    private DetachedTimestampFile _twoCalendars = null!;
    private DetachedTimestampFile _differentBlockchains = null!;

    [GlobalSetup]
    public void Setup()
    {
        string fixturesDir = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "python-opentimestamps");
        _helloWorld = DetachedTimestampFile.DeserializeFromFile(
            Path.Combine(fixturesDir, "hello-world.txt.ots"));
        _twoCalendars = DetachedTimestampFile.DeserializeFromFile(
            Path.Combine(fixturesDir, "two-calendars.txt.ots"));
        _differentBlockchains = DetachedTimestampFile.DeserializeFromFile(
            Path.Combine(fixturesDir, "different-blockchains.txt.ots"));
    }

    [Benchmark(Baseline = true)]
    public byte[] SerializeHelloWorld() => _helloWorld.SerializeToArray();

    [Benchmark]
    public byte[] SerializeTwoCalendars() => _twoCalendars.SerializeToArray();

    [Benchmark]
    public byte[] SerializeDifferentBlockchains() => _differentBlockchains.SerializeToArray();
}
