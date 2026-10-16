# Stored markup safety validation

Approved design: [open issue consolidation](../specs/2026-10-16-open-issue-consolidation-design.md).
Implementation plan: [stored markup safety](2026-10-16-stored-markup-safety.md).
Compatibility: [reader-first deployment and rollback](../../guides/stored-markup-compatibility.md).

## Acceptance evidence

| Contract | Executable evidence |
| --- | --- |
| Existing dependency | `MarkupStringVersion` already 2.21.0; no dependency bump, wire upgrade or automatic rewrite |
| Original serialization/provenance | `StoredMarkupSafetyTests.FutureWireBytesSurviveAttributeCopyAndRestart` reads future-wire display, preserves exact serialization through store copy and world reopen |
| Opaque/damaged inspection | Unknown-layer roundtrip; truncated run; throwing codec retains raw stored copy; codec-write counter proves ordinary display reads defer rewrite inspection |
| Copy/move/clone | `StoredMarkupCommandSafetyTests.CopyMoveAndClonePreserveOriginalSerialization` runs actual commands, checks destination raw bytes and move source removal |
| Atomic wildcard edit | `WildcardEditRefusesAllMatchesBeforeChangingAnyAttribute` checks every matched attribute remains unchanged and explicit replacement remains allowed |
| Snapshot take/restore | `ObjectSnapshotTests.FutureWireSnapshotCaptureAndRestorePreserveOriginalBytes` variants cover Unsupported and structurally valid Damaged payloads, including original capture and restored serialization |
| Per-record isolation | `MalformedAttributeDoesNotAbortItsNeighbours` variants corrupt attr.val or attr.meta, reopen the world, confirm exact reads retain valid access metadata (or fail closed when metadata itself is corrupt), while surrounding display enumeration survives |
| Startup | `MalformedStartupIsSkippedAndFollowingStartupStillRuns` exercises the same shared startup attribute primitive as boot, skips a malformed STARTUP and executes the following object's command list |
| Feed accounting | `FeedStoreTests.FutureWireDisplayDoesNotChangeFeedByteAccounting` checks future-wire display does not change recorded byte counts and deletion subtracts the original recorded quantities |
| Scene edit/history | `FutureWirePoseEditIsRefusedWithoutTruncatingHistory` and `MalformedSerializedPoseDoesNotSaveFallbackBackIntoHistory` preserve current raw content and edit history before refusal |
| Semaphore admission/release | Numeric future-wire admission/notify/drain refuse without counter mutation; timeout refuses and can retry after explicit compatible replacement |
| Uncertain semaphore repair | `NotifyRepairsPartialCustomCounterInitialization(0, markup)` commits a counter, replaces it with future-wire numeric content, loses the acknowledgement, then verifies repair refuses and retains the original incompatible bytes |
| Compatibility policy | Reader-first rollout, minimum-reader stamp required only when a future writer introduces a new format, backup-based rollback documented |

The malformed-row regression failed before the fix with `JsonReaderException` escaping `ScanAttributesCoreAsync`. The scene future-wire regression failed with the original payload replaced by serialized plain replacement text. Semaphore admission/notify/drain regressions failed by changing the future-wire payload to plain counters or deleting it. The new startup regression exposed and fixed the separate inherited-reader boundary.

## Scope and integration

The attribute aggregate owns original serialization. Existing Mediator query shapes carry it, and the existing invalidating SetAttributeCommand selects the raw aggregate-store write when needed. Permission and value gates remain in AttributeWriter; copies that would normalize stored text are refused. Malformed records are excluded with per-record diagnostics; display-only lazy/index fallbacks never become stored-value copies.

Normal eager display still performs basic JSON structural validation and decoding; expensive rewrite inspection (including codec writes) is lazy and tied to the registry captured at read time. Existing attribute read-cost tests protect lazy reads and byte accounting.

The Scene plugin's `EditPoseAsync` return contract expands from `Found<ScenePose>` to `FoundResult<ScenePose>` to carry a specific refusal. Command/function consumers and the test stub handle the error case. External plugin callers must rebuild and handle Error. Missing-pose meaning is unchanged. Legacy bare-text submissions, including braces, remain supported.

Semaphore guards overlap the later queue lane: preserve guards in `TaskScheduler.AdjustHaltSemaphoreCountCore`, `SettleTimeout`, admission and admission repair, plus `SemaphoreCommandAccounting` initial/reconciliation/created-identity validation. They do not alter successful compatible-counter behavior.

