# Samples

Small, runnable programs that demonstrate `OpenTimestamps` API usage.

## `StampVerifyDemo`

End-to-end: stamp a file → save → upgrade → verify.

```
dotnet run --project samples/StampVerifyDemo -- path/to/your/file
```

Produces `path/to/your/file.ots` next to the input and verifies it against
Bitcoin via Esplora (`blockstream.info`). Note the trust category in the
output is `Explorer` — you're trusting Blockstream to report the correct
merkle root. For trustless verification, swap the
`EsploraBlockHeaderProvider` for a `BitcoinCoreRpcBlockHeaderProvider`
pointing at a node you control.

A freshly stamped file will report `Incomplete` from the verifier because
the calendars need ~1-3 hours to anchor it on-chain. Re-run after an hour or
two and the same `.ots` file (after `UpgradeAsync` merges in the calendar's
new attestation) will report `Verified`.

This sample is intentionally outside `OpenTimestamps.sln` so `dotnet build`
of the main solution doesn't drag it in — `dotnet run --project` it
directly.
