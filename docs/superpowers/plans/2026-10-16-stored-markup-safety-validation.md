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
