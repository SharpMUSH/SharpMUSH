# Application flow validation (#1794, #1795, #1796)

Design: [approved consolidation design](../specs/2026-10-16-open-issue-consolidation-design.md).
Plan: [application lane](../plans/2026-10-16-application-flow.md).

## Chosen boundary

A package-only draft implementation can declare/project fields, but it cannot safely provide per-incarnation ownership, bounded expiry and atomic File receipts across independent terminal/HTTP callbacks. The implementation retains the optional package and adds only a transient core adapter (`@appdraft`) plus pure structural validation (`appfields`). No world record store, application registry, transaction protocol or workflow DSL was added. Fixed actions call ordinary softcode VALIDATE/FILE children; Jobs still owns permission checks and writes through its existing filing routine.

The Jobs proof has four declared fields and two authoritative callbacks. Adding a field changes one declaration; the test appends `extra` and boolean `flag` and verifies its terminal and portal projection without another mapper/controller. Changing max_length changes one declaration; the test verifies portal hints, terminal draft validation and legacy +request validation. Conditional review belongs to WEB`DRAFT`ANSWER and world checks to FORM`VALIDATE; terminal-only presentation belongs to FORM`DISPLAY. Unsupported types/rules fail explicitly.

## Regression and acceptance matrix

| Contract | Executable evidence |
| --- | --- |
| Real wire null versus absent defaults | SchemaFormModelTests.WireNullDefaultIsPresentWhileOmittedDefaultStaysAbsent (failed before the converter) |
| Number/false/zero/empty/null and mixed JSON values | TypedSeeds_PreserveNullNumberFalseEmptyAndAbsence; ExplicitNullPrefillSuppressesDefaultAndMixedArraysStayTyped (number and removed-field regressions failed on baseline) |
| Removed fields retained locally but not posted | RemovedFields_AreDraftOnlyAndUnboundErrorsRemainVisible; Payload projects current form fields; views post only button values |
| Reset → merge → replacement data → defaults | SchemaApplicationStateTests.Success_ResetMergeThenReplacementDataAndDefaults; NamedResetRemovesOnlySelectedKeysThenSeedsResultingDefaults (list reset failed before bool/list converter) |
| Failure replacements without success effects | Failure_ReplacesPresentationButNeverResetsMergesOrRedirects; host form→view→failed form test |
| Invisible/removed/other-page/empty-form errors | RemovedFields_AreDraftOnlyAndUnboundErrorsRemainVisible; ErrorsForAnotherPageRemainVisibleInTheSummary; FormToViewAndFailedViewToForm_RenderResultingKindAndErrors (last two failed before summary fixes) |
| Form↔view and same-kind compatibility | SchemaApplicationHostTests plus existing SchemaActionTests and SchemaViewRenderer suites |
| Repeated clicks, field editing while pending | ReplacedInstance_IgnoresDelayedResponse asserts one request and disabled fieldset |
| Retired response token and redirect | RetiredResponsesAndConcurrentAdmissionsCannotChangeCurrentInstance; ReplacedInstance_IgnoresDelayedResponse |
| Active-character stale private data | CharacterSwitchRetiresDelayedPrivateResponseImmediately |
| Unsupported version/kind | UnsupportedSchemaNeverReplacesTheCurrentPresentation |
| Ordinary terminal commands between edits | JobsIntegrationTests.DraftAdapterSharesReviewAndFileAcrossTerminalAndPortal |
| One declaration drives both clients and legacy checks | Same Jobs proof changes max_length and appends a declared field |
| Literal brackets, percent substitutions and semicolons | Same Jobs proof verifies exact persisted JOB`ENTRY`1`TEXT; draft service literal-value test |
| Current bucket/permission rechecked at File | FileRechecksPermissionAndIsolatedInstancesCannotConsumeDraft; Jobs proof closes Bugs after review and asserts bucket error/form replacement with no null prefill |
| Actor/instance ownership and malformed action/field rejection | ExpiryCancelForgedActorAndUnsupportedActionsFailClosed; wrong actor and wrong instance fail; server adapter derives actor from bound runtime/verified HTTP context |
| Two tabs/connections and retired binding | Jobs proof rejects another form nonce; service tests isolate instance keys; terminal logout/rebind rejects the old draft |
| Descriptor-private replies and incarnation fence | Jobs proof asserts draft text never reaches recipient-wide notifications and a second handle cannot consume it; ApplicationDraftPublicationTests verify original SessionId publication and retired/missing handle rejection |
| Independent hosts and record generations | SchemaApplicationHostTests verifies distinct per-host nonce and replacement-record nonce rotation; timeout permits retry |
| Cancel, Back and expiry | Jobs proof plus ReviewBackFile_ValidateLiteralFieldsAndReplayReceipt and ExpiryCancelForgedActorAndUnsupportedActionsFailClosed |
| Duplicate File/lost response/concurrent retries | ConcurrentFileRetriesSerializeAndConflictingPayloadCannotRepeatEffect; Jobs proof replays the same receipt and verifies no additional job |
| Callback failure after starting an effect | CallbackFailureAfterEffectCannotAutomaticallyRepeatTheEffect; provisional receipt fences automatic replay |
| Indexed help and registration | Jobs proof exercises @appdraft/appfields and reads help @appdraft / help appfields(); ClientData help generation is part of the build |
| Legacy +request and modal @input | Whole existing Jobs class; existing InputSessionServiceTests and InputSessionCommandTests (no modal routing changes) |

## Commands and results

The repository-pinned SDK was used, with its directory first on PATH for nested formatting. Builds were bounded with `-m:2 -nr:false`. Runtime tests used disk-backed `/temp/codex/apps` and rootless Podman (`DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`, `TESTCONTAINERS_RYUK_DISABLED=true`). All modified C# projects received two formatting passes; builds ran their formatting gate.

- Entire bUnit project: **2,524 passed, 0 failed** (12.207 s).
- Entire Jobs integration class: **20 passed, 0 failed** (5 min 03.257 s), before final descriptor-publication and generic typed-field tightening.
- Existing InputSession compatibility classes: **192 passed, 0 failed** (30.098 s).
- ApplicationDraftPublicationTests: **2 passed, 0 failed** (254 ms), including wrong/missing transport rejection and retired session handling.
- Final integration build: **0 warnings, 0 errors**; final targeted Jobs proof: **1 passed, 0 failed** (49.931 s) after all final changes.

The targeted Jobs proof was rerun after descriptor fencing and after adding an additional boolean declaration. That boolean test first failed because a field-name special case produced a string; declaration-type conversion fixes the behavior without adding another controller mapping.

## Limits

Drafts and receipts are process-local, expire after 30 idle minutes, and are capped at 64 per actor / 4,096 globally with 64 fields / 64 KiB of values. They do not promise exactly-once effects across process failure. A started File with no callback answer leaves a fail-closed receipt requiring the user to check the world before beginning another operation. The form nonce is an untrusted form-instance selector bound to the verified actor, never actor authorization.

A nonmodal terminal edit is one ordinary input command. Transport-level newline framing is unchanged; JSON/portal values can contain multiline text, and the adapter does not infer clipboard or command boundaries. Existing optional short modal @input prompts remain available to package authors; no passthrough mode was added. The existing modal compatibility suite covers its cancellation, timeout, stale transport and callback behavior.

The complete engine/integration repositories and manual browser sessions are left to aggregate CI/review; the lane runs the entire bUnit project, entire Jobs integration class and focused existing input compatibility classes. No merge or deployment was performed.

## Review round 1 corrections

Three review findings were reproduced and corrected in one revision:

- A deliberately reserved earlier `HandlePublicationLane` slot caused the real notifier to publish private output after logout/rebind (one failing regression). `ConnectionData.Binding` now supplies an immutable binding identity and publication semaphore. `NotifyToBinding` carries the captured connection and transport SessionId through the lane wait, then checks the binding at the bus publication boundary. Bind, Unbind, BindAccount and Disconnect share that fence: retirement either wins first or waits for an already admitted publication to complete. Draft instance IDs also derive from that binding identity. `NotifyToSession` remains available. Tests cover different-actor and same-actor rebinds, and a blocked bus publication orders retirement after the admitted send.
- Delayed widget GETs could install old private records as fresh hosts (the original delayed-schema test failed). Widget loads now use a generation across route resolution, object resolution, schema GET and data GET; actor changes reload, and disposal retires pending reads. OOB callbacks also check their captured generation. Final tests await the complete old load and cover delayed schema/data separately for configuration, cascading profile context, active actor and disposal.
- Real Jobs review/back turned an added number default `0` into null (the regression failed at GetInt32 on Null). `WEB`DRAFT`DATA now checks JSON member presence, omits absent fields and copies raw present values. Real review and Back preserve defaulted zero, explicit null, false and empty text; a second review preserves explicitly posted zero.

