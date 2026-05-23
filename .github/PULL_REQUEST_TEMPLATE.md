<!--
Thanks for the PR. Please skim the checklist before requesting review.
Anything you don't apply, leave unchecked and add a one-line note explaining
why — that's faster than a back-and-forth on review.
-->

## What changed

<!-- One or two sentences. -->

## Why

<!-- Link an issue or describe the motivation. -->

## Completion checklist

Drawn from CLAUDE.md's "Completion checklist" — see that file for the
rationale on each item.

- [ ] Wire format respected (parse → serialize is byte-identical on all fixtures)
- [ ] Unknown attestations still round-trip verbatim
- [ ] Deterministic serialization (sorted ops + sorted attestations) preserved
- [ ] Crypto primitives use only the approved providers (BCL SHA-256/SHA-1, BC RIPEMD-160, BC Keccak-256)
- [ ] Trust category of each `BlockHeaderProvider` use is disclosed
- [ ] Pending vs. anchored vs. verified status surfaced correctly
- [ ] Parsing and verification kept separate in the public API
- [ ] Tests added / updated
- [ ] CLI exercised against a real fixture (if CLI touched)
- [ ] Public API change reflected in `CHANGELOG.md` and `PublicAPI.Unshipped.txt`
- [ ] Fixture changes reflected in `PROVENANCE.md`
- [ ] Relevant `docs/*.md` updated when behaviour / format / trust model changed

## Notes for the reviewer

<!--
Anything you'd flag to a reviewer that isn't obvious from the diff:
risky bits, alternatives you considered, follow-ups you'd defer.
-->
