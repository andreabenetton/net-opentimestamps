# Versioning policy

This project uses [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html)
for both NuGet packages and the `ots` CLI's user-visible contract.

## Where the version lives

A single source of truth in `Directory.Build.props`:

```xml
<VersionPrefix>X.Y.Z</VersionPrefix>
<VersionSuffix></VersionSuffix>
```

The Cli and library projects do not override this. Pre-release builds set
`VersionSuffix` on the command line:

```
dotnet pack OpenTimestamps.sln --configuration Release -p:VersionSuffix=alpha.1
```

`AssemblyVersion` and `FileVersion` never include the suffix; `PackageVersion`
does — so a pre-release package and its release counterpart have the same
assembly identity but different NuGet identity.

## What counts as a breaking change

**Library (NuGet `OpenTimestamps`)** — major bump on any of:

- Removing or renaming a public type or member.
- Changing the type or position of a public method parameter or return.
- Adding a *required* member to a public interface.
- Tightening the documented exception contract (a method now throws something it didn't).
- Changing a `TrustCategory` value the caller observed.

Adding an optional parameter with a default to an existing public method is
a *minor* change as long as it doesn't change behaviour when omitted —
PublicApiAnalyzers will refuse the diff unless you acknowledge it in
`PublicAPI.Unshipped.txt`.

**CLI (`ots` tool)** — major bump on any of:

- Removing or renaming a subcommand or flag.
- Changing an exit code's meaning (the 0/1/2/3 contract in
  [`cli-usage.md`](cli-usage.md) is part of the public API).
- Changing the JSON shape emitted by `--json` (consumers parse it).
- Changing the human-readable stdout format in a way scripts could
  reasonably break on (treat it as API).

## Release flow

1. Move pending entries in `CHANGELOG.md`'s **Unreleased** section under a
   new `[X.Y.Z] — YYYY-MM-DD` heading.
2. Flush `src/OpenTimestamps/PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt`.
   Leave `Unshipped.txt` containing only `#nullable enable`.
3. Bump `VersionPrefix` in `Directory.Build.props`.
4. Commit on `main` with message `release: vX.Y.Z`.
5. Tag the commit: `git tag vX.Y.Z && git push origin vX.Y.Z`.
6. The release workflow (`.github/workflows/release.yml`) fires on the tag
   and produces signed NuGet packages + a GitHub release with auto-generated
   notes — see `docs/releasing.md`.

## Wire format vs library version

The OpenTimestamps wire format is governed by the upstream protocol, not by
this library. A library version bump *never* implies a wire-format change.
Conversely, if upstream defines a new attestation type or operation, that
appears here as a minor (additive) bump — the protocol's own evolution
follows a separate process.