## Validation environment and limits

Pinned SDK: 11.0.100-rc.2.26504.105. Builds cap MSBuild concurrency at two and disable node reuse. Tests use the real LMDB provider and, for command/server fixtures, Podman through `/run/user/1000/podman/podman.sock`. Runtime TMPDIR/TMP/TEMP use `/temp/codex/markup` to stay on disk and below Linux Unix-socket path limits.

Each test class/prefix uses a separate `--treenode-filter`; pipe-combined filters are not supported by the current runner. No production deploy, new wire writer, bulk world migration or minimum-reader stamp is performed. Remote CI/review remains the PR controller's responsibility.

## Verified commands and results

- `dotnet build SharpMUSH.Tests --no-restore -m:2 -nr:false`: passed, zero warnings/errors, normal formatting verification enabled.
- `dotnet build SharpMUSH.Tests.ScenePlugin --no-restore -m:2 -nr:false`: passed, zero warnings/errors.
- Two whitespace-format passes converged; `git diff --check` passed.
- `/*/*/StoredMarkup*/*`: 14 passed.
- `/*/*/ObjectSnapshotTests/*`: 29 passed.
- `/*/*/AttributeReadCostTests/*`: 18 passed.
- `/*/*/FeedStoreTests/*`: 14 passed.
- `/*/*/SemaphoreCommandTests/*`: 46 passed, one pre-existing timing-sensitive `DolistDefault_ShouldQueueCommands` skip.
- `/*/*/Clone*/*`: 49 passed.
- `/*/*/EditMessageTests/*`: 5 passed.
- Full `SharpMUSH.Tests.ScenePlugin`: 159 passed (including the unsafe edit/history regressions).
- `/*/*/StringFunctionUnitTests/Edit*`: 6 passed, including markup-preserving pure string edit cases.


## Independent review correction wave 1

Fix range starts at `3f290d7a51b3a48a2a4418c59c3c6be66eb870c3`.

| Finding | Fix and new regression |
| --- | --- |
| Corrupt value hid SAFE/WIZARD/INTERNAL/LOCKED metadata | Exact provider reads retain the real attribute metadata and mark only the body unreadable. AttributeService refuses display/copy of unreadable bodies; AttributeWriter still checks all flags, including a corrupt protected ancestor. Invalid access metadata produces InvalidDataException; read/write and snapshot service boundaries map it to explicit refusals. Regression checks four protected leaf flags, a protected corrupt ancestor, authorized God replacement, and corrupt-metadata startup/write refusal. |
| First existing-counter rollback overwrote concurrent future bytes | Every rollback, including first cleanup, freshly reads and validates the persisted value before any restoration write. Publication-failure regression installs exact future bytes while Quartz awaits, then proves no second provider write, retained queue accounting/repair barrier, and successful retry only after explicit compatible repair. |
| Full eager/lazy readers aborted corrupt records | Full-tree RangeMap projections catch each bad row and return null, preserving cursor progress. Exact authorization metadata remains separate from display omission. Corrupt full-tree ancestors suppress their subtree conservatively; sibling command/listen definitions remain discoverable. Tests cover attr.val and attr.meta corruption, full eager/lazy metadata enumeration, and command/listen discovery beyond the corrupt row. |
| Raw write serialized before replacing bytes | PreparedAttributeWrite now takes optional original serialization and chooses it before calling Serialize. A registered test codec successfully reads a concrete layer but throws/counts on Write; raw copy and reopen preserve exact bytes without writer calls. Snapshot capture/restore also preserves exact bytes and asserts the raw service/provider stage never invokes the writer. |

Before fixes, the expanded stored-markup regression wave failed 10 of 24 scenarios (the original 14 passed), including all protected-value bypasses, full-tree/discovery aborts and readable/unwritable raw copy. Separate admission rollback and snapshot restore reproductions also failed. A follow-up warm-cache corrupt-metadata test exposed provider write decoding outside the service mapping; that boundary now returns a refusal without a partial write. No access flags are fabricated or dropped to represent absence.

The snapshot authorization audit confirms ReadContext still receives the original valid flags for unreadable bodies and maps unparseable metadata to a typed corrupt-snapshot failure. Explicit replacement of a corrupt body remains possible for an actor authorized by its retained metadata.

Correction-wave verification (single supported treenode filter per run):

