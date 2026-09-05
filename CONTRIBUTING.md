# Contributing to net-opentimestamps

Thanks for your interest. This guide covers how to build, test, and submit
changes. The repo's working agreement is documented in [`CLAUDE.md`](CLAUDE.md);
that's the authoritative reference for non-negotiable architectural rules
(wire format, trust model, exception types, etc.) — please read it before
opening a non-trivial PR.

## Build and test loop

Requirements:

- .NET 10 SDK (pinned in [`global.json`](global.json)).
- Optional, for the cross-implementation interop test: the Python
  [`opentimestamps-client`](https://github.com/opentimestamps/opentimestamps-client)
  CLI on `PATH`.

The full local loop:

```
dotnet build OpenTimestamps.sln --configuration Release
dotnet test  OpenTimestamps.sln --configuration Release
```

For UI-equivalent CLI verification:

```
dotnet run --project src/OpenTimestamps.Cli --framework net10.0 -- \
  info tests/OpenTimestamps.Tests/fixtures/python-opentimestamps/hello-world.txt.ots
```

Network-gated integration tests are off by default. Run them locally with:

```
dotnet test tests/OpenTimestamps.IntegrationTests
```

Skip them with `OTS_SKIP_NETWORK=1` (the value CI sets). Run the
Python-reference interop suite with `OTS_PYTHON_REF=1`.

## Commit discipline

Adapted from [`CLAUDE.md`](CLAUDE.md). Briefly:

- **One logical fix per commit.** A single PR may contain multiple commits;
  each should be independently reviewable, revertible, and bisectable.
- **No squashing of distinct fixes** when chasing a bug across multiple
  root causes. Each root cause deserves its own commit.
- **Clean up diagnostic instrumentation** before committing. Don't ship
  `Console.WriteLine` traces, commented-out experiments, or hardcoded test
  URLs in non-test code.
- **Library vs CLI commits split.** When a change spans both, prefer two
  commits — library + library tests first, CLI consumption second.
- Commit messages are short, specific, and scoped to the actual change.
- No `Co-Authored-By` trailer.

## Public API changes

Any change to a `public` symbol in `OpenTimestamps` is a NuGet surface
change. The PublicApiAnalyzers analyzer enforces this:

- New symbols: add them to `src/OpenTimestamps/PublicAPI.Unshipped.txt`.
- Removed symbols (rare; prefer `[Obsolete]` first): add a `*REMOVED*`
  line in `PublicAPI.Unshipped.txt`.
- Add a `CHANGELOG.md` entry under **Unreleased**.

## Testing requirements

- New library code: at least one targeted unit test in
  `tests/OpenTimestamps.Tests/`. Mock at the HTTP boundary, not at the
  protocol boundary — protocol bugs must be caught.
- New CLI flags or commands: exercise via `dotnet run --project src/OpenTimestamps.Cli --framework net10.0`
  against a real fixture and confirm the exit code and stdout shape.
- Network code: integration tests live under
  `tests/OpenTimestamps.IntegrationTests` and are gated by `OTS_SKIP_NETWORK=1`.
  Unit tests do not make network calls.

The coverage gate enforces minimum line and branch coverage in CI
(see `.github/workflows/ci.yml`). Bumping the floor is welcomed; lowering
it is not.

## Filing issues

- Bug reports: please include the .ots fixture (attached or inline as hex)
  that reproduces, plus the expected vs actual behaviour. Network-dependent
  bugs need the calendar / explorer URL.
- Feature requests: please cite the upstream behaviour we'd be matching, if
  any. We deliberately defer to the Python reference's on-wire behaviour
  (see [`docs/protocol-notes.md`](docs/protocol-notes.md)).
- Security issues: see [`SECURITY.md`](SECURITY.md). Don't file these
  publicly.

## Code of conduct

By participating you agree to abide by the [Contributor Covenant
2.1](CODE_OF_CONDUCT.md).
