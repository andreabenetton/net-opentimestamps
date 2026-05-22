# CLAUDE.md

## Purpose
Use this file as the default implementation context for this repo.
Do not restate the whole architecture in every prompt.
Optimize for correctness, determinism, byte-level interoperability with the
upstream OpenTimestamps reference clients, and clear separation between parsing,
serialization, and verification.

---

## Repo stance
This repo is:
- a protocol-conforming implementation of OpenTimestamps for .NET 9
- library-first, CLI second
- byte-compatible with the Python and JavaScript reference clients
- forward-compatible: unknown operations and attestations must round-trip verbatim
- trustless-by-default for verification; any non-trustless data source must be
  opt-in and clearly labelled at the API surface
- cross-platform: Windows, Linux, macOS — no platform-specific assumptions in
  the core library

---

## Source precedence
For implementation work, use this order:

1. The on-wire behavior of the canonical Python reference
   (`opentimestamps/python-opentimestamps`) — specifically `core/op.py`,
   `core/timestamp.py`, `core/notary.py`, `core/serialize.py`. If our code
   disagrees with what the Python reference produces or accepts, the Python
   reference wins.
2. `docs/protocol-notes.md` — our normative summary of the wire format; updated
   when the Python reference is consulted on a previously-undocumented detail.
3. `docs/architecture.md` — project layout, dependency direction, public API
   shape.
4. `docs/verification-model.md` — what guarantees verification gives the
   caller, and what trust categories each data source falls into.
5. `docs/cli-usage.md` — surface and exit-code contract of the `ots` CLI.

Rules:
- Wire format always wins for implementation. If the spec doc disagrees with
  the reference's actual emitted bytes, fix the spec doc.
- `docs/architecture.md` explains module boundaries and public API shape.
- `docs/verification-model.md` governs what counts as "verified" and the trust
  category of each block-header source.
- Existing code is not source of truth when it conflicts with the protocol.

The JavaScript reference (`opentimestamps/javascript-opentimestamps`) and Java
reference (`opentimestamps/java-opentimestamps`) are useful as cross-checks but
are not authoritative. When they disagree with the Python reference, the
Python reference wins — that is the implementation upstream calendars are
tested against.

---

## Non-negotiable architectural rules

### Wire format is the contract
The byte-level format of `.ots` files and calendar HTTP responses is the
public contract of this library. Round-tripping any valid input produced by
the reference clients must be byte-identical. If you cannot produce identical
bytes, the bug is in this codebase, not in the input.

### Preserve the unknown
Unknown attestation tags must be preserved as `UnknownAttestation(tag,
payload)` and re-emitted verbatim. Future operation tags are not currently
extensible (the op registry is closed-set in the reference), but the
attestation registry is open and we must round-trip forward-compatible
attestations without data loss.

### Determinism in serialization
The reference emits attestations in sorted order (by tag, then payload) and
operations in sorted order (by tag, then argument). Our serializer must do
the same so that a parse-then-serialize round-trip is byte-identical. Do not
emit ops or attestations in insertion order, hash-set iteration order, or any
other non-deterministic order.

### Cryptographic correctness
- SHA-1, SHA-256: `System.Security.Cryptography`.
- RIPEMD-160: `BouncyCastle.Cryptography` (`RipeMD160Digest`). .NET no longer
  ships RIPEMD-160 in the BCL.
- Keccak-256: `BouncyCastle.Cryptography` (`KeccakDigest(256)`). This is
  **Ethereum's Keccak-256**, not NIST SHA3-256. They differ in padding; using
  the wrong one silently produces incompatible digests. Do not substitute
  `Sha3Digest` for `KeccakDigest`.
- Do not roll our own primitives.
- Constant-time comparison is not required for OTS digests (they are not
  secrets), but never weaken to a `.SequenceEqual` over an attacker-controlled
  HMAC-style verification — there are none in OTS today, but if one is added,
  use `CryptographicOperations.FixedTimeEquals`.

### Parsing is not verification
Successfully parsing an `.ots` file means the bytes form a structurally valid
proof. It says nothing about whether the proof anchors to Bitcoin, whether
the anchored block exists, or whether the file matches. Keep these phases
separate:

1. Parse: bytes → in-memory `DetachedTimestampFile`.
2. Walk: replay each operation chain to produce intermediate commitments.
3. Verify: for each attestation tip, check the commitment matches the named
   external truth source (Bitcoin block merkle root at the named height).