- `/*/*/StoredMarkup*/*`: 25 passed, including all new access-metadata, discovery and raw-copy cases.
- `/*/*/ObjectSnapshotTests/*`: 30 passed, including readable-but-unwritable codec snapshot restore.
- `/*/*/QueueAdmissionTests/*`: 167 passed, including existing-counter first rollback and all prior repair/publication scenarios.
- `/*/*/AttributeTreeWriteGateTests/*`: 14 passed.
- `/*/*/AttributeReadCostTests/*`: 18 passed.

All 254 affected scenarios passed without skips. Earlier scene, feed, clone and pure edit suites were not repeated for this correction wave; the shared raw persistence seam is covered by the new copy/reopen and snapshot tests. The registered readable/unwritable codec exists only in the test assembly bootstrap. Runtime scratch remains disk-backed and outside the source diff.

Final correction build: `dotnet build SharpMUSH.Tests --no-restore -m:2 -nr:false` passed with zero warnings/errors and the normal formatting gate enabled. Two whitespace passes converged and `git diff --check` passed. No production data or deployed services were changed.


## Independent review correction wave 2: inherited discovery masks

Fix range starts at `465f86a6edf63c11dd62ad51d11c09927f931ffd`. The original four findings remain addressed. Re-review exposed a distinct P1 inheritance regression: omitting corrupt local rows from display enumeration also erased names and NO_COMMAND masks from the scanner's state before it visited parents.

The attribute aggregate now supplies `GetDiscoveryAttributesAsync` specifically for the existing command/listen Mediator query handlers. It retains existence and actual flag metadata when only the body is unusable. Malformed metadata returns a discovery-only `IsMetadataUnreadable` marker, which conservatively masks its name and subtree; it is not display/copy or authorization data. No access flags are fabricated. Display/copy streams retain their existing omission policy.

Command discovery preserves `seenNames` and valid NO_COMMAND/NO_INHERIT semantics before excluding unusable bodies. A metadata-unreadable marker masks farther levels even if a nearer object already saw that name. Listen snapshot/discovery uses the same aggregate stream; ListenAttributeSearch carries equivalent shadowing and mask state across its parent walk. Both compilers explicitly refuse unreadable markers, even if a caller supplies pattern-shaped fallback text.

Eight real-handler regressions failed before the fix and cover both command and listen inheritance: local NO_COMMAND with corrupt body versus a matching parent subtree; corrupt ordinary local value versus a same-name parent definition; corrupt local metadata versus a parent subtree; and corrupt metadata on an intermediate parent versus a grandparent subtree, including a nearer child's ordinary same-name shadow. Every scenario also requires valid unrelated child and parent siblings to remain discoverable. The command cases execute the actual GetCommandAttributesQueryHandler parent walk; listen cases execute ReadPhaseAsync through the actual GetListenAttributeSnapshotQueryHandler. Direct compiler regressions prove unreadable and metadata-unreadable rows cannot compile.

The earlier same-object discovery regression now explicitly sets NO_COMMAND on its valid-metadata ancestor. A corrupt body alone does not erase valid metadata or invent a subtree mask; display omission and discovery policy are separate contracts.

Two additional command/listen positive controls preserve known NO_INHERIT ancestor semantics: an unknown-metadata descendant inside a valid private intermediate tree does not invent a mask against a visible grandparent definition. Conservative unknown metadata applies after known inherited-private boundaries and before seen-name suppression.

The normal correction-wave build (`dotnet build SharpMUSH.Tests --no-restore -m:2 -nr:false`) passed with zero warnings/errors after two whitespace-format passes. `git diff --check` passed. Earlier scoped compiler and listen snapshot runs passed 2 and 7 scenarios respectively; final inherited discovery/private-boundary results are recorded below. No deployment or production-world mutation occurred.

Final wave-2 scoped results (separate supported treenode filters):

- `/*/*/StoredMarkup*/*`: 35 passed, including all retained stored-markup regressions and the 10 inherited mask/private-boundary cases.
- `/*/*/UnreadableDiscoveryCompilationTests/*`: 2 passed.
- `/*/*/ListenAttributeSnapshot*/*`: 7 passed (identity/cancellation and listen search contracts).
- `/*/*/CommandAttributeInheritanceTests/*`: 10 passed.

All 54 scoped scenarios passed without skips. The original eight mask scenarios failed 8/8 on the prior commit. Unrelated broad suites were not repeated; the previous correction's raw persistence, authorization and scheduler fixes remain intact.

## External review follow-up: matched subtrees and null metadata

