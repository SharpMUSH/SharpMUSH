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