Never short-circuit later phases by smuggling them into earlier ones. A
`Parse` call must not touch the network. A `Walk` call must not fetch block
headers. A `Verify` call is the only place that consults a
`BlockHeaderProvider`.

### Pending != confirmed
A `PendingAttestation` is the calendar's promise to anchor — not proof of
anchoring. Public-facing summaries (CLI output, library status enums) must
distinguish:

- `Incomplete` — file has only pending attestations; not yet anchored.
- `Anchored` — file has at least one Bitcoin attestation, but we have not
  verified the block header.
- `Verified` — at least one Bitcoin attestation verified against a block
  header from a configured `BlockHeaderProvider`, with the provider's trust
  category disclosed.

Never claim "verified" when only "anchored" has been shown.

### Trust categories are explicit at the API surface
`BlockHeaderProvider` implementations are tagged with a trust category:

- `LocalNode` — Bitcoin Core RPC, fully-validating node the user controls.
  Trustless given the node is honest.
- `TrustedHeaders` — user-supplied block header data, e.g. a static
  `headers.dat` mirror or an SPV header chain validated by the caller.
- `Explorer` — public block explorer (Esplora, Blockstream.info, etc.).
  **Not trustless**: caller is trusting a third party to report the correct
  merkle root.

CLI output, public API result types, and log lines must report which category
was consulted. Verification methods must accept the provider explicitly; no
ambient default.

### Calendar interactions are bounded
- `POST /digest` body ≤ 64 bytes.
- Response body ≤ 10000 bytes (matches reference cap).
- All calendar HTTP calls go through one `HttpClient` configured by the
  application; never construct a transient `HttpClient` per request.
