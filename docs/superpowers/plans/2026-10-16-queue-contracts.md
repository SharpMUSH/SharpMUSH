# Queue contracts and accounting

Implements the approved queue lane of [open issue consolidation](../specs/2026-10-16-open-issue-consolidation-design.md), issues #1792, #1793 and #1741. This lane stacks on the argument contracts PR; its supplied-key metadata and case-insensitive argument bindings remain intact.

## Execution boundaries

| Producer | State and admission contract |
| --- | --- |
| Ordinary `@trigger` / `@trig`, including `/match` and `/args` | Admit the computed action body exactly once. Attribute holder is executor; triggerer is caller and default enactor; `/spoof` preserves enactor. Copy arguments, supplied-key metadata, q-registers and output. New attribute scope drops the invoking iteration/switch/regexp frames. |
| `@trigger/inline` | Execute within the current entry and budget. Registers pass back unless localized/cleared. Break propagates unless `/nobreak`; `/inplace` means inline/localize/nobreak. |
| `@include` and inline control flow | Continue within the current entry; existing localization and break rules apply. |
| Immediate command lists, reservations, `@wait` delays, semaphores and timeout publication | `TaskScheduler.CaptureExecutor` captures a `SnapshotForQueuedAction` at admission. All share existing owner/global accounting. Publishing a reservation or releasing a delayed entry does not create another entry or budget. |
| `@dolist`, `@map`, `@switch`/`@select` and other queued control flow | Same scheduler snapshot boundary; retain detached nested iteration/regexp/switch frames needed by deferred text. Do not discard these globally. |
| SQL row/header callbacks | Capture each callback's complete state while submitting it. The completion reservation retains existing FIFO/accounting rules. |
| HTTP response callbacks | Capture caller state before admitting the request. Response arguments and compatibility response q-registers are installed on that private state after I/O; never mutate the submitter. |
| Async attribute factories | A factory must capture its input values before admission; its result may depend on deferred I/O. The scheduler starts the returned state with independent counters before executing the attribute. |
| Typed connection commands | `ForTypedLine` creates a fresh evaluation at execution; connection/session/input-capture tickets preserve their existing admission semantics. |
| Events, DidIt, connection hooks, input sessions, recurring jobs and HTTP handlers | Existing producers construct fresh roots or snapshot the explicit callback state. Direct work callbacks retain their existing scheduler budget and quota path. |

`SnapshotForQueuedAction` copies argument dictionaries as values, keeping failure metadata while dropping deferred parser closures, parse trees and mutable argument arrays. It copies the top q-register frame and required nested contexts, shares immutable supplied-key provenance/restrictions, resets per-entry invocation/depth/movement counters, break/history/command-dispatch context, and leaves the new elapsed budget to the scheduler. Limits are not removed: a failing entry stops its ordinary or piped command list; independent siblings start with fresh counters.

