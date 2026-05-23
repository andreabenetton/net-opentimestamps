# `ots` CLI usage

The `ots` command-line tool wraps the `OpenTimestamps` library and supports
four operations: `stamp`, `verify`, `upgrade`, and `info`.

## Installation

After building the solution, the CLI is at
`src/OpenTimestamps.Cli/bin/Debug/net9.0/ots.dll` and can be run with
`dotnet`:

```
dotnet src/OpenTimestamps.Cli/bin/Debug/net9.0/ots.dll <command> ...
```

For day-to-day use, pack and install as a `dotnet tool`:

```
dotnet pack src/OpenTimestamps.Cli -c Release
dotnet tool install --global --add-source ./src/OpenTimestamps.Cli/bin/Release OpenTimestamps.Cli
ots --version
```

## Global flags

| Flag        | Effect                          |
|-------------|---------------------------------|
| `--help`    | Show help and exit.             |
| `--version` | Print the version and exit.     |

## Exit codes

The CLI's exit-code contract is part of the public surface. Scripts can
distinguish failure modes reliably:

| Code | Meaning                                                                 |
|------|-------------------------------------------------------------------------|
| `0`  | Success / verified.                                                     |
| `1`  | Operation failed (parse error, network error, calendar refused, etc.).  |
| `2`  | Proof is structurally valid but verification did not succeed (file hash mismatch, no Bitcoin attestation reachable, merkle-root mismatch). |
| `3`  | Usage / argument error.                                                 |

## `ots info <proof.ots>`

Print a human-readable dump of the proof tree: the file hash op, the file
digest, and every operation chain leading to each attestation.

```
ots info hello-world.txt.ots
```

## `ots stamp <file>... [--calendar URL]... [--quorum N] [--output PATH]`

Hash each `<file>` with SHA-256, append a fresh per-file 16-byte privacy nonce,
hash again to get a per-file commitment. When stamping a single file, the
commitment is submitted directly to each calendar. When stamping multiple
files, the per-file commitments are folded into a merkle tree and only the
root is submitted — one calendar round-trip for the whole batch — and each
file's proof records its leaf-to-root merkle path.

| Option                | Default                                | Notes                                                              |
|-----------------------|----------------------------------------|--------------------------------------------------------------------|
| `--calendar URL`      | the four default aggregators            | May repeat. Each URL is contacted in parallel.                     |
| `--quorum N`          | `2`                                     | The minimum number of calendars that must accept the stamp.        |
| `--output PATH`       | `<file>.ots`                            | Single-file only. Batch invocations always write each file's proof as `<file>.ots` next to it. Refuses to overwrite an existing file. |

`ots stamp` produces an `INCOMPLETE` proof — each output `.ots` contains
pending attestations from the calendars that accepted the batch. Run
`ots upgrade <proof.ots>` later (typically a few hours after stamping) to
merge in the Bitcoin block-header attestation. Every file in a batch
verifies independently after the upgrade — they share the calendar
attestation via the merkle root.

Batch example:

```
$ ots stamp doc1.pdf doc2.pdf doc3.pdf
Stamped doc1.pdf -> doc1.pdf.ots
  file digest: abc...
Stamped doc2.pdf -> doc2.pdf.ots
  file digest: def...
Stamped doc3.pdf -> doc3.pdf.ots
  file digest: 012...
  pending calendar: https://alice.btc.calendar.opentimestamps.org
  pending calendar: https://bob.btc.calendar.opentimestamps.org
Run `ots upgrade <proof>` later (typically a few hours) to merge in the Bitcoin attestation.
```

## `ots upgrade <proof.ots> [--allow-calendar PATTERN]... [--no-backup]`

Poll each pending calendar for its attestation. When a calendar responds
`200 OK`, merge the returned subtree into the existing proof. When it
responds `404 Not Found`, leave the pending attestation in place and try
again later.

| Option                       | Default                                  | Notes                                                                  |
|------------------------------|------------------------------------------|------------------------------------------------------------------------|
| `--allow-calendar PATTERN`   | `CalendarWhitelist.Default` patterns      | Additional wildcard glob patterns to permit (useful for local calendars). |
| `--no-backup`                | (off)                                     | Skip writing `<proof.ots>.bak` alongside the upgraded proof.           |

