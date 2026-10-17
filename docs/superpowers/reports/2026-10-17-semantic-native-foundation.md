# Native semantic wiki foundation: measured prototype results

This is disposable-fixture evidence for the [B1 plan](../plans/2026-10-17-semantic-wiki-phase-b1.md), tracked in [#2031](https://github.com/SharpMUSH/SharpMUSH/issues/2031). B1 acceptance remains open: a real non-NotFound native cursor error during iteration has not been reproduced. Actual native seek and read-only mutation errors, exhaustive return-status classification, and source review do not substitute for that runtime gate.

The optional bounded-read capability preserves existing transaction/accessor signatures. It checks native lengths before copying, uses actual persisted table flags, and carries caller-owned probe, byte and working-memory budgets. The tool-owned aggregate plans its complete delta before the first write and submits one actual provider writer job per page. Typed rejection cannot leave partial registries, indexes, catalog state or mutation receipts. There is no production semantic registration, migration, endpoint or package activation.

## Verified behavior

- Native aggregate tests: 23 passed, including all-table index symmetry, metadata-only and zero-fact state changes, catalog capacity/reclamation, fresh class IDs on reactivation, absent-target reservation, deletion without incoming-degree scans, durable receipts and reopen.
- Recovery tests: 9 passed, including real aggregate MapFull, composed rollback, grouped child isolation/parent completion, cancellation phases, durable retry, separate-process persisted duplicate flags, and owned process termination before/after actual commit. Reopened complete-table checksums matched the respective old/new state. These are process-recovery tests, not power-loss tests.
- A deterministic regression reproduced and fixed a harness race where normal stderr EOF could beat stdout checkpoint parsing. Both child pumps and submitted writer jobs are observed before fixture disposal.
- Final focused regressions: 109 passed, zero skips across cursor failures, bounded reads, existing store/streaming/paging behavior, native keys, preparation and aggregate mutations. The final normal build had zero warnings/errors. These checks are additional to the 9 recovery cases and explicit corpus run.
- Explicit corpus test: 1 passed, zero skips. All 4,711 pages and 10,605 facts from verified `pf2e-remaster` 1.1.1 commit `2659a733a4519b76e64a727be2687794afc04f6b` went through actual full-sync native commits. Two fresh reopens preserved every page, fact, provenance field, summary and receipt, authored Alchemist/Dwarf goldens, and complete registry/index/catalog/partition symmetry. Source files were reverified unchanged; only the owned disposable world was removed.

## One measured full-corpus run

Linux x86_64, Intel Core Ultra 7 265F, NVMe/Btrfs, pinned .NET 11 SDK, Debug build, 256 MiB map ceiling, full-sync commits. This is one uncontrolled workstation run, not a load-test baseline or timing guarantee.

| Observation | Result |
| --- | ---: |
| Full import | 41.158 s |
| Registry identities / rows | 4,977 / 9,954 |
| Active classes / catalog bytes | 252 / 56,126 |
| Forward rows / posting rows | 10,605 / 10,605 |
| Logical key/value bytes | 12,101,410 |
| Native directory file lengths / allocated disk | 16,580,736 / 16,568,320 bytes |
| Maximum actual complete write delta | 23,679 bytes |
| Maximum writer probes / inspected bytes | 23 / 1,450 |
| Maximum inspected batch rows / key-value bytes | 128 / 131,063 |
| Commit latency p50 / p95 / max | 6.199 / 18.135 / 145.971 ms |
| Synchronous writer delegate p50 / p95 / max | 0.065 / 0.132 / 7.922 ms |
| Synchronous read call p50 / p95 / max | 0.005 / 0.012 / 5.510 ms |

Both reopened complete-table digests were `70bcef433029dc4af6afe78a1aa7e4036f50dc5eb66f3967fa7e482f2e7fa79b`. Timing maxima exceed 5 ms; no duration guarantee is established. Whole-commit latency includes work beyond the synchronous writer delegate.

Working-memory observations are sampled accounting checkpoints, not exact peaks: writer maximum 168,180 bytes; read maximum 200,412 bytes. The whole offline oracle separately recorded 1,643,372,172 bytes of conservative cumulative reservation, 1,032,463,624 cumulative managed allocated bytes, and a 316,559,360-byte process working-set checkpoint. None is a production query memory measurement. The 4 MiB per-operation reservation limit remains unchanged.

## Reproduction and remaining gates

Use the pinned SDK and disk-backed TMPDIR/TMP/TEMP. Build `SharpMUSH.Tests` normally, then run the `NativeSemanticCommitTests` and `NativeSemanticRecoveryTests` classes. The explicit full-corpus method is `/*/*/NativeSemanticCorpusTests/VerifiedFullCorpusRoundTripsThroughNativeStore`; set `SEMANTIC_PF2E_SOURCE` to the immutable verified package directory. Missing source fails that explicit acceptance run. It is not silently converted to a passing test. The harness uses the existing shared build lock described in the plan.

Recovery/serialization evidence does not establish native query execution, trusted authorization, real installer integration, legacy mutation coverage, live grant/requirements/locale fences, command interference, generation rebuild or the load/crash matrix. Those remain B2/C/D work. Keep #2031 open; these results authorize no production release or deployment.
