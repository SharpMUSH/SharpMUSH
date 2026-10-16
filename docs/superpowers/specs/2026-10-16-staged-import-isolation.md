# Production database import isolation proposal

Review proposal for [#1969](https://github.com/SharpMUSH/SharpMUSH/issues/1969). This starts the follow-up; it does not implement transactional import or make current imports safe against a running game.

## Verified problem

`EngineRegistration` registers `IPennMUSHDatabaseConverter` directly as `PennMUSHDatabaseConverter`. Its requests resolve the host's Mediator/store graph. Those stores and the Lightning provider are singletons. Opening an ordinary DI scope does not redirect those requests to another world.

Lightning already exposes `CreateStagingAsync` and `PromoteToLiveAsync`, but their callers are staging tests; the production import path does not use them. The converter can catch an exception and return an aborted result after earlier writes have reached the configured world. A successful export/parse is not a transaction around those writes.

`LightningStagingDatabase.PromoteToLiveAsync` currently closes staging, swaps directories, reloads definitions, then calls `RecomputeCountersAsync(ct)` before setting `IsPromoted`. Cancellation after the swap must not be reported as an ordinary pre-commit cancellation: the active world has already changed.

## Recommended boundary

Introduce an import coordinator around all production import callers. Parsing and semantic validation happen before the coordinator creates staging when possible. Conversion itself receives a dedicated world context containing a complete isolated provider/store/Mediator/service graph, including relation loaders, caches, package state and migrations. No request issued by this graph can resolve live stores or live caches.

A provider-neutral staging session owns lifecycle and yields explicit states: preparing, converting, validating, ready, promoting, committed, aborted and failed-recovery-required. The session ID identifies retained diagnostics and owns temporary storage. Do not return `Success` merely because conversion ended without throwing; inspect converter errors and required validation results.

The coordinator should expose a preview/validation result before a separate explicit promotion operation. The administrator reviews counts, warnings, rewrites and import logs. A start-up import can request promotion under its existing explicit configuration contract, but it follows the same isolated conversion and validation path.

## Promotion contract

1. Refuse another active import/promotion and refuse promotion while the old `.previous` destination would be overwritten. Check available disk before writing.
2. Establish a maintenance boundary: stop new world commands, drain admitted writes and suspend game-state readers/services that could hold old objects. Define what connected clients see and how their character/session bindings are reconciled.
3. Close staging writers and validate allocators/definitions before the commit point where feasible. Check cancellation before entering the commit phase.
4. Perform the provider's directory/environment replacement under its existing gate, retaining the outgoing world separately. Once the swap starts, complete required reopen/allocator work with an internal bounded recovery policy independent of the caller's disconnected HTTP cancellation token.
5. Invalidate live per-world caches, reload definitions and reconcile long-lived services before admitting commands. Report committed only when the active world is usable. Record the commit point durably so process recovery can distinguish committed from aborted work.
6. On a post-swap failure, report the actual active-world state and keep maintenance enabled. Do not call staging abort or delete a path that is now live. Recovery must follow an explicit retained-world rollback or restart procedure.

Promotion needs its own authorization and audit event, separate from permission to upload/preview. Do not silently promote a rehearsal into a public game. Uploaded files and logs retain current access and size protections.

## Alternatives

- A fresh DI scope around the current converter is insufficient because singleton stores remain live.
- Pointing one mutable global provider at staging would redirect unrelated live requests and caches, violating isolation.
- A separate offline import host/process provides stronger structural isolation and is a valid first implementation if it can produce a verified staging world through the same provider contract. It still needs the promotion/commit/recovery protocol above.

Prefer a dedicated isolated import host for the first implementation rather than adding ambient mutable world selection to every normal request. Reuse the existing converter and store registrations through an explicit host factory; do not duplicate conversion logic.

## Implementation sequence and proof

1. Add an isolated import-host factory and lifecycle coordinator without enabling live promotion. Failure-inject conversion after real writes and verify live objects, attributes, package registry, cache entries and allocator state remain unchanged. Demonstrate the same import succeeds in staging.
2. Wire portal and start-up callers through the coordinator. Preview results/logs name the staging session; failed or cancelled conversion aborts only its own directory. Cover simultaneous requests and process cleanup/restart.
3. Harden the provider promotion primitive around its commit point and allocator/definition refresh. Test cancellation immediately before and after swap, disk-full/move/open failures, preexisting `.previous`, restart recovery and repeated promotion requests.
4. Add explicit preview/promote UI/API and maintenance/session reconciliation. Assert no command observes a mixture of old/new game state; permission-filter reports and audit promotion outcomes.
5. Rehearse representative PennMUSH, RhostMUSH and TinyMUX imports and rollback on a disposable stack. Update migration documentation only after production callers demonstrate the staging contract end to end.

Required tests use distinct live/staging sentinels and verify the active provider plus actual downstream reads after each failure point. Provider unit tests alone do not prove that the production coordinator is using staging. Keep #1969 open until portal/start-up conversion and promotion/recovery are verified.