- Upgrade fetches must respect a whitelist of calendar URI patterns (wildcard
  glob, matching the reference's defaults plus caller-supplied additions).
  Off-whitelist URIs are refused with a typed error, not silently allowed.

### Privacy nonce is mandatory on stamping
Stamping a file always appends a fresh 16-byte cryptographic nonce before the
final aggregator hash (mirrors the reference `cmds.py` flow). Without it,
two independent stamps of unrelated files can be cross-linked by their
shared aggregator path. Do not expose a public "stamp without nonce" API.

### Library is the contract; CLI is a wrapper
All real work lives in `OpenTimestamps`. The CLI handles argument parsing,
exit codes, stdout/stderr formatting, and exit codes only. If a CLI command
needs new behavior, add it to the library first.

Exit codes:
- `0` — success / verified
- `1` — operation failed (parse error, network error, calendar refused, etc.)
- `2` — proof is structurally valid but verification did not succeed (file
  hash mismatch, no Bitcoin attestation reachable, merkle root mismatch).
  Distinct from `1` so scripts can tell "could not verify" from "verified as
  invalid".
- `3` — usage / argument error

The exit-code contract is part of the public surface; treat changes to it
with the same care as a library API break.

### Time is external
- We do not use the local clock to make verification decisions.
- The proven timestamp returned from `Verify` is the `nTime` of the named
  Bitcoin block, reported as `DateTimeOffset` in UTC, and is documented as
  "data existed at or before this block's median time, per Bitcoin consensus".
- Client-side timestamps for logging are informational only.

---

## Required documentation updates

### Spec, architecture, verification, CLI changes
- Update `docs/protocol-notes.md` when consulting the Python reference clarifies
  a wire-format detail we had wrong, or when a new attestation type is added.
- Update `docs/architecture.md` when module boundaries, dependency direction,
  or the public API surface change.
- Update `docs/verification-model.md` when adding or changing a
  `BlockHeaderProvider`, when changing trust categories, or when changing
  what a status enum value means to a caller.
- Update `docs/cli-usage.md` for any user-visible CLI change — new command,
  new option, changed flag spelling, changed exit code, changed stdout/stderr
  format. The CLI's stdout format is consumed by scripts; treat it as API.

### Public API surface changes
Any change to a `public` symbol in `OpenTimestamps` is part of the library's
NuGet surface. Before merging:

1. Confirm the change is intentional — public surface drift is a compatibility
   break for downstream consumers.
2. Add a `[Obsolete(...)]` shim if a rename/move can be expressed that way.
3. Note the change in the `CHANGELOG.md` under the next unreleased version,
   under "Breaking" if it removes or renames anything.

`internal` symbols may be reshaped freely; `[InternalsVisibleTo("OpenTimestamps.Tests")]`
gives the test project access.

### Fixture additions
When adding a `.ots` fixture from upstream:

1. Copy the file into `tests/OpenTimestamps.Tests/fixtures/<source>/` where
   `<source>` names the upstream repo (e.g. `python-opentimestamps`).
2. Record the upstream commit SHA and path in
   `tests/OpenTimestamps.Tests/fixtures/PROVENANCE.md`.
3. Add at minimum a round-trip parser test that asserts byte-identical
   re-emission.

---

## Project layout conventions
Follow the layout already established. Do not invent a parallel structure.

| Type           | Location                                                  |
|----------------|-----------------------------------------------------------|
| Core library   | `src/OpenTimestamps/<Area>/`                              |
| CLI            | `src/OpenTimestamps.Cli/`                                 |
| Unit tests     | `tests/OpenTimestamps.Tests/<Area>/`                      |
| Integration    | `tests/OpenTimestamps.IntegrationTests/`                  |
| Fixtures       | `tests/OpenTimestamps.Tests/fixtures/<upstream-repo>/`    |
| Documentation  | `docs/<topic>.md` (lowercase-kebab)                       |

Rules:
- Namespaces match folder structure under the project root.
- One public top-level type per file (nested types and small internal helpers
  may share a file).
- File-scoped namespaces (enforced via `.editorconfig`).
- Avoid project-wide `using` directives except for genuinely ubiquitous types;
  prefer explicit `using` per file.

---

## Git discipline
After each logical unit of work:
- create a git commit
- push to the current branch

If push cannot be completed because of credentials, remote access, branch
protection, or environment limits:
- say so explicitly
- do not claim the push succeeded

Commit messages must be short, specific, and scoped to the actual change.
Do not leave completed logical units of work uncommitted.
Do not add a "Co-Authored-By" trailer to any commit message.

### Multi-fix prompts
When a single prompt asks for **more than one unrelated fix** (different
files, different bugs, different concerns — not the natural sub-tasks of one
feature), do not bundle them into a single commit. Instead, for each fix in
turn:

1. implement only that one fix
2. add or update only the tests directly related to it
3. run the impacted tests; verify they pass
4. create one commit scoped to that fix (commit message describing only it)
5. push, then move to the next fix

Each fix becomes one commit. Each commit is independently reviewable,
revertable, and bisectable. A multi-fix prompt produces N commits, not one.

Related sub-tasks of the same fix (e.g., a code change plus its test plus a
docstring update plus a doc cross-reference) belong in the same commit — they
are not "different fixes". The discriminator is whether the changes share a
single root cause or feature; if yes, one commit; if no, separate commits.

Do not bundle "while I'm here" cleanups into a fix commit. If a stale comment
or unrelated drift is discovered mid-fix, either: (a) note it explicitly and
defer it; or (b) handle it as its own follow-up commit after the in-scope fix
is committed.

### Library vs. CLI commits
When a change spans both `src/OpenTimestamps` (library) and
`src/OpenTimestamps.Cli` (CLI), prefer two commits:

1. Library change + library tests.
2. CLI change consuming the new library surface + CLI/integration tests.

This keeps the library independently reviewable and bisectable, and matches
the "library is the contract" stance. Shared edits that enable both commits
cleanly may ride with whichever commit needs them first; if both depend on
the same edit, put it in a preceding commit.

### Debugging hygiene

When chasing a bug across multiple commits, **do not squash the chain into a
single "fix X" commit**. Each independent root cause peeled back during the
investigation deserves its own commit, even when the surface symptom is the
same. The multi-fix rule above already governs this: the discriminator is
"single root cause vs. distinct root causes", not "user saw the same error
message". Squashing distinct fixes into one commit loses bisectability,
makes reverts blast-radius bigger than they should be, and hides the
diagnostic narrative future-you will want when the same symptom resurfaces.

What MUST be cleaned up before commit:

- **Diagnostic instrumentation added while chasing the bug.** Examples:
  `Console.WriteLine` traces in library code, per-call `Trace.WriteLine` you
  added to a hot path, dump-the-bytes shims, hex-dumping a digest mid-walk.
  These served their purpose finding the bug; leaving them in pollutes the
  log surface and wastes future-debugger attention on noise.
- **Throw-away one-shot fixtures.** Hardcoded calendar URLs, sample digests
  pasted from a curl session, `if (DEBUG) return early` shortcuts.
- **Commented-out code** from earlier hypotheses.

What is NOT diagnostic noise (keep it):

- A **structured warning on a real fallback path** the production code can
  take (e.g., "calendar refused, falling back to next aggregator"). That's a
  permanent operational signal a caller needs.
- A **catch-block log of swallowed exceptions** that previously surfaced
  silently. Silent swallowing is a bug magnet — the log is the fix.
- A **structured info log on a one-shot path** (e.g., "verified against
  block 800000 via LocalNode at <merkle-root>"). Fires once per
  verification, not per call.

Mechanically: either fold the cleanup into the same commit as the fix, OR
add the cleanup as a follow-up commit before pushing the chain. Do not push
diagnostic noise to main "to clean up later" — later rarely comes.

---

## Verifying changes before declaring done

This library is consumed two ways: through the .NET API and through the
`ots` CLI. Both must be exercised before claiming a change is done.

For library changes:
```
dotnet test tests/OpenTimestamps.Tests/OpenTimestamps.Tests.csproj
```

For CLI changes, run the actual binary against a real fixture:
```
dotnet run --project src/OpenTimestamps.Cli -- info tests/OpenTimestamps.Tests/fixtures/python-opentimestamps/hello-world.txt.ots
```

For changes touching calendar HTTP or external block-header providers,
exercise them under `tests/OpenTimestamps.IntegrationTests` (these may be
network-gated; skip them under `OTS_SKIP_NETWORK=1`). Do not declare a
change done by trusting that unit tests covered network behavior — they did
not.

If `dotnet test` fails or the CLI run does not produce the expected output,
the change is not done. Treat green tests + working CLI invocation as the
verification loop closing.

---

## Anti-patterns
Do not introduce:
- discarding unknown attestations on round-trip
- non-deterministic op or attestation ordering in the serializer
- conflating parse errors with verification errors at the API or exit-code
  level
- treating block-explorer responses as authoritative without an explicit
  trust category disclosed to the caller
- stamping APIs that omit the privacy nonce
- ambient `HttpClient` construction per call
- ambient `BlockHeaderProvider` default that hides the trust category
- new cryptographic primitives implemented inside this codebase
- substituting NIST SHA3-256 for Ethereum Keccak-256 (or vice versa)
- reading the local clock for verification decisions
- using `SequenceEqual` over a digest comparison that the caller might treat
  as security-sensitive (use `CryptographicOperations.FixedTimeEquals`)
- public symbols added without a `CHANGELOG.md` entry
- fixture files committed without a `PROVENANCE.md` entry
- documentation naming schemes that contradict this file
- network calls from unit tests (those belong in
  `OpenTimestamps.IntegrationTests`)
- swallowing exceptions silently in `catch (Exception)` blocks

---

## Completion checklist
Use this checklist internally before closing work.
Do not reproduce it in responses unless items are missing or need explicit callout.

- wire format respected (parse → serialize is byte-identical on all fixtures)
- unknown attestations round-trip verifies
- determinism preserved (sorted ops and attestations)
- crypto primitives are SHA-256 (BCL), RIPEMD-160 (BC), Keccak-256 (BC)
- trust category of each `BlockHeaderProvider` use is disclosed
- pending vs. anchored vs. verified status surfaced correctly
- parsing and verification kept separate in the public API
- tests added / updated
- CLI exercised against a real fixture when CLI changed
- public API change reflected in `CHANGELOG.md`
- fixture changes reflected in `PROVENANCE.md`
- relevant `docs/*.md` updated when behavior, format understanding, or trust
  model changed
- changes committed
- changes pushed, or push limitation explicitly reported

---

## Expected delivery format
For minor fixes, a short summary and commit status are sufficient.

For significant work, include:
1. What changed
2. Why
3. Wire-format / interop impact (if any)
4. Public API surface impact
5. Trust-model impact (if any)
6. Docs updated
7. Tests added / run
8. CLI verification (if CLI touched)
9. Known discrepancies / follow-ups
10. Commit and push status
11. Remaining implementation work implied by the change

Never present work as complete while a parse-then-serialize fixture round-trip
is failing, while a public API change is undocumented, or while a trust-model
change is unsurfaced in the affected docs. Never claim commit or push
completion if it did not actually happen.