At `0540957be`, six regressions reproduced null metadata/`Flags:null` aborting eager and lazy pattern scans and escaping exact-path authorization as NullReferenceException. An actual `@clone` regression reproduced a readable child copied beneath an unreadable `no_clone` branch, recreating that branch without its restriction.

Attribute readers now share metadata validation. Invalid metadata reaches existing row-isolation or exact-path refusal handling; writes also refuse it instead of treating JSON null as a missing node. Pattern scans suppress descendants after omitting a matched unreadable parent, preserving parent-before-child copy semantics. Lazy scans remain metadata-only, and unmatched rows retain the existing no-value-read behavior. Dedicated discovery markers remain separate from display/copy results.

Verification with pinned SDK, PATH first, TMPDIR/TMP/TEMP `/temp/codex/markup`, rootless Podman and Ryuk disabled:

- `dotnet build SharpMUSH.Tests -m:2 -nr:false`: zero warnings/errors.
- `dotnet run --project SharpMUSH.Tests --no-build -- --treenode-filter '/*/*/StoredMarkupSafetyTests/*'`: 16 passed (six new cases failed before the fix).
- `dotnet run --project SharpMUSH.Tests --no-build -- --treenode-filter '/*/*/StoredMarkupCommandSafetyTests/*'`: 26 passed (new clone case failed before the fix).
- Existing `ObjectDestructionTests` full class: 18 passed before these production edits. The single CI purge failure therefore remains unexplained; no speculative purge change was made.
- Full engine suite with `--maximum-parallel-tests 8`: 17,618 total, 17,570 passed, 47 skipped and one failure: `Purge_TakesTwoPasses_AdvancingThenFreeing` retained its target after the second purge (1.124s). See the bounded investigation below; this run is not claimed green.


## Bounded purge fixture investigation and isolation

Fix range starts at `510f6d637`. The CI failure at `0540957be` observed a missing first-pass GOING_TWICE flag (20ms); the subsequent complete engine run observed the target surviving the second purge (1.124s). The original 18-test class passed by itself. Both failed assertions discarded the command result and lacked the target's pre-purge flag state, so neither trace identifies the exact causal state.

A temporary controlled reproduction added 512 GOING + GOING_TWICE objects to the original shared fixture, then ran the actual two-pass command test with returned results captured. Both purge commands returned `HadErrors=False`, and the target was freed correctly (test 26.807s; complete process 51.547s). The normal parser budget is 2000ms. This control does **not** support claiming accumulated doomed-object volume or budget exhaustion caused the failures; no production purge change is justified by this evidence. Temporary backlog creation and console instrumentation were removed.

The fixture had a separate demonstrable isolation defect: its global purge and OBJECT`DESTROY hook used the entire session's database. `[NotInParallel]` prevented concurrent test bodies but could not make existing unrelated objects or world configuration belong to this test. The fixture now uses the established `UsesOwnWorld` factory mechanism, with one class-wide keyed serialization boundary for every destruction/event test. The four global serialization annotations are removed. A second isolated control world supplies a GOING-only sentinel; two actual purges in the destruction world must neither advance nor free it, and cleanup removes it explicitly. No global shared-world sentinel is introduced.

The two-pass test retains its actual first-pass advance and second-pass deletion assertions. It now verifies the @destroy result and GOING-only precondition, checks both purge `HadErrors` results, and includes returned text, target GOING/GOING_TWICE state and both world flag counts in failure diagnostics. The original full-suite failure's exact cause remains unknown; isolation removes a known cross-suite coupling rather than asserting a speculative root cause. The coordinator will verify the combined implementation with its final integration full-suite run.

Final bounded verification (pinned SDK first on PATH; TMPDIR/TMP/TEMP `/temp/codex/markup`; rootless Podman socket and Ryuk disabled):

- Two whitespace formatter passes converged; `git diff --check` passed.
- `dotnet build SharpMUSH.Tests -m:2 -nr:false`: passed with zero warnings/errors, normal formatting gate enabled (21.35s).
- `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/ObjectDestructionTests/*' --output detailed`: **19 passed, zero failed/skipped** (30.109s), including every previous destruction behavior, actual first/second purge states and the independent-world control with local deletion asserted.
- Local ignored evidence: `work/review-followups/purge-backlog-control.log`, `purge-isolated-build.log` and `purge-isolated-class.log`.

No full-suite rerun was undertaken for this bounded fixture correction; the coordinator owns final combined integration. No production API, plugin contract or purge semantics changed.
