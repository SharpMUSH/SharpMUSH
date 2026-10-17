# Semantic wiki Phase A validation

Status: offline Phase A integration verified. This is an offline correctness baseline for the approved prototype, not a released game feature or a performance result.

The user approved the architecture and recommended choices on 2026-10-17. [Implementation issue #2031](https://github.com/SharpMUSH/SharpMUSH/issues/2031) tracks the A–D prototype following the completed PF2e organization review (#1970). [Architecture PR #2016](https://github.com/SharpMUSH/SharpMUSH/pull/2016) merged at `59458edad21dc79706ee9a19d4865d583b4c8eaf`. [The implementation plan](../superpowers/plans/2026-10-17-semantic-wiki-phase-a.md) separates this milestone from the indexed, recovery and shared-game-load gates.

## Reproducible source and extraction

The source is `SharpMUSH/SharpMUSH.Packages`, commit `2659a733a4519b76e64a727be2687794afc04f6b`, `pf2e-remaster` 1.1.1. All 4,714 declared/required files match both their pinned Git blob hashes and SHA256 hashes: 4,560,161 bytes, including the manifest, 4,711 page files and two package notices. Extra undeclared files are not authenticated or read. The embedded lock verifies file content, not a local checkout's Git history or remote.

The manifest SHA256 is `3c87cc186fc42b9c78e696716d6ed337cef07752beb263a32b5af41cfea22ee6`. Pages comprise 4,580 Main articles, 130 Category hubs and one Help page. Upstream files remain unchanged. The CLI keeps the three attribution texts and full source citations; it infers no prerequisites, rules from prose, or Remaster/Legacy equivalences.

| Property | Subjects | Values |
| --- | ---: | ---: |
| `pf2e:level` | 3,396 | 3,396 |
| `pf2e:rank` | 659 | 659 |
| `pf2e:tradition` | 408 | 894 |
| `pf2e:class` | 904 | 977 |
| `pf2e:ancestry` | 99 | 99 |
| `pf2e:source-book` | 4,580 | 4,580 |
| Total facts | — | 10,605 |

Seven versatile heritages remain explicit ancestry unknowns. Absence of supported evidence is counted separately from zero/false and does not assert that a property is inapplicable. Class and tradition retain multiple values. Spell-family rank does not also become level; class/ancestry hubs do not inherit numeric facts from their schedules.

Two complete CLI runs produced byte-identical facts, page maps, coverage report and three attribution files. A fresh integration run matched every output byte, and all three attribution files matched upstream bytes. The full-corpus oracle acceptance compared all 47 Alchemist feat keys and levels with the independently authored category hub, and checked the five Dwarf heritage keys. Both explicit corpus tests passed again in integration (one test each, no failures/skips). A separate scan verified the exact 4,714-file source set and every SHA256 remained unchanged.

| Artifact | Bytes | SHA256 |
| --- | ---: | --- |
| `facts.jsonl` | 5,576,124 | `a02b5266b77b05fabb40d3abfd4469235ad72b2fa5d849af45a23a690769ffaa` |
| `pages.jsonl` | 1,061,203 | `8d3dcec3f4475e39fdcd2acbabad6a242f976b0e815a1017b0135c07851417b7` |
| `report.json` | 3,420 | `34e40cb353d770c8d600e747a1d18a89888ed47e1e1628d9ab622de5304549fb` |

These export sizes are not native index size or memory measurements.

Fixture IDs are deterministic initial offline identities. They are not a production ID allocation/update algorithm: a real import must retain mappings, handle explicit rename migrations, and allocate a new incarnation after deletion.

## Correctness and permissions

The reference evaluator scans canonical facts independently of future indexes. Existing `WikiVisibility.Admits` handles both subject and PageRef target, and both must belong to the requested collection. The tests use real `WikiAccessService` grant resolution for admin/global-read/draft and namespace/category/page requirements. Hidden and missing supplied targets produce the same outcome. Hidden relations do not contribute to predicates, projected values, exact page counts, enum facets or continuation claims. Positive visible matches and nonempty facets make the hidden-only mutation comparison meaningful.

Queries support conjunctions of equality and signed integer ranges, at most four predicates and six property columns, 25 default/100 maximum rows, stable ID order or single-valued integer order with ID ties and missing values last. Multivalue columns are allowed; multivalue ordering is rejected. Facets are registered keyword enums with at most 16 members. Search and unsupported shapes are rejected. Counts are optional and deduplicate subjects.

This offline oracle intentionally has full scans and offset enumeration. It has no trusted live authorization stamp, authenticated cursor, runtime admission/work budget, response fence or latency guarantee. None of its timings establish indexed-query performance. Planned version records are declarations of required dimensions, not manufactured production epochs.

Canonical validation tests cover qualified property keys, typed failure results for malformed records, normalized enum/keyword duplicates, deterministic provenance selection, signed extrema, explicit aliases, and inclusive/over-limit fact quotas. Parser and CLI safeguards cover malformed or altered input, immutable verified extraction data, numeric overflow, existing destinations and output/source symlink boundaries.

## Storage coverage and validation evidence

[The mutation matrix](../design/wiki-semantic-mutation-coverage.md) maps current portal, in-game, startup, source restore, requirements, package adoption/apply/undo/removal and ownership paths. Its reflection test detects unclassified store API changes; it does not prove index atomicity. Native probes establish current requirement/ownership persistence and identity behavior. Existing body revision cannot certify metadata or ACL freshness. Translation summaries also need a separate version across deletion/recreation. Package compensation is a sequence of real writes; package revision rollback currently leaves wiki pages unchanged.

| Verification | Result |
| --- | --- |
| Existing WikiAccessService baseline | 10 passed, no failures/skips |
| Existing native wiki baseline | 7 passed, no failures/skips |
| Canonical contract tests | 12 passed, no failures/skips |
| Mutation coverage contribution | 6 passed, no failures/skips |
| Reference evaluator tests | 11 passed, no failures/skips |
| Semantic permission tests | 6 passed, no failures/skips |
| Dataset safeguards | 6 passed, no failures/skips |
| Authored extraction samples | 2 passed, no failures/skips |
| Explicit corpus oracle and frozen declarations | Each passed 1/1, no failures/skips; fresh integration reruns also passed |
| Final integration formatting-gated build | 0 warnings/errors, 1m04.80s |
| Complete engine suite | 18,519 passed, 0 failed, 50 skipped (18,569 total), 6m57.037s |
| Tracked-repository C# whitespace | Passed for all 3,859 tracked C# files |
| Independent final review | Whole-branch Phase A spec/quality PASS; separate fixture and grant-cache fix reviews PASS |

Worker reports retain behavioral RED/GREEN evidence and normal formatting-gated builds. Independent whole-branch and scoped correction reviews passed without findings. Extraction review rechecked every source byte, coverage, deterministic output and attribution preservation. The final complete engine run and tracked-repository formatting passed.

Integration also corrected three existing test fixtures; production behavior is unchanged:

- **Exporter deadline:** A delayed real child showed the 500 ms fixture deadline could expire before the intended byte-budget failure. Ordinary failure cases now allow initialization time; the dedicated timeout remains 500 ms. All 21 focused cases passed with strict error and source/publication assertions. The initial suite discarded exception details, so its exact original exception is not claimed.
- **Preview isolation:** A controlled maintenance job changed only expanded server data before any upload. The dedicated fixture now awaits maintenance shutdown and drains startup work before its baseline. Its complete live-file hash remains strict and ordered; a real write control proves writes are still detected. Both cases passed. The original run did not capture its exact writer.
- **Wiki grant setup:** The fixture assigned approval directly without production’s grant invalidation. A real mediator cache prewarm followed by the old setup and an actual command reproduced `needs wiki.create`. The setup now sends production’s per-object invalidation. All 71 wiki command cases passed, including persisted-page/output checks and the four prior failures. Their original outputs were not retained, so precise original denials are not asserted retrospectively.

## Next gated milestone

[The reviewed B1 implementation plan](../superpowers/plans/2026-10-17-semantic-wiki-phase-b1.md) narrows the next slice to native storage evidence; B1 is only part of Phase B.

Phase B needs an atomic page/fact/summary/index/version commit, immutable target identities, a bounded visibility-class catalog and bounded direct inverse reads. It must prove real package keep-mine/take-theirs/removal/compensation behavior at the new aggregate boundary and preserve existing authorization policy. Portal grants currently aggregate account/character authority separately from acting-character draft attribution; the prototype must not silently change that contract.

Durable grant-policy, requirements and collection versions plus live session authority must fence the response. In-memory cache tag counters, source body revisions and provider reopen counters are insufficient. The final coherent metadata check must include every observed source, target and translation dependency, including excluded targets and lookahead.

Native store prerequisites include byte-bounded reads before copying, explicit native cursor error handling, signed integer encoding, cancellation/commit-status semantics, and short reader lifetimes. A disposable native environment proves atomic/reopen behavior; only the actual disposable game host's shared writer can establish interference. No production world, host cache dropping, service rollout or deployment is part of this work.

Later Phase C adds bounded indexed lists/aggregates and localized projections; Phase D covers crash/rebuild recovery and fixed-load interference. Numeric limits and latency thresholds remain hypotheses until measured on recorded hardware. Ordinary public game commands, offered load, sync policy and output completion must stay comparable to baseline. Failure of privacy, atomicity, capacity or game-latency gates prevents release; Phase A correctness results do not waive them.
