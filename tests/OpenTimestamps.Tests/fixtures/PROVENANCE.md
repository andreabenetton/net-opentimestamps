# Fixture provenance

`.ots` files committed here are copied verbatim from the upstream reference
clients. Each row records the upstream repository, the path inside that repo,
and the commit SHA from which the file was pulled. When a fixture is updated,
add a new row and link the old row's SHA to the historical record rather than
overwriting.

## `python-opentimestamps/` directory

Files in `tests/OpenTimestamps.Tests/fixtures/python-opentimestamps/` come from
the [`opentimestamps-client`](https://github.com/opentimestamps/opentimestamps-client)
repository (the `examples/` directory inside the upstream CLI). They are
named `python-opentimestamps` because the reference flow is rooted in that
ecosystem and these files are also exercised by the Python reference tests.

| Fixture                                  | Upstream path                                 | SHA                                        |
|------------------------------------------|-----------------------------------------------|--------------------------------------------|
| `hello-world.txt`                        | `examples/hello-world.txt`                    | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `hello-world.txt.ots`                    | `examples/hello-world.txt.ots`                | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `two-calendars.txt`                      | `examples/two-calendars.txt`                  | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `two-calendars.txt.ots`                  | `examples/two-calendars.txt.ots`              | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `incomplete.txt`                         | `examples/incomplete.txt`                     | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `incomplete.txt.ots`                     | `examples/incomplete.txt.ots`                 | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `known-and-unknown-notary.txt`           | `examples/known-and-unknown-notary.txt`       | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `known-and-unknown-notary.txt.ots`       | `examples/known-and-unknown-notary.txt.ots`   | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `unknown-notary.txt`                     | `examples/unknown-notary.txt`                 | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `unknown-notary.txt.ots`                 | `examples/unknown-notary.txt.ots`             | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `different-blockchains.txt`              | `examples/different-blockchains.txt`          | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |
| `different-blockchains.txt.ots`          | `examples/different-blockchains.txt.ots`      | `cd71c7609421bed2a07b9642a3c02a58c9fd2cdf` |

## `java-opentimestamps/` directory

Files in `tests/OpenTimestamps.Tests/fixtures/java-opentimestamps/` come from
the [`java-opentimestamps`](https://github.com/opentimestamps/java-opentimestamps)
reference implementation's `examples/` directory. They cover artifacts the
Python set does not (multi-file merkle batches, an Ethereum-attestation
example).

| Fixture                       | Upstream path                          | SHA                                        |
|-------------------------------|----------------------------------------|--------------------------------------------|
| `merkle1.txt`                 | `examples/merkle1.txt`                 | `81863915ea218354e9c84cae0c522b85d1dc9f91` |
| `merkle1.txt.ots`             | `examples/merkle1.txt.ots`             | `81863915ea218354e9c84cae0c522b85d1dc9f91` |
| `merkle2.txt`                 | `examples/merkle2.txt`                 | `81863915ea218354e9c84cae0c522b85d1dc9f91` |
| `merkle2.txt.ots`             | `examples/merkle2.txt.ots`             | `81863915ea218354e9c84cae0c522b85d1dc9f91` |
| `merkle3.txt`                 | `examples/merkle3.txt`                 | `81863915ea218354e9c84cae0c522b85d1dc9f91` |
| `merkle3.txt.ots`             | `examples/merkle3.txt.ots`             | `81863915ea218354e9c84cae0c522b85d1dc9f91` |
| `hello-world.txt`             | `examples/hello-world.txt`             | `81863915ea218354e9c84cae0c522b85d1dc9f91` |
| `hello-world.txt.eth.ots`     | `examples/hello-world.txt.eth.ots`     | `81863915ea218354e9c84cae0c522b85d1dc9f91` |

## `javascript-opentimestamps/` directory

Files in `tests/OpenTimestamps.Tests/fixtures/javascript-opentimestamps/` come
from the [`javascript-opentimestamps`](https://github.com/opentimestamps/javascript-opentimestamps)
reference implementation's `examples/` directory. Selected to cover file-hash
ops the other sets don't (SHA-1, RIPEMD-160) plus a JS-produced sample.

| Fixture                          | Upstream path                         | SHA                                        |
|----------------------------------|---------------------------------------|--------------------------------------------|
| `osdsp.txt`                      | `examples/osdsp.txt`                  | `c07ba8be0dae4e8721a84f32820abf7b2547a6ce` |
| `osdsp.txt.ots`                  | `examples/osdsp.txt.ots`              | `c07ba8be0dae4e8721a84f32820abf7b2547a6ce` |
| `sha1/a`                         | `examples/sha1/a`                     | `c07ba8be0dae4e8721a84f32820abf7b2547a6ce` |
| `sha1/b`                         | `examples/sha1/b`                     | `c07ba8be0dae4e8721a84f32820abf7b2547a6ce` |
| `sha1/a_or_b.ots`                | `examples/sha1/a_or_b.ots`            | `c07ba8be0dae4e8721a84f32820abf7b2547a6ce` |
| `ripemd160/README.md`            | `examples/ripemd160/README.md`        | `c07ba8be0dae4e8721a84f32820abf7b2547a6ce` |
| `ripemd160/README.md.ots`        | `examples/ripemd160/README.md.ots`    | `c07ba8be0dae4e8721a84f32820abf7b2547a6ce` |
