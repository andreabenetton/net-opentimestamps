# Changelog

This project uses [Semantic Versioning](https://semver.org/). The wire format
follows the OpenTimestamps protocol and is not subject to our versioning;
versioning here applies to the .NET public API surface and the `ots` CLI.

## Unreleased

Nothing yet.

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
