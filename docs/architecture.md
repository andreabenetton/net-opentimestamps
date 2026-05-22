# Architecture

`net-opentimestamps` is a small, library-first .NET implementation of the
OpenTimestamps client protocol. This document describes the module layout,
public API shape, and dependency direction.

## Solution layout

```
src/
  OpenTimestamps/             # core library, the public surface
  OpenTimestamps.Cli/         # the `ots` CLI tool (executable, packed as a dotnet tool)
tests/
  OpenTimestamps.Tests/       # unit tests + fixture round-trip tests, no network
  OpenTimestamps.IntegrationTests/  # network-dependent tests; gated by OTS_SKIP_NETWORK=1
docs/                         # this directory
```

## Dependency direction

```
OpenTimestamps.Cli            ──▶  OpenTimestamps  ──▶  BouncyCastle.Cryptography
                                                  ──▶  System.Net.Http
                                                  ──▶  System.Security.Cryptography
```

The library never depends on the CLI. The library never depends on a
network stack unless the caller hands it an `HttpClient`. Tests do not depend
on the CLI.

## Module map (under `src/OpenTimestamps/`)

| Folder            | Responsibility                                                                 |
|-------------------|--------------------------------------------------------------------------------|
| `Serialization/`  | `OtsReader`/`OtsWriter` and exceptions; LEB128 varuint, varbytes, magic, EOF.  |
| `Ops/`            | `Op` hierarchy: `Op` → `UnaryOp` / `BinaryOp` / `CryptOp` and the eight concrete ops. |
| `Attestations/`   | `TimeAttestation` hierarchy: Pending, Bitcoin / Litecoin / Ethereum, Unknown.  |
| `Calendars/`      | `CalendarClient`, `CalendarWhitelist`, `DefaultCalendars`.                     |
| `Stamping/`       | `StampService` (with mandatory privacy nonce), `UpgradeService`.               |
| `Verification/`   | `BlockHeaderProvider` + concrete `Esplora` and `BitcoinCoreRpc` providers; `VerificationService`. |
| (root)            | `Timestamp` (the tree node), `DetachedTimestampFile` (the .ots envelope).      |

## Public surface (high-level)

### Parsing and serialization

```csharp
DetachedTimestampFile dtf = DetachedTimestampFile.DeserializeFromFile("foo.txt.ots");
dtf.SerializeToFile("foo.txt.ots");
byte[] bytes = dtf.SerializeToArray();
DetachedTimestampFile fromBytes = DetachedTimestampFile.DeserializeFromArray(bytes);
```

### Stamping

```csharp
using var http = new HttpClient();
var calendars = DefaultCalendars.Aggregators
    .Select(uri => new CalendarClient(http, new Uri(uri)))
    .ToList();

var svc = new StampService();
DetachedTimestampFile dtf = await svc.StampFileAsync("foo.txt", calendars);
dtf.SerializeToFile("foo.txt.ots");
```

### Upgrade

```csharp
var whitelist = CalendarWhitelist.Default;
var svc = new UpgradeService(whitelist, uri => new CalendarClient(http, uri));
UpgradeResult result = await svc.UpgradeAsync(dtf);
```

### Verification

```csharp
var provider = new EsploraBlockHeaderProvider(http, new Uri("https://blockstream.info/api/"));
var svc = new VerificationService();
VerificationResult result = await svc.VerifyFileAsync(dtf, "foo.txt", provider);
if (result.Status == TimestampStatus.Verified)
{
    Console.WriteLine($"existed at or before {result.EarliestVerifiedTime}");
}
```

## Design rules (enforced)

1. **Wire format is the contract.** `Parse → Serialize` round-trips byte-identically
   on every upstream fixture. The serializer emits attestations and ops in canonical
   sorted order so the output is deterministic regardless of insertion order.

2. **Preserve the unknown.** `UnknownAttestation(tag, payload)` retains the raw
   payload bytes verbatim and re-emits them on serialization — proofs containing
   future attestation types still round-trip without data loss.

3. **Parsing is not verification.** `DetachedTimestampFile.Deserialize` touches no
   network and verifies no commitment. `VerificationService.VerifyAsync` is the
   only entry point that consults a `BlockHeaderProvider`.

4. **Trust categories are explicit.** Every `BlockHeaderProvider` declares its
   `TrustCategory` (LocalNode, TrustedHeaders, or Explorer). The category appears
   in every `VerifiedAttestation` so the caller can decide whether the bar was met.

5. **Privacy nonce is mandatory on stamping.** `StampService` always applies
   `OpAppend(random16) → OpSHA256` before submitting to a calendar. There is no
   public "stamp without nonce" overload.

6. **HttpClient is injected.** Calendar and explorer clients take an
   `HttpClient` in their constructor; the library never builds one per call.

7. **Time is external.** Verification never reads the system clock to make
   decisions. The proven timestamp is the Bitcoin block's `nTime`.

## Cross-cutting conventions

- File-scoped namespaces, one public top-level type per file.
- All public symbols are nullable-annotated; `Nullable` is enabled solution-wide.
- `Directory.Build.props` enables `TreatWarningsAsErrors` and `AnalysisLevel=latest`.
- Central package management via `Directory.Packages.props`.
- `[InternalsVisibleTo("OpenTimestamps.Tests")]` lets the unit tests reach
  internal helpers; the integration project gets the same.

## Versioning and stability

- The library is pre-1.0 (`0.1.0`). Public API may change between minor versions
  until 1.0; treat every signature change as breaking and call it out in
  `CHANGELOG.md`.
- The wire format is the OpenTimestamps protocol's wire format; it is fixed by
  the reference clients and not subject to our versioning choices.
- The `ots` CLI exit-code contract (0/1/2/3) is treated as public API.
