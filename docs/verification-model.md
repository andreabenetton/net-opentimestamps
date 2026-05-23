# Verification model

What "verified" means in this library, and what trust assumptions each
verification mode carries.

## What verification proves

If `VerificationService.VerifyFileAsync` returns
`TimestampStatus.Verified`, then for the candidate file `F`:

1. `file_hash_op(F)` equals the file digest committed in the `.ots` proof.
2. Walking the proof's operation chains from that digest produces, at some
   leaf, the merkle root of a Bitcoin block at the height named by a
   `BitcoinBlockHeaderAttestation` in the proof.
3. That merkle root was supplied by a `BlockHeaderProvider` whose
   `TrustCategory` is part of the result.

What that gives the caller is the Bitcoin consensus assertion that "the data
identified by this file digest existed at or before block `H`'s time." The
attestation is asymmetric: it bounds the data's existence from above (it
cannot have been authored after the block), not from below (the data may have
existed long before the block — Bitcoin can only assert when it was
committed, not when it was created).

`EarliestVerifiedTime` is the UTC instant of the earliest verified block's
`nTime`. Per Bitcoin consensus, this is the upper bound on the data's
creation time.

## What verification does not prove

- **Not when the file was created.** Bitcoin can only attest that a commitment
  was published *by* block `H`; the underlying data may be much older.
- **Not who created the file.** OpenTimestamps is a commitment scheme, not a
  signature scheme; there is no notion of author identity.
- **Not whether the file is meaningful, true, or correct.** Verification only
  ties bytes to a block-time bound.
- **Not whether the Bitcoin chain you verified against is the canonical
  one.** That is the caller's responsibility (see "Trust categories" below).
- **Not absence of forgery in the proof itself, unless** the merkle-root match
  succeeds against a block header the caller has independently checked is on
  Bitcoin's main chain at the named height.

## Statuses

`VerificationResult.Status` reports one of three values:

| Status       | Meaning                                                                                   |
|--------------|-------------------------------------------------------------------------------------------|
| `Incomplete` | The proof contains only pending calendar attestations. No on-chain anchor exists yet.     |
| `Anchored`   | The proof contains ≥ 1 Bitcoin attestation, but no `BlockHeaderProvider` was supplied to verify against. |
| `Verified`   | ≥ 1 Bitcoin attestation was matched against a block header from a configured provider.    |

A claim of "verified" without disclosing the `TrustCategory` is incomplete.
The CLI always prints both, and library code should never present a `Verified`
result without also surfacing the provider name + trust category.

## Trust categories

Every `BlockHeaderProvider` declares a `TrustCategory`.

### `LocalNode`

Source: Bitcoin Core (or another fully-validating node the caller controls)
over its JSON-RPC interface. Implementation:
`BitcoinCoreRpcBlockHeaderProvider`.

Trust: **trustless, given the node is honest about its own chain state.**
A fully-validating node enforces consensus rules itself, so it does not need
to trust any third party for the block headers it returns. The caller is
trusting that the RPC endpoint is, in fact, a real Bitcoin Core node they
control — the library does not police that on its own.

### `TrustedHeaders`

Source: a static block-header file (`headers.dat`) or an SPV header chain the
caller has independently validated. (Not implemented in this library yet; the
category exists so callers can implement their own provider.)

Trust: **as trustless as the caller's own validation of the header chain.**
If you stored a `headers.dat` produced by a node you trust, this is
equivalent to `LocalNode`.

### `Explorer`

Source: a public block explorer (Esplora at `blockstream.info` or
`mempool.space`, etc.). Implementation: `EsploraBlockHeaderProvider`.

Trust: **NOT trustless.** The caller is trusting the explorer operator to
report the correct merkle root. An attacker who controls the explorer can
silently fail to verify a real proof, or succeed-verify a forged one (by
serving forged headers). Use this category when convenience matters more
than tamper-resistance — most users running ad-hoc `ots verify` against a
file fall in this category. The CLI prints a clarifying note when
verification used an `Explorer`-class provider.

## Pending != confirmed

A `PendingAttestation` is a calendar's promise to anchor the commitment in a
future Bitcoin block. It is **not** proof of anchoring; reading the URI of a
pending attestation, or seeing it succeed-parse, says nothing about whether
Bitcoin has accepted the calendar's transaction.

Public-facing summaries (CLI output, library result types) must distinguish:

- **Incomplete:** only pending attestations exist; the data is not yet
  anchored.
- **Anchored:** at least one `BitcoinBlockHeaderAttestation` is in the proof,
  but the caller has not (or has not been asked to) check a block header for
  it.
- **Verified:** the merkle-root match succeeded against a header from a
  configured `BlockHeaderProvider`.

