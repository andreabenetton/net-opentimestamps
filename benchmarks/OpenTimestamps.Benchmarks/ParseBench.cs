using BenchmarkDotNet.Attributes;
using OpenTimestamps;

namespace OpenTimestamps.Benchmarks;

[MemoryDiagnoser]
public class ParseBench
{
    private byte[] _helloWorld = null!;
    private byte[] _twoCalendars = null!;
    private byte[] _differentBlockchains = null!;

    [GlobalSetup]
    public void Setup()
    {
        string fixturesDir = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "python-opentimestamps");
        _helloWorld = File.ReadAllBytes(Path.Combine(fixturesDir, "hello-world.txt.ots"));
        _twoCalendars = File.ReadAllBytes(Path.Combine(fixturesDir, "two-calendars.txt.ots"));
        _differentBlockchains = File.ReadAllBytes(Path.Combine(fixturesDir, "different-blockchains.txt.ots"));
    }

    [Benchmark(Baseline = true)]
    public DetachedTimestampFile ParseHelloWorld() =>
        DetachedTimestampFile.DeserializeFromArray(_helloWorld);

    [Benchmark]
    public DetachedTimestampFile ParseTwoCalendars() =>
        DetachedTimestampFile.DeserializeFromArray(_twoCalendars);

    [Benchmark]
    public DetachedTimestampFile ParseDifferentBlockchains() =>
        DetachedTimestampFile.DeserializeFromArray(_differentBlockchains);
}
