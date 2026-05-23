# Security policy

This document covers how to report security issues in `net-opentimestamps`
and what versions we support with security fixes.

## Reporting a vulnerability

Please report security issues **privately** rather than opening a public
GitHub issue.

Preferred channel: GitHub's [private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
on this repository. From the repo, go to **Security → Advisories →
Report a vulnerability**.

If for any reason you cannot use private reporting, open an issue titled
"security: contact request" (no details) and we will respond with a
private contact route.

When reporting, please include:

- A description of the vulnerability, including its impact.
- Steps to reproduce, or a proof-of-concept input (a malformed `.ots`
  fixture is often the cleanest form).
- The affected version(s) and any commit SHAs you've identified.
- Whether you intend to disclose publicly, and if so, your target date.

We aim to acknowledge reports within **7 days** and triage them within
**14 days**. These are best-effort timelines on a single-maintainer
project; if the issue is critical we will move faster.

## Supported versions

`1.0.0` is cut in-tree (the source has `VersionPrefix=1.0.0` and the API
surface is frozen under `PublicApiAnalyzers`) but no version has been
tagged or published to NuGet yet — there is currently nothing for an
external consumer to depend on. Until the first published release, the
only supported version is `main` itself.

| Version | Security fixes |
|---------|----------------|
| `main` (unreleased) | ✅ |
| `1.x.y` once published | ✅ — to be filled in once tags exist |
| any pre-publish snapshot taken from `main` | ❌ — please update to `main` |

This table will be revised on each tagged release to track which lines
receive fixes. Until then, every security report is assessed against
`main`.

## Scope

In scope:

- Parser issues: malformed `.ots` inputs causing crashes, unbounded
  allocation, infinite loops, or escaping non-typed exceptions.
- Verification issues: any path where a proof reports `Verified` when it
  should not, or fails to detect a digest mismatch / chain mismatch /
  attestation-payload mismatch.
- Calendar / HTTP client issues: bypass of the URI whitelist, response
  size cap bypass, response-body confusion.
- Cryptographic issues: incorrect SHA-256 / SHA-1 / RIPEMD-160 / Keccak-256
  use, or substituting NIST SHA3-256 for Ethereum Keccak-256.

Out of scope (please don't file these as security issues):

- "Verified" trust category being weaker than the caller expected when
  the caller chose an Explorer provider — that's documented in
  `docs/verification-model.md` and is the caller's responsibility.
- Bugs in third-party calendar servers or block explorers.
- Bugs in dependencies (please report those to the upstream project;
  if a dependency advisory affects this project, we will follow up).

## What you can expect after reporting

- A reply within 7 days acknowledging receipt.
- A triage note within 14 days indicating whether we've reproduced the
  issue and what severity we've assigned.
- A fix proposal you can review privately before public disclosure.
- Public credit in the release notes for the fix, unless you ask
  otherwise.