All commands below used the repository-pinned SDK first on PATH, `TMPDIR=TMP=TEMP=/temp/codex/apps`, and builds capped at `-m:2 -nr:false`. Runtime integration used the rootless Podman environment documented above. Each changed C# project received two whitespace-format passes; final builds ran the formatting gate.

| Final command | Result |
| --- | --- |
| `dotnet build SharpMUSH.Tests.Integration -m:2 -nr:false` | 0 warnings, 0 errors |
| `dotnet run --no-build --project SharpMUSH.Tests.Integration -- --treenode-filter '/*/*/JobsIntegrationTests/DraftAdapterSharesReviewAndFileAcrossTerminalAndPortal'` | 1 passed, 0 failed; 1 min 14.306 s |
| `dotnet build SharpMUSH.Tests.BUnit -m:2 -nr:false` | 0 errors; 54 existing warnings, none in changed files |
| `dotnet run --no-build --project SharpMUSH.Tests.BUnit -- --treenode-filter '/*/*/Schema*/*'` | 141 passed, 0 failed; 650 ms |
| `dotnet run --no-build --project SharpMUSH.Tests.BUnit -- --treenode-filter '/*/*/ApplicationDraftServiceTests/*'` | 6 passed, 0 failed; 314 ms |
| `dotnet build SharpMUSH.Tests -m:2 -nr:false` | 0 warnings, 0 errors |
| `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/ApplicationDraftPublicationTests/*'` | 5 passed, 0 failed; 460 ms |
| `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/ConnectionService*/*'` | 60 passed, 0 failed; 2.332 s |
| `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/Notify*/*'` | 25 passed, 0 failed; 409 ms |
| `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/InputSession*/*'` | 192 passed, 0 failed; 22.410 s |

