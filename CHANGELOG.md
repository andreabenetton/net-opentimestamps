# Changelog

This project uses [Semantic Versioning](https://semver.org/). The wire format
follows the OpenTimestamps protocol and is not subject to our versioning;
versioning here applies to the .NET public API surface and the `ots` CLI.

## Unreleased

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
