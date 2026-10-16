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
| Per-record isolation | `MalformedAttributeDoesNotAbortItsNeighbours` variants corrupt attr.val or attr.meta, reopen the world, confirm the exact bad row is absent and surrounding enumeration survives |
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
