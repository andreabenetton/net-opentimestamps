# net-opentimestamps

A native .NET 9 implementation of the [OpenTimestamps](https://opentimestamps.org/)
protocol — a library and CLI for creating, parsing, upgrading, and verifying
detached `.ots` proofs that anchor data to the Bitcoin blockchain.

This implementation is byte-compatible with the upstream Python and JavaScript
reference clients: parse-then-serialize round-trips every upstream fixture
byte-identically, and proofs produced here are accepted by the reference
clients (and vice versa).

## Project layout

```
src/OpenTimestamps/             Core library (NuGet: OpenTimestamps)
src/OpenTimestamps.Cli/         Command-line tool `ots` (NuGet: OpenTimestamps.Cli)
tests/OpenTimestamps.Tests/                 Unit + fixture round-trip tests, no network
tests/OpenTimestamps.IntegrationTests/      Network-gated integration tests
docs/                                       Architecture, protocol notes, CLI usage, trust model
```

## Quickstart (library)

```csharp
using OpenTimestamps;
using OpenTimestamps.Calendars;
using OpenTimestamps.Stamping;
using OpenTimestamps.Verification;

using var http = new HttpClient();

// Stamp a file against the public default calendars.
var calendars = DefaultCalendars.Aggregators
    .Select(uri => new CalendarClient(http, new Uri(uri)))
    .ToList();
var dtf = await new StampService().StampFileAsync("contract.pdf", calendars);
dtf.SerializeToFile("contract.pdf.ots");

// Hours later, upgrade pending attestations into Bitcoin block attestations.
var upgrade = new UpgradeService(CalendarWhitelist.Default,
                                 uri => new CalendarClient(http, uri));
await upgrade.UpgradeAsync(dtf);
dtf.SerializeToFile("contract.pdf.ots");

// Verify against Bitcoin (using a public explorer; not trustless — see docs/verification-model.md).
var provider = new EsploraBlockHeaderProvider(http, new Uri("https://blockstream.info/api/"));
var result = await new VerificationService().VerifyFileAsync(dtf, "contract.pdf", provider);
Console.WriteLine($"{result.Status}: existed at or before {result.EarliestVerifiedTime}");
```

## Quickstart (CLI)

```
ots stamp contract.pdf
ots upgrade contract.pdf.ots
ots verify contract.pdf --explorer https://blockstream.info/api/
ots info contract.pdf.ots
```

Exit codes:

- `0` success / verified
- `1` operation failed
- `2` proof valid but verification did not succeed
- `3` usage error

See [`docs/cli-usage.md`](docs/cli-usage.md) for full flag reference.

## Building and testing

```
dotnet build OpenTimestamps.sln
dotnet test                                 # all tests
OTS_SKIP_NETWORK=1 dotnet test              # skip network-gated integration tests
```

## Documentation

- [`docs/architecture.md`](docs/architecture.md) — module layout and public API shape.
- [`docs/protocol-notes.md`](docs/protocol-notes.md) — wire format, encoding, calendar HTTP API.
- [`docs/verification-model.md`](docs/verification-model.md) — what "verified" means and the trust categories.
- [`docs/cli-usage.md`](docs/cli-usage.md) — `ots` CLI reference.
- [`CLAUDE.md`](CLAUDE.md) — implementation guidance, non-negotiable rules, anti-patterns.

## Status

Pre-1.0 (`0.1.0`). The wire-format implementation is exercised against the
upstream fixtures and verified end-to-end against the live Bitcoin network
via Esplora. The public API may evolve before 1.0; treat every signature
change as breaking.

Not implemented yet:

- Litecoin / Ethereum attestation verification (round-tripped, but the
  block-header providers don't speak those chains).
- Multi-file merkle aggregation when stamping (the reference's batch-stamp mode).
- A persistent local block-header cache to avoid hitting Esplora on every
  verification.

## License

LGPL-3.0-or-later, matching the upstream OpenTimestamps reference clients.
See [LICENSE](LICENSE) for the LGPL-3.0 text and [LICENSE.GPL](LICENSE.GPL)
for the GPL-3.0 text that the LGPL incorporates by reference.