Off-whitelist pending URIs are surfaced as "Skipped" rather than silently
contacted.

## `ots verify <file> [--proof PATH] [--explorer URL | --bitcoin-rpc URL [--rpc-user U --rpc-password P]] [--litecoin-explorer URL]`

Hash `<file>`, compare it to the digest committed in the proof, then walk the
tree and check every chain attestation's commitment against the merkle
root of the named block.

| Option                                 | Default                | Notes                                                                                            |
|----------------------------------------|------------------------|--------------------------------------------------------------------------------------------------|
| `--proof PATH`                         | `<file>.ots`            | Path to the proof.                                                                              |
| `--explorer URL`                       | (off)                   | Bitcoin: Esplora-compatible block explorer. **Trust category: `Explorer`** (not trustless).      |
| `--bitcoin-rpc URL`                    | (off)                   | Bitcoin: Bitcoin Core JSON-RPC endpoint. **Trust category: `LocalNode`** (trustless given the node). |
| `--rpc-user U` / `--rpc-password P`    | (none)                  | Basic-auth credentials for the Bitcoin RPC endpoint.                                            |
| `--litecoin-explorer URL`              | (off)                   | Litecoin: Esplora-compatible Litecoin explorer (e.g. `https://litecoinspace.org/api/`). **Trust category: `Explorer`**. |
| `--ethereum-rpc URL`                   | (off)                   | Ethereum: JSON-RPC endpoint (e.g. `https://cloudflare-eth.com`). **Trust category: `Explorer`** — advisory only post-Merge; see `docs/verification-model.md`. |
| `--eth-rpc-user U` / `--eth-rpc-password P` | (none)             | Basic-auth credentials for the Ethereum RPC endpoint. |

A proof is `VERIFIED` if **any** chain attestation in it was successfully
verified against the corresponding provider — Bitcoin, Litecoin, or
Ethereum. If you've supplied a Litecoin provider but not a Bitcoin one,
Bitcoin attestations show in the printout as merely anchored, while Litecoin
attestations get verified — and the file is reported `VERIFIED` if even one
Litecoin attestation matches its block's merkle root.

When no header source is supplied for any chain, `ots verify` reports
`ANCHORED` (proof contains chain attestations but they were not checked) or
`INCOMPLETE` (proof contains only pending attestations) and exits with code `2`.

When an `Explorer` provider is used, the output includes a note that
verification used a block explorer and is therefore not trustless. Use
`--bitcoin-rpc` (or a self-hosted Litecoin RPC, once supported) against a
node you control for trustless verification.

### Example output

A successful Esplora-based verify of the upstream `hello-world.txt` fixture:

```
$ ots verify hello-world.txt --explorer https://blockstream.info/api/
VERIFIED: hello-world.txt existed at or before 2015-05-28 15:41:18 UTC.
  bitcoin block 358391 time 2015-05-28 15:41:18 UTC (source: blockstream.info, trust: Explorer)
  note: verification used a block explorer (Explorer trust category); for fully trustless verification, use a Bitcoin Core node.
```

A file-mismatch failure:

```
$ ots verify other.txt --proof hello-world.txt.ots
ots verify: File hash does not match the digest committed in the timestamp: expected …, got ….
$ echo $?
2
```

An anchored-but-unchecked report:

```
$ ots verify hello-world.txt
ANCHORED: hello-world.txt contains chain attestations but no block-header source for the relevant chain was configured. Re-run with --explorer, --bitcoin-rpc, or --litecoin-explorer to verify against headers.
  bitcoin block 358391
$ echo $?
2
```

A multi-chain proof verified via Litecoin (when the Bitcoin attestation is
also present but no Bitcoin provider was supplied):

```
$ ots verify file.txt --litecoin-explorer https://litecoinspace.org/api/
VERIFIED: file.txt existed at or before 2024-XX-XX HH:MM:SS UTC.
  litecoin block 2500000 time 2024-XX-XX HH:MM:SS UTC (source: litecoinspace.org, trust: Explorer)
  note: verification used a block explorer (Explorer trust category); for fully trustless verification, use a Bitcoin Core node.
```