Never call something "verified" when only "anchored" has been demonstrated.

## Calendar URI whitelist

`UpgradeService` requires every pending-attestation URI to match a
`CalendarWhitelist` pattern before issuing a `GET /timestamp/{hex}`. The
defaults in `CalendarWhitelist.Default` are the same patterns the Python
reference accepts. Callers can extend the whitelist (e.g., to test against a
local calendar instance) by passing additional patterns; off-whitelist URIs
are surfaced as `UpgradeResult.Skipped` rather than silently contacted.

This is a *privacy* and *availability* guard, not a trust guard: a malicious
pending URI cannot have already corrupted the existing proof, but contacting
it would leak the caller's interest in this commitment to an arbitrary
endpoint of the attacker's choosing.

## Replay and audit

The Bitcoin attestations in a proof are immutable: a block at a given height
has one merkle root forever (modulo a chain reorganisation that re-orders
blocks). A `Verified` result computed today should remain `Verified`
tomorrow as long as the underlying chain is unchanged at that height. If a
caller wants to audit a verification result later, the right thing to record
is `(file_digest, attestation_height, attestation_merkle_root, block_time,
provider_name, trust_category)`; any subsequent provider returning the same
merkle root at the same height confirms the same fact.

## Time semantics

- Verification never reads the local system clock to make a decision.
- The proven instant is `block.nTime` for the verified attestation, surfaced
  as `DateTimeOffset` in UTC.
- A block's `nTime` is loosely constrained by Bitcoin consensus (must exceed
  the median of the previous 11 blocks; must not exceed network-adjusted
  time + 2 hours). The reported time is therefore an approximation of when
  the block was mined, accurate to within a few hours. For OTS, the relevant
  property is the upper bound, not the precise minute.

## Multi-chain verification

The library can verify attestations against Bitcoin, Litecoin, and Ethereum.
Each chain has its own provider abstraction
(`BlockHeaderProvider`, `LitecoinBlockHeaderProvider`,
`EthereumBlockHeaderProvider`), passed to
`VerificationService.VerifyMultiChainAsync` via `VerifyOptions`. A proof is
reported `Verified` if **any** chain attestation in it was successfully
verified.

Trust posture per chain:

- **Bitcoin** — `LocalNode` is trustless given an honest node; `TrustedHeaders`
  is trustless given the supplied headers; `Explorer` is third-party-trusted.
- **Litecoin** — same model as Bitcoin: an honest local Litecoin Core node
  would be trustless; the bundled `LitecoinSpaceBlockHeaderProvider` is
  `Explorer`-trusted.
- **Ethereum** — **advisory only.** The OTS Ethereum attestation commits to
  the block's `transactionsRoot`. Pre-Merge (block < 15537394), the
  containing header was PoW-anchored and verifying the transactions root
  matched a real, work-secured block. Post-Merge, the header containing
  that field is no longer cryptographically PoW-anchored at the block
  level — it's attested by validator signatures over the beacon chain.
  This means a sufficiently-resourced attacker can produce a parallel
  header containing whatever `transactionsRoot` they choose. We therefore
  classify any `EthereumBlockHeaderProvider`'s trust category as
  `Explorer` regardless of how it's hosted, and recommend Ethereum
  verification be treated as informational alongside (never instead of)
  Bitcoin verification.

This matches the posture documented in the upstream Python reference,
where `EthereumBlockHeaderAttestation` is annotated as "dubious".

## Caching and persistence

`CachingBlockHeaderProvider` is a decorator over any other
`BlockHeaderProvider`. In its in-memory-only form it amortises the cost of
re-verifying the same proof; LRU eviction enforces a configurable cap.
Faulted fetches are never cached — a transient failure does not poison the
cache.

For persistence across process restarts, supply an `IHeaderCacheStore` via
the optional `store:` constructor parameter. The bundled
`FileBackedHeaderCacheStore` writes one JSON record per line to a file the
caller supplies. The store is consulted on miss before the inner provider;
successful inner fetches write through to the store.

**Trust propagates from the inner provider, not the store.** If your inner
provider is `LocalNode`, the cached entries are `LocalNode`-trusted forever
for that file. If your inner provider is `Explorer`, the cached entries are
`Explorer`-trusted forever for that file — caching a third-party answer
does not make it trustless. This is by design: if you cached an `Explorer`
answer under a `LocalNode` label, a single rogue/compromised explorer
response would silently poison every future verification.

If you want a persistent trustless cache, run your verification with an
inner `LocalNode` provider once and save the resulting store file. Future
verifications against that file remain `LocalNode`-trusted even when the
node is offline.
