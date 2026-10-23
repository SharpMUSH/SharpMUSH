# Semantic wiki queries against the oracle (phase C)

Every supported query shape, for every measured reader, on the actual `pf2e-remaster` pack and on random
worlds, matches an oracle that never reads the projection. No query returned a row the reader may not see,
dropped one it may, or ordered rows differently.

## The oracle

The oracle takes each subject's imported facts, reads the live page, and asks `IWikiAccessService` whether
the reader may see it. It orders rows the way the query does: by subject (creation order) when an equality
drives the query, and by the driving integer, then subject, when a range drives it. A query on a class or
ancestry target the reader may not see, or that was deleted, must answer `Unavailable` and show nothing
that points at it.

## Regression tests (every CI run)

`IntegratedWikiMutationTests.RandomGraphs`:

- `RandomWorldsMatchTheOracleForEveryReaderAndShape` (seeds 11, 23, 47). Each world has 90 subjects and 6
  class targets. Pages land at random in the character namespace or the secret category, some are
  unpublished, and some get a page rule. Most subjects point at one hot target. Levels sit at the edges of
  every range (`int.MaxValue`, negative values, no level at all). After import, pages are deleted, moved in
  or out of the secret category, or edited, and one target is deleted. Every reader runs every shape, paged
  through at a random page size from 1 to 7: relationship, relationship with range and keyword, keyword,
  four ranges, integer equality, two keywords on one multi-valued property, an ordered list, and
  `arcane-by-level`.
- `BudgetStopIsNotEmptyOrExactCount`: under random budgets of 1–5 candidate probes and 0–3 denied skips, a
  query either asks to be refined or answers the oracle's first page exactly, with a continuation exactly
  when more rows follow. It never returns a short or empty page in place of the real one.
- `DeniedSubjectsDoNotConsumeVisiblePostingBudget`: there are 115 secret arcane subjects and 5 public ones.
  A public reader gets all five with 6 candidate probes and no denied skips, because its seek never enters
  the secret class. The secret reader, under the same budget, is asked to refine.

Facts are page metadata, so an edit of a page's text, title or categories keeps them. The random worlds
state half their subjects' facts through `SetFactsAsync` and import the rest, then edit and recategorize
pages, and the oracle expects every page to keep its facts.

## Full pack (evidence run)

`SemanticLoadMeasurement` mode `oracle` takes a world built by mode `build` and compares every shape for
the four readers. These are the 66–68 shapes, each run for every reader:

- each class target alone, and with level 1–10;
- each ancestry target;
- each tradition alone, and with rank 1–5;
- level ranges 0–3 through 24–27;
- rank 1–10;
- level = 1;
- `arcane-by-level`.

Rows are 50 per page, paged to the end. The run fails on any difference.

| Dataset | Subjects | Comparisons | Match | Unavailable (expected) | Rows compared | Differences |
|---|---:|---:|---:|---:|---:|---:|
| `pf2e-remaster` 1.1.1 at `2659a733`, as extracted (pages `8d3dcec3…`, facts `a02b5266…`) | 4,580 | 264 | 264 | 0 | 31,708 | 0 |
| The same pack through `generate.py --times 1` (99.9% sealed, one hot target with 4,580 edges) | 4,580 | 272 | 128 | 144 | 14,667 | 0 |

The results are in [`2026-10-21-semantic-wiki-oracle/`](2026-10-21-semantic-wiki-oracle/). No query asked to
be refined at these sizes.

Each comparison also asks for the exact count and the `pf2e:tradition` facet, and both must equal the
oracle's. They matched in every comparison but one. In that one, the administrator queried the sealed set's
hot target, which has 4,580 matches, more than the 4,096-subject aggregate budget. The aggregate answered
`Unknown("subjects")`, which is the only honest answer there.

To check that the comparison can fail, one run used a native query that dropped every row whose subject id
is a multiple of 97. It reported 112 mismatches out of 264 comparisons, and the run failed.

The run was made at `b679123` (#2178 on main `be8def7`) with this change, in a cloud container with 4 cores and
15 GiB, on the default sync settings. It used fixture worlds only: no real game had semantics activated.

To reproduce, extract the pack (`tools/WikiSemantics` dry-run) and optionally generate the sealed set. Then
run `SemanticLoadMeasurement` with `SHARPMUSH_SEMANTIC_LOAD` naming a `build` spec, and after it an `oracle`
spec whose `World` is that build's output directory.
