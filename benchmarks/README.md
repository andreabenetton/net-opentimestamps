# Benchmarks

Microbenchmarks for the OpenTimestamps library, built with
[BenchmarkDotNet](https://benchmarkdotnet.org/).

## Running

```
dotnet run -c Release --project benchmarks/OpenTimestamps.Benchmarks
```

BenchmarkDotNet will print a menu of benchmark classes; pass `--filter '*'`
to run all, or `--filter '*ParseBench*'` to run a subset.

Results land in `BenchmarkDotNet.Artifacts/` (gitignored). Copy interesting
runs into the table below with the date and machine.

## Benchmarks included

- **ParseBench** — `DetachedTimestampFile.DeserializeFromArray` for three
  Python-reference fixtures of varying complexity.
- **SerializeBench** — `DetachedTimestampFile.SerializeToArray` for the
  same fixtures.
- **WalkBench** — `Timestamp.AllAttestations()` enumeration for tree-walking
  performance.

## Status

Benchmarks are advisory at this stage: they exist to detect regressions when
making perf-sensitive changes (e.g. tightening varuint paths, swapping
allocators). No regression gate is enforced in CI.

## Notes

- All Parse and Serialize benches operate on byte arrays / in-memory streams;
  filesystem cost is excluded.
- `MemoryDiagnoser` is enabled — watch the `Allocated` column for unintended
  allocation regressions.
- Use `--profiler ETW` (Windows) or `--profiler PerfCollect` (Linux) for
  deeper attribution when investigating a regression.
