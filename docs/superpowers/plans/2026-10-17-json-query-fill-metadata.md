# JSON query and fill metadata plan

> **For agentic workers:** Use superpowers:executing-plans to execute this approved slice.

**Goal:** Describe json_query action arguments and json_fill pointer/value pairs accurately in editor signatures.
**Architecture:** Correct only the two registration parameter arrays. Existing analyzer and Monaco variadic expansion consume them; preserve all function bodies, arities, flags, evaluation and errors.
**Tech stack:** .NET 11 RC2, TUnit, existing Node Monaco provider harness and ContractAudit exporter.
**Spec:** `docs/superpowers/specs/2026-10-16-open-issue-consolidation-design.md`, Contract audit; root-approved #1797 inventory/prioritization in this chat.

## Constraints and review focus

- Only json_query and json_fill declarations change; json_mod null-patch behavior stays a source-only candidate.
- Query action is optional; get/exists accept segments, extract accepts one combined path. Generic `arg...` describes the existing overloads.
- Fill repeats pointer/value pairs using the existing paired declaration grammar, never an unpaired variadic value.
- Census evidence remains partial: no behavioral completion, live browser/LSP or compatibility claim.
- Use disk scratch `/temp/codex/json-contract-metadata`, pinned RC2 SDK and shared `flock --close /temp/codex/sharpmush-build.lock`; tests use maximum parallelism 8.

## Execution

- [x] Add literal expected analyzer, exported definition and registered Monaco regressions; verify failures with old labels. Characterize existing JSON parser tests before corrections.
- [x] Correct the two arrays; verify those regressions and JSON parser family, without changing behavior.
- [ ] Extend exporter evidence/facets for these entries, document observed contracts and un-reproduced null-patch candidate; regenerate the census twice and compare reproducibility.
- [ ] Format twice, build normally and run appropriate focused checks; stop for root review before commit, push or PR.

Source/test verification is complete. Root review gates the first source commit; final census exports then use that committed tracked source and are committed separately. Scratch candidate exports provide review evidence without claiming a dirty working tree is an immutable revision.
