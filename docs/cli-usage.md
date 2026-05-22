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

## `ots stamp <file> [--calendar URL]... [--quorum N] [--output PATH]`

Hash `<file>` with SHA-256, append a fresh 16-byte privacy nonce, hash again,
submit the resulting commitment to each calendar, and write the merged proof
to `<file>.ots` (or `--output PATH`).

| Option                | Default                                | Notes                                                              |
|-----------------------|----------------------------------------|--------------------------------------------------------------------|
| `--calendar URL`      | the four default aggregators            | May repeat. Each URL is contacted in parallel.                     |
| `--quorum N`          | `2`                                     | The minimum number of calendars that must accept the stamp.        |
| `--output PATH`       | `<file>.ots`                            | Refuses to overwrite an existing file.                             |

`ots stamp` produces an `INCOMPLETE` proof — it contains pending attestations
from each calendar. Run `ots upgrade <proof.ots>` later (typically a few
hours after stamping) to merge in the Bitcoin block-header attestation.

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

## `ots verify <file> [--proof PATH] [--explorer URL | --bitcoin-rpc URL [--rpc-user U --rpc-password P]]`

Hash `<file>`, compare it to the digest committed in the proof, then walk the
tree and check every Bitcoin attestation's commitment against the merkle
root of the named block.

| Option                                 | Default                | Notes                                                                                            |
|----------------------------------------|------------------------|--------------------------------------------------------------------------------------------------|
| `--proof PATH`                         | `<file>.ots`            | Path to the proof.                                                                              |
| `--explorer URL`                       | (off)                   | Use an Esplora-compatible block explorer for headers. **Trust category: `Explorer`** (not trustless). |
| `--bitcoin-rpc URL`                    | (off)                   | Use a Bitcoin Core JSON-RPC endpoint. **Trust category: `LocalNode`** (trustless given the node).     |
| `--rpc-user U` / `--rpc-password P`    | (none)                  | Basic-auth credentials for the RPC endpoint.                                                    |

When no header source is supplied, `ots verify` reports `ANCHORED` (proof
contains Bitcoin attestations but they were not checked) or `INCOMPLETE`
(proof contains only pending attestations) and exits with code `2`.

When `--explorer` is used, the output includes a note that verification used
a block explorer and is therefore not trustless. Use `--bitcoin-rpc` against
a node you control for trustless verification.

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
ANCHORED: hello-world.txt contains Bitcoin attestations but no block-header source was configured. Re-run with --explorer or --bitcoin-rpc to verify against headers.
  bitcoin block 358391
$ echo $?
2
```