Only affected suites were rerun for this review round. Earlier whole-project results above remain historical evidence, not a claim of a new complete aggregate run. The limits above are unchanged.

## Review round 2 correction

The new publication fence exposed a close-versus-binding race: an admitted private send held the binding gate, Bind queued before Disconnect, and Bind replaced the record before Disconnect's old-record removal. The transport remained logged in. The regression blocks the real notifier's bus send, queues Bind then Disconnect against that same transport, and verifies removal after release. A second case replaces the socket while both operations wait and verifies that the new SessionId survives. Both also reject any later private send from the original binding.

Disconnect now captures the transport metadata identity and SessionId, then re-reads the record after acquiring each binding gate. If the same transport changed bindings, it releases the old gate and retries against the current one. Removal and player-index counts still occur together under the existing binding lock, after confirming the held gate belongs to the current binding. A changed SessionId or replacement metadata identity ends the stale close without touching the replacement transport. Publication retirement ordering is retained.

Using the same pinned SDK, disk-backed environment and build caps as round 1, the direct regression was verified against the previous implementation (two cases: the same-transport close failed, replacement-transport safety passed), then the fixed implementation was rebuilt. Only nearby lifecycle/publication suites were rerun:

| Command | Result |
| --- | --- |
| `dotnet build SharpMUSH.Tests -m:2 -nr:false` | 0 warnings, 0 errors |
| `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/ApplicationDraftPublicationTests/*'` | 7 passed, 0 failed; 237 ms |
| `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/ConnectionService*/*'` | 60 passed, 0 failed; 2.269 s |
| `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/ConnectionIncarnationTests/*'` | 14 passed, 0 failed; 741 ms |

Both changed C# projects received two whitespace-format passes; the final build ran the formatting gate. Final test binaries and reusable build outputs were retained. No widget, Jobs, permission or workflow behavior changed in this round.
