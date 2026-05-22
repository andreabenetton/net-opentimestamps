# Protocol notes

A normative summary of the OpenTimestamps `.ots` wire format and calendar HTTP
API as used by this library. The canonical reference is the Python implementation
([`opentimestamps/python-opentimestamps`](https://github.com/opentimestamps/python-opentimestamps));
if this document and the reference's emitted bytes disagree, fix this document.

## File layout

A `.ots` file is, in order:

| Field          | Length      | Notes                                                                                |
|----------------|-------------|--------------------------------------------------------------------------------------|
| Header magic   | 31 bytes    | `00 4F 70 65 6E 54 69 6D 65 73 74 61 6D 70 73 00 00 50 72 6F 6F 66 00 BF 89 E2 E8 84 E8 92 94` |
| Major version  | 1 byte      | Currently `0x01`; other values are rejected.                                         |
| `file_hash_op` | 1 byte      | Tag of a `CryptOp` (SHA-1 `02`, RIPEMD-160 `03`, SHA-256 `08`, Keccak-256 `67`).     |
| `file_digest`  | 20–32 bytes | Raw digest under `file_hash_op`; length = `DigestLength` of that op.                 |
| Timestamp tree | variable    | Recursive structure (see below). No length prefix; assert-EOF after the last leaf.   |

## Encoding primitives

### `varuint` (LEB128, unsigned)

Continuation-bit encoding with 7 payload bits per byte. The byte `0x00`
encodes `0`. The encoder emits the smallest number of bytes for the value.

### `varbytes`

`varuint(length)` followed by `length` raw bytes.

### `uint8`

A single raw byte (used only for `MAJOR_VERSION`).

### `bool`

`0xFF` = true, `0x00` = false. (Not used by the file format itself, but
defined for completeness.)

## Operations

Operations transform `msg` into a new byte string. There are unary ops (just a
tag byte on the wire) and binary ops (tag byte + `varbytes` argument).

| Tag    | Name        | Arity  | Effect                                              | Output length          |
|--------|-------------|--------|-----------------------------------------------------|------------------------|
| `0x02` | `sha1`      | unary  | `SHA-1(msg)`                                        | 20                     |
| `0x03` | `ripemd160` | unary  | `RIPEMD-160(msg)`                                   | 20                     |
| `0x08` | `sha256`    | unary  | `SHA-256(msg)`                                      | 32                     |
| `0x67` | `keccak256` | unary  | Keccak-256(msg) — **not** NIST SHA3-256             | 32                     |
| `0xF0` | `append`    | binary | `msg ‖ arg`                                         | `len(msg) + len(arg)`  |
| `0xF1` | `prepend`   | binary | `arg ‖ msg`                                         | `len(arg) + len(msg)`  |
| `0xF2` | `reverse`   | unary  | `reverse(msg)` — deprecated; new stamps must not emit | `len(msg)`           |
| `0xF3` | `hexlify`   | unary  | lowercase ASCII hex of `msg`                        | `2 * len(msg)`         |

Bounds: `MAX_RESULT_LENGTH = MAX_MSG_LENGTH = 4096` for every op except
`hexlify` which caps `MAX_MSG_LENGTH` at 2048 so its output still fits.

Binary-op arguments are mandatory and non-empty: an empty `varbytes` argument
to `OpAppend`/`OpPrepend` is rejected on read and on construction.

## Attestations

A `TimeAttestation` is the proof-tip type that anchors a commitment to an
external truth source. On the wire it is:

```
[ 8-byte type TAG ]
[ varbytes payload ]
```

`MAX_PAYLOAD_SIZE = 8192`. Unknown TAGs become `UnknownAttestation` and are
preserved verbatim, payload bytes intact, so future attestation types still
round-trip.

| Attestation                       | 8-byte TAG (hex)              | Payload                                       |
|-----------------------------------|-------------------------------|-----------------------------------------------|
| `PendingAttestation`              | `83 DF E3 0D 2E F9 0C 8E`     | `varbytes(uri_utf8)`; URI ≤ 1000 bytes, restricted ASCII alphabet |
| `BitcoinBlockHeaderAttestation`   | `05 88 96 0D 73 D7 19 01`     | `varuint(block_height)`                       |
| `LitecoinBlockHeaderAttestation`  | `06 86 9A 0D 73 D7 1B 45`     | `varuint(block_height)` — not verified by this library |
| `EthereumBlockHeaderAttestation`  | `30 FE 80 87 B5 C7 EA D7`     | `varuint(block_height)` — not verified by this library |
| `UnknownAttestation`              | any other 8 bytes             | opaque bytes, preserved                       |

Pending-attestation URIs must consist of bytes from the set
`A-Z a-z 0-9 - . _ / : `. Anything else triggers a `DeserializationException`.

## Timestamp tree

A timestamp node holds a `msg`, a set of attestations on that `msg`, and a map
of outgoing operations producing child timestamps. The wire encoding of one
node, given `N = number of attestations` and `M = number of ops`:

```
for each attestation a in sorted(attestations) except the last:
    emit 0xFF 0x00
    serialize a

if M == 0:
    emit 0x00
    serialize last attestation
else:
    if N > 0:
        emit 0xFF 0x00
        serialize last attestation
    for each (op, child) in sorted(ops) except the last:
        emit 0xFF
        serialize op
        recurse into child
    serialize last op
    recurse into last child
```

Reading mirrors the writer:

```
tag = read 1 byte
while tag == 0xFF:
    inner = read 1 byte
    handle(inner)
    tag = read 1 byte
handle(tag)
```

`handle(0x00)` reads an attestation. `handle(op_tag)` reads the op (including
its `varbytes` argument if it's a binary op), applies the op to the parent
node's `msg` to get the child's `msg`, then recurses.

### Determinism

The writer sorts attestations by `(tag_bytes, payload_bytes)` and ops by
`(tag_byte, argument_bytes)`. This is the canonical order; our serializer
matches the Python reference's output byte-for-byte on every fixture in
`tests/OpenTimestamps.Tests/fixtures/python-opentimestamps/`.

### Termination

A timestamp tree has no explicit terminator. Every path must end in at least
one attestation (a "leaf"). The outer `DetachedTimestampFile.Deserialize` asserts
EOF after the tree to guard against trailing junk.

## Calendar HTTP API

Each calendar speaks a tiny HTTP API. Both endpoints expect:

- `Accept: application/vnd.opentimestamps.v1`
- `User-Agent: <client>` (recommended)

### `POST /digest`

Submit a commitment. Body is the raw commitment bytes (≤ 64 bytes).

Successful response (`200 OK`) body is a serialized **partial** Timestamp tree
(no magic, no version, no file-hash op, no file-digest prefix — just the
recursive node format described above), with `initial_msg = submitted_digest`.
The body is capped at 10000 bytes by the reference and by this library.

### `GET /timestamp/{hex}`

Look up the (possibly upgraded) tree for an existing commitment. `{hex}` is
the lowercase hex of the commitment.

- `200 OK` → partial Timestamp body, same format as above, with
  `initial_msg = commitment`.
- `404 Not Found` → commitment is pending; the calendar has not yet anchored it.

### Default calendar endpoints

Used by the reference client for stamping aggregation:

- `https://a.pool.opentimestamps.org`
- `https://b.pool.opentimestamps.org`
- `https://a.pool.eternitywall.com`
- `https://ots.btc.catallaxy.com`

Default whitelist patterns for upgrade fetches:

- `https://*.calendar.opentimestamps.org`
- `https://*.calendar.eternitywall.com`
- `https://*.calendar.catallaxy.com`

Upgrade requests against URIs that don't match the whitelist are refused with
a typed error.

## Endianness

Bitcoin block-header `hashMerkleRoot` is stored in *internal* byte order
(little-endian) within the header serialization. Block explorers (Esplora,
Blockstream.info, mempool.space) and `bitcoin-cli getblockheader` report the
merkle root in *display* (big-endian) order. The OTS attestation commitment
matches the **internal** order, so `EsploraBlockHeaderProvider` and
`BitcoinCoreRpcBlockHeaderProvider` both reverse the explorer's hex string
before comparing.