PennMUSH reference: commit `95ad3511d`, [`src/set.c`](https://github.com/pennmush/pennmush/blob/95ad3511d/src/set.c) `do_trigger` clears `parent_queue` for ordinary triggers, `src/cque.c` `queue_attribute_useatr` supplies `PE_INFO_DEFAULT` with ARG/Q registers, while `src/parse.c` `pe_info_from` distinguishes cloning a deferred text context. This is why trigger scope is reset but generic queued text retains its required contexts.

## Own-time audit

Raw argument evaluation pauses `InvocationClock` around the existing `FunctionParse` call, without moving evaluation to another parser scope. The shared helper is only timing, not a new evaluation API.

| Direct parse sites | Treatment |
| --- | --- |
| COND family: condition, selected result, default (3) | Pause raw argument work. |
| IF/IFELSE branches (2) | Pause raw argument work. |
| ALLOF candidates and delimiter (2) | Pause raw argument work. |
| LETQ body, with and without bindings (2) | Pause while preserving the localized register scope. |
| CAND/COR family shared short-circuit argument path (1) | Pause raw argument work. |
| LOCALIZE (1) | Pause inside the scope already established by the function flags. |
| RESTRICTEDEXPR expression (1) | Pause in the restricted parser scope. |
| BENCHMARK repeated expression (1) | Pause each argument evaluation; benchmark's elapsed result remains its own measurement. |
| UDEFAULT/ULDEFAULT/U2DEFAULT selector, fallback and actual inputs (3) | Pause raw argument work; preserve selector/argument error metadata. |
| OBJEVAL expression (1 shared site) | Pause raw expression in its selected executor scope. OBJEVALS evaluates already-parsed text as function work. |
| PRIVATIZE expression (1) | Pause in its selected localized/private register scope. |
| UDEFAULT actual attribute body, S evaluated text, FN synthetic dispatch (3) | Continue charging function work; these are not raw argument parsing. |

The original audit has 21 direct parse sites; the helper adds its shared call, and the OBJEVAL/OBJEVALS distinction makes their shared site explicit. Existing `GetParsedResultAsync` paths already pause argument time. Deterministic tests use a counted clock, not wall-time thresholds; localization regressions assert both the result and restored outer registers.

## Verification

The implementation sequence was real-scheduler red regressions, shared admission and trigger boundaries, deferred factory/raw parse audit, limits/cancellation/refusal/completion, bounded production measurements and dependent suites. No new batching API is required by the measured workload.

| Regression | Observed before the fix | Accepted behavior |
| --- | --- | --- |
| Ordinary trigger ordering and parent halt | Body ran synchronously before the submitting tail. | Exactly one admitted body, after the submitting list; a subsequent halt can cancel it. |
| Queued arguments/reservation | Mutation leaked; queued argument retained a deferred parser callback. | Captured values and supplied-key provenance; no executable callback retained. |
| HTTP/SQL callback registers | Later caller mutation appeared in callback input; HTTP response mutated the caller. | Admission-time q-registers on a private callback frame. |
| Trigger scope | Invoking switch frame survived into a new attribute entry. | Trigger starts without old loop/switch/regexp frames; generic deferred text retains cloned frames. |
| Raw own-time | Nested argument work inflated own time (51 counted milliseconds versus expected <10). | Nested `ADD` time excluded, including localized/private scopes; restored register values checked. |
| OBJEVALS | Treating its already-parsed text as raw dropped its work (4 counted ms versus expected >=50). | Actual text evaluation remains function work. |
| Oversized ordinary and piped lists | After exceeding 100 invocations, the literal `DONE=1` tail still ran. | Tail does not run; invocation-failure diagnostics remain visible and an independent sibling returns `3`. |
| Async factory counters | A factory returning a used state with its limit flag set skipped the callback body. | Fresh counters execute `captured|3`; original counters remain unchanged. |
| Existing include/recursion tests | Read immediately after ordinary trigger, before its newly queued body. | Explicit FIFO barriers before checking the original semantic assertions. |

The 100-invocation test executes 20 independent entries, each with 12 explicit `ADD` calls plus control work, then separately rejects a single 120-`ADD` entry. Limits therefore remain effective while work spread across independent entries receives independent counters. Both ordinary and piped failure tails are covered.

The executable guide test reads its commands directly from `docs/guides/bounded-queue-work.md`, admits two START requests before either runs, then checks exactly 80 completions, every stored row, `STATUS=complete` and no remaining worker entries. Cancellation and refusal tests each finish 8 of 80 rows, leave `STATUS=running`, and check exactly one cancelled continuation or one global-limit refusal respectively. Neither claims completion.

The test fixture uses its isolated options wrapper for temporary low limits and restores it in `finally`. Its wrapper is a test substitute: `@config/set` updates the real options monitor but does not update this substitute, so it cannot establish the low-limit precondition in this fixture.

Reproduce with .NET SDK pinned by `global.json`, a working Podman/Docker socket for test services, and disk-backed temporary storage:

```bash
export TMPDIR=/temp/codex/queue TMP=/temp/codex/queue TEMP=/temp/codex/queue
export DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true
dotnet build SharpMUSH.Tests -m:2 -nr:false
dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/QueueContractRuntimeTests/*' --output detailed
```

Run each class in the final acceptance table using the same single-class filter. The original red and final local logs are retained under `work/queue`; the source-controlled tests and this record are the portable evidence. No full-suite or deployed-runtime claim is made here.

## Initial focused acceptance

Build: `dotnet build SharpMUSH.Tests -m:2 -nr:false`, 0 warnings and 0 errors. Changed C# projects were formatted in two passes and passed the build's formatting gate. Initial lane acceptance at `8b232a185`: **621 passed, 1 pre-existing skipped, zero failures**, each class run separately with the exact filter above.

| Class | Passed | Skipped |
| --- | ---: | ---: |
| `InvocationMetricsTests` | 29 | 0 |
| `QueueContractRuntimeTests` | 11 | 0 |
| `ParserStateContextTests` | 8 | 0 |
| `QueuedUserCommandTests` | 39 | 0 |
| `QueuedControlFlowStateTests` | 3 | 0 |
| `IncludeBreakPropagationTests` | 8 | 0 |
| `RecursionAndInvocationLimitTests` | 18 | 0 |
| `GeneralCommandTests` | 71 | 1 |
| `QueueAdmissionTests` | 166 | 0 |
| `QueueQuotaTests` | 5 | 0 |
| `QueuePauseTests` | 52 | 0 |
| `QueuedHaltTests` | 7 | 0 |
| `HttpCommandTests` | 66 | 0 |
| `DatabaseCommandTests` | 53 | 0 |
| `NamedArgumentCommandTests` | 51 | 0 |
| `InplaceDepthLimitTests` | 4 | 0 |
| `PipedOutputSubstitutionTests` | 18 | 0 |
| `ExamplePackageTests` | 12 | 0 |

The new `OrdinaryMatchedTriggerQueuesTheComputedBodyAndExplicitAliases` test verifies a real queued regexp trigger executes the stripped action body after its parent tail, with a case-insensitive explicit named override and its numbered alias. The prerequisite catalog correction is also checked on this branch. `GeneralCommandTests` retains its existing `CommandAliasRuns("l")` skip, marked "Not yet implemented properly". The argument branch was merged after these checks; its merge had an identical Git tree before and after, so it changed ancestry only.

The measured workload uses a fresh isolated Lightning test world and two distinct mortal owners on the actual single queue consumer. Each of 80 rows performs 60 `ADD` calls through `ITER`, writes a deterministic result, and increments a completion attribute. One row warms parser/storage paths first. A gate admits the producer and second actor in fixed FIFO order; latency runs from gate release through the second actor's real command. Elapsed throughput ends after the worker queue drains, before the result-verification reads. Invocation limit 25,000; queue-entry elapsed limit 30,000 ms; mortal owner capacity 100; global capacity 10,000. Linux x64, Debug build, .NET 11 RC2; other host test work was active.

| Strategy | Second actor (ms) | Exact pending at witness | Sampled peak admitted | Completed entries | Mean / max execution per entry (ms) | Total (ms) | Rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Ordinary dolist | 6.522 | 80 | 80 | 81 | 31.029 / 330.625 | 2524.992 | 31.7 |
| One inline block | 1978.148 | 0 | 1 | 1 | 1977.897 / 1977.897 | 1978.187 | 40.4 |
| Inline pages of 8 + trigger | 687.402 | 1 | 1 | 10 | 216.720 / 687.204 | 2171.753 | 36.8 |

Every mode verified all 80 stored results and the completion count, with zero refused/cancelled entries and no remaining worker entry. Dolist's 81 completed entries include the producer; the other strategies execute one block or ten pages. Execution costs are measured by the scheduler's diagnostics, so the per-entry mean includes the producer where one exists.

The witness runs on the only consumer, so its exact worker count represents waiting entries. The 1 ms peak sampler includes running and waiting entries and can miss the brief overlap between a running page and its admitted continuation (maximum two by construction). Earlier samples observed that overlap. This is one final observation per strategy, not a distribution, an SLO or a claim that paging is universally faster. Shared-host scheduling and storage noise are visible in the page maximum; prior exploratory samples had page latency of about 72–169 ms. Ordinary dolist minimizes first-turn latency here but admits the entire backlog; pages bound pending work and yield between bounded batches. Page size must be chosen for the actual row cost.

## Independent review round 1: HTTP response ownership

The reviewer established one additional mutable-state leak: an ordinary triggered child could change the HTTP response after its submitting handler finished. The permanent real-scheduler regression runs `@respond/type`, `/header` and status changes in the child, verifies the parent response at submission and after the queue drains, and checks that the child itself completed. It failed before the fix (`application/child` instead of `application/parent`); its inline positive control already passed. A separate snapshot regression also failed because the live context was retained.

`SnapshotForQueuedAction` now clears the inherited `HttpResponse` reference. That drops the whole request-owned mutable context, including application viewer/instance metadata when composed with the applications lane. It covers independent trigger/wait/control-flow/callback snapshots rather than only the trigger call site. Inline execution still uses the active response.

The inbound HTTP request is different: `HttpHandlerCommandService` creates its new response inside `ExecuteAsync`, after the socket queue entry begins, and installs it on a fresh root. The added `AQueuedHttpHandlerOwnsItsFreshResponseContext` regression exercises the real dispatcher and verifies status, content type and body. This checks the actual inbound handler path separately from outbound `@http` callback tests.

`dotnet build SharpMUSH.Tests --no-restore -m:2 -nr:false` passed with zero warnings/errors after two formatting passes. Five exact-class runs passed **100/100**: `ParserStateContextTests` 9, `QueueContractRuntimeTests` 13, `HttpHandlerQueueTests` 4, `HttpCommandTests` 66 and `IncludeBreakPropagationTests` 8. The two new ownership regressions were red before the fix; the inline control stayed green. No full-suite rerun was needed for this scoped finding. Logs: `work/queue/review1-red-*` and `review1-green-*`. The source was rebuilt before testing, replacing the reviewer's temporary probe binary.

## Integration boundary

The argument prerequisite's case-insensitive `/match` overlay and Time package 1.3.1 explicit HTTP body forwarding are retained. Named and numbered aliases of real inputs remain supplied; generated helper padding remains excluded. The storage lane's `TaskScheduler` semaphore guards occupy admission/rollback/repair/release methods; this lane changes only `CaptureExecutor` and the async attribute execution state there. Those edits are separate hunks, with combined-branch verification still required.


### Deferred package lifecycle fixture correction

Final CI exposed `TheBundledPackagesMakeWayForTheSourceAndComeBackPastIt`: the isolated import world replaces its queue with an inert substitute, so Scene's ordinary `@trigger %!/STARTUP` never installs the speech hooks. Reproduced the missing `CommandHook` on the queue branch before changing the fixture (one failed test, 26.015 s). The Scene-enabled private world now uses the real immediate scheduler with a substituted Quartz timer backend, preserving isolation from Quartz's process-wide host. It drains startup before import removes the old package objects, and drains reinstallation before checking the new hook targets. Production trigger behavior and hook assertions remain intact.

The complete `PennMUSHDbrefPreservationTests` class passed 31/31 in 49.048 s, with a zero-warning build. Non-Scene import tests retain the original inert queue. Final combined verification is recorded by the coordinator.
