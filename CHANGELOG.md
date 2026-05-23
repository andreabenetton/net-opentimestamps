# Changelog

This project uses [Semantic Versioning](https://semver.org/). The wire format
follows the OpenTimestamps protocol and is not subject to our versioning;
versioning here applies to the .NET public API surface and the `ots` CLI.

## Unreleased

### Added

- Multi-chain verification scaffolding:
  - `OpenTimestamps.Verification.ChainId` enum (`Bitcoin`, `Litecoin`, `Ethereum`).
  - `OpenTimestamps.Verification.LitecoinBlockHeaderProvider` (abstract).
  - `OpenTimestamps.Verification.LitecoinSpaceBlockHeaderProvider` —
    concrete provider against an Esplora-compatible Litecoin explorer
    (defaults to `https://litecoinspace.org/api/`). Trust category:
    `Explorer`.
  - `OpenTimestamps.Verification.EthereumBlockHeaderProvider` (abstract).
  - `OpenTimestamps.Verification.JsonRpcEthereumBlockHeaderProvider` —
    concrete Ethereum provider against any Ethereum JSON-RPC endpoint
    (e.g. Cloudflare's public ETH gateway, self-hosted geth/erigon).
    Trust category: `Explorer` regardless of hosting — Ethereum
    verification is advisory; see `docs/verification-model.md` for the
    post-Merge caveat.
  - `OpenTimestamps.Verification.VerifyOptions` — bundles optional
    per-chain providers.
  - `VerificationService.VerifyMultiChainAsync` /
    `VerifyFileMultiChainAsync` — sibling methods taking
    `VerifyOptions`. Bitcoin-only callers should keep using the
    existing `VerifyAsync` / `VerifyFileAsync`.
  - `VerificationResult.LitecoinAttestations`, `.EthereumAttestations` —
    per-chain attestation lists.
  - `VerifiedAttestation.Chain` init-only property distinguishing which
    chain a successfully verified attestation anchors to.

### Changed

- Litecoin attestations encountered during verification with no
  Litecoin provider supplied no longer surface as a warning. They
  appear in `VerificationResult.LitecoinAttestations` and contribute
  to `Anchored` status when no chain has been verified yet. (Same
  applies to Ethereum attestations.)

## 1.0.0 — first stable release

The first release with the API surface frozen under
`Microsoft.CodeAnalysis.PublicApiAnalyzers`. Every public symbol present
at this version is captured in `src/OpenTimestamps/PublicAPI.Shipped.txt`;
post-1.0 changes will surface as deliberate diffs in `PublicAPI.Unshipped.txt`.

### Production-grade infrastructure landed since 0.1.0

- Property-style parser fuzz harness (`tests/.../Fuzz/`) — any unhandled
  exception escaping the parser fails the test.
- Cross-implementation interop tests against the Python reference CLI
  (gated by `OTS_PYTHON_REF=1`).
- Fixture corpus expanded from 6 (Python-only) to 14 across Python, Java,
  and JavaScript references, exercised via directory enumeration.
- `Microsoft.Extensions.Logging.Abstractions`-based `ILogger` plumbing on
  every consumer-facing service.
- Persistent file-backed block-header cache
  (`FileBackedHeaderCacheStore`) — trust category inherits from inner
  provider.
- BenchmarkDotNet baseline (parse / serialize / walk).
- GitHub Actions CI: cross-platform matrix, coverage threshold gate,
  CodeQL static analysis, Dependabot for `nuget` + `github-actions`,
  signed release pipeline (NuGet push + GitHub release + CycloneDX SBOM).
- `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, CODEOWNERS,
  issue templates, PR template.
- `samples/StampVerifyDemo` — runnable end-to-end demo.
- `docs/versioning.md`, `docs/releasing.md` — SemVer policy + release flow.

### Added

- `OpenTimestamps.TimestampMergeException` — typed exception thrown by
  `Timestamp.Merge` when the operand's `Msg` differs from the receiver's.
  Inherits from `InvalidOperationException` so existing catch blocks still
  match.
- `OpenTimestamps.Serialization.VarUIntOverflowException` — typed exception
  thrown when a LEB128 varuint on the wire exceeds the 64-bit value range.
  Replaces the previous plain `DeserializationException` thrown by
  `OtsReader.ReadVarUInt` in that case. `catch (DeserializationException)`
  blocks continue to match.
- `CalendarException(string message, int httpStatus, Exception? innerException)`
  constructor — chains the underlying cause when a non-success response body
  fails to read.
- `<exception>` XML doc tags on the most consumer-facing public methods of
  `Timestamp`, `DetachedTimestampFile`, `StampService`, `CalendarClient`, and
  `VerificationService` — exception contract is now part of the documentation.

### Fixed

- `CalendarClient` no longer silently swallows body-read failures on
  non-success HTTP responses. The read failure is preserved on
  `CalendarException.InnerException` and the message reports `(unreadable: <type>)`
  instead of `(no body)`.

### Changed

- `CachingBlockHeaderProvider` now evicts the least-recently-used entry
  when the cap is exceeded (was: clear the entire cache). Faulted lookups
  are no longer cached — the next caller retries the inner provider, which
  matters for transient network failures. Default `maxEntries` raised from
  4096 to 8192 (≈800 days of Bitcoin blocks).
- `OpenTimestamps.Verification.IHeaderCacheStore` and
  `OpenTimestamps.Verification.FileBackedHeaderCacheStore` — pluggable
  persistent backing store for block headers. Compose via the optional
  `store:` parameter on `CachingBlockHeaderProvider`. Trust category
  inherits from the inner provider; see `docs/verification-model.md`.
- Library now references `Microsoft.Extensions.Logging.Abstractions`.
  `CalendarClient`, `StampService`, `UpgradeService`, `VerificationService`,
  `CachingBlockHeaderProvider`, `EsploraBlockHeaderProvider`, and
  `BitcoinCoreRpcBlockHeaderProvider` each gain a final optional
  `ILogger? logger = null` constructor parameter. Defaults to
  `NullLogger.Instance`. Source- and binary-compatible for any call site
  using positional args ≤ the previous parameter count.

## 0.1.0 — initial release

Public API introduced:

- `OpenTimestamps.DetachedTimestampFile` — `.ots` envelope (parse, serialize,
  hash-from-file/stream/bytes factories).
- `OpenTimestamps.Timestamp` — tree node with sorted serialization, merge,
  enumeration helpers.
- `OpenTimestamps.Ops.*` — `Op`, `UnaryOp`, `BinaryOp`, `CryptOp`, and the
  eight concrete ops (`OpSha1`, `OpSha256`, `OpRipemd160`, `OpKeccak256`,
  `OpAppend`, `OpPrepend`, `OpReverse`, `OpHexlify`).
- `OpenTimestamps.Attestations.*` — `TimeAttestation` plus `PendingAttestation`,
  `BitcoinBlockHeaderAttestation`, `LitecoinBlockHeaderAttestation`,
  `EthereumBlockHeaderAttestation`, `UnknownAttestation`.
- `OpenTimestamps.Calendars.*` — `CalendarClient`, `CalendarWhitelist`,
  `DefaultCalendars`, `CalendarException`.
- `OpenTimestamps.Stamping.StampService` (mandatory privacy nonce),
  `OpenTimestamps.Stamping.UpgradeService`.
- `OpenTimestamps.Verification.*` — `BlockHeaderProvider`, `EsploraBlockHeaderProvider`,
  `BitcoinCoreRpcBlockHeaderProvider`, `VerificationService`, `VerificationResult`,
  `TimestampStatus`, `TrustCategory`, `FileDigestMismatchException`,
  `VerifiedAttestation`.
- `OpenTimestamps.Serialization.*` — `OtsReader`, `OtsWriter`,
  `DeserializationException`, `RecursionLimitException`,
  `UnsupportedMajorVersionException`, `OpMessageException`.

CLI:

- `ots stamp / verify / upgrade / info` with exit codes `0 / 1 / 2 / 3`.
