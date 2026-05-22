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
