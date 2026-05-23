# Releasing

End-to-end checklist for cutting a new release.

## One-time setup

Repository secrets (Settings → Secrets and variables → Actions):

- `NUGET_API_KEY` — API key from nuget.org. Required for `publish-nuget`
  job. Without it, `workflow_dispatch` with `publish_nuget=true` will fail
  fast with a clear error; tag pushes still build packages but fail the
  publish step.

Repository settings:

- Tag protection on `v*` (Settings → Code and automation → Tags) so only
  release managers can push release tags.

## Per-release flow

1. **Prepare changes on `main`.**
   - Move any **Unreleased** items in `CHANGELOG.md` under a new
     `[X.Y.Z] — YYYY-MM-DD` heading.
   - Flush `src/OpenTimestamps/PublicAPI.Unshipped.txt` into
     `PublicAPI.Shipped.txt`. Leave `Unshipped.txt` with only
     `#nullable enable`.
   - Bump `VersionPrefix` in `Directory.Build.props`. Drop any
     `VersionSuffix` for a stable release; set e.g. `alpha.1` for
     pre-release.

2. **Final pre-flight on `main` before tagging.**
   ```
   dotnet build OpenTimestamps.sln -c Release
   dotnet test OpenTimestamps.sln -c Release
   ```
   Optionally exercise the Python interop suite if `ots` is on PATH:
   ```
   OTS_PYTHON_REF=1 dotnet test tests/OpenTimestamps.IntegrationTests
   ```

3. **Commit the release-prep changes.** Message: `release: vX.Y.Z`.

4. **Tag and push.**
   ```
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

5. **The release workflow fires automatically:**
   - `build-pack`: produces `.nupkg` + `.snupkg` artifacts for both
     `OpenTimestamps` and `OpenTimestamps.Cli`.
   - `sbom`: produces a CycloneDX JSON SBOM as an artifact.
   - `publish-nuget`: pushes the packages to nuget.org (requires the
     `NUGET_API_KEY` secret).
   - `github-release`: creates a GitHub release named `vX.Y.Z`, attaches
     the `.nupkg`, `.snupkg`, and SBOM, and auto-generates release notes
     from commits since the previous tag.

6. **Post-release verification.**
   - Check the [package page on nuget.org](https://www.nuget.org/packages/OpenTimestamps).
   - Try `dotnet add package OpenTimestamps --version X.Y.Z` in a scratch
     project.
   - Try `dotnet tool install --global OpenTimestamps.Cli --version X.Y.Z`
     and run `ots --version`.

## Dry runs without publishing

Trigger the workflow manually from the Actions UI with
`publish_nuget=false`. This runs `build-pack` and `sbom` and skips the
push to nuget.org and the GitHub release. Artifacts are available on the
workflow run for download.

## Hotfix patch release

For a `Z`-only bump on the latest line:

1. Make the fix on `main`.
2. Bump `VersionPrefix` to `X.Y.(Z+1)`.
3. Tag `vX.Y.(Z+1)`. The workflow does the rest.

For a hotfix on an older line (post-1.0), branch from the relevant tag,
apply the fix, bump version, tag.

## Yanking a release

If a published version turns out to be broken, **deprecate, don't unlist**:
NuGet allows marking a package version as deprecated with a reason and a
pointer to the replacement. Yanking via "unlist" hides the package but
leaves dependent builds working — confusing.

```
dotnet nuget delete OpenTimestamps X.Y.Z --source https://api.nuget.org/v3/index.json --api-key $NUGET_API_KEY
```

(`delete` actually unlists. Use the package owner UI on nuget.org for
deprecation with a reason.)
