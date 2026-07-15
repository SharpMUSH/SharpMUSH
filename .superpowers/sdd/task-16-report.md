# Task 16 Report: `@sitelock` mutation + enforcement trigger

## Implemented

Replaced the four `NotImplemented` stubs in `Commands.SiteLock` (`SharpMUSH.Implementation/Commands/WizardCommands.cs`):

- **`@sitelock/ban <pattern>`** → adds/replaces the rule with `["!connect", "!create", "!guest"]`.
- **`@sitelock/register <pattern>`** → adds/replaces the rule with `["!create", "register"]`.
- **`@sitelock/remove <pattern>`** → deletes the rule; notifies "not found" (`#-1 NO MATCH`) if it doesn't exist.
- **Generic 2-arg add `@sitelock <pattern>=<flag1> <flag2> ...`** (`args.Count == 2`, `RSArgs` only splits the RHS on commas, so a space-separated flag list stays one arg) → adds/replaces the rule with whatever flags were provided, split on whitespace.

All three "add" paths persist then call `IBanEnforcer.EnforceHostRuleAsync(pattern)` to drop matching live connections immediately; REMOVE does not (nothing to enforce on a deletion). All four notify the wizard of success/failure via two new `ErrorMessages.Notifications` format strings (`SitelockRuleAddedFormat`, `SitelockRuleRemovedFormat`, `SitelockRuleNotFound`). The four now-dead `*NotImplemented` message constants were deleted (`SitelockNameNotImplemented` was kept — `@sitelock/name` is still unimplemented, out of scope).

Two private static helpers do the read-persist-signal work, both in `WizardCommands.cs`:
- `AddSitelockRuleAsync(pattern, flags)` — merge + `SetExpandedServerData` + `SignalChange` + `EnforceHostRuleAsync`.
- `RemoveSitelockRuleAsync(pattern)` — remove-if-present + `SetExpandedServerData` + `SignalChange`; returns `false` (no persist) if the pattern wasn't present.
- `CurrentPersistedOptionsAsync()` — shared base-state helper (see finding #2 below).

**Services wiring** (`SharpMUSH.Implementation/Commands/Commands.cs`): `Database` (`ISharpDatabase`) already existed. Added two new statics + constructor params, mirroring the existing pattern exactly: `ConfigReloadService` (`ConfigurationReloadService`) and `BanEnforcer` (`IBanEnforcer`). Both types are already registered in `SharpMUSH.Server/Startup.cs` (`AddSingleton<ConfigurationReloadService>()` and `AddSingleton<IBanEnforcer>(...)`), so no DI registration changes were needed — `Commands` is resolved from that same container as `ILibraryProvider<CommandDefinition>`.

## Config-reload observability — the actual finding

The brief's "investigate and handle" flag was well-placed. Two distinct issues surfaced:

**1. Test-fixture-only staleness in `IOptionsWrapper<SharpMUSHOptions>`.** `ServerTestWebApplicationBuilderFactory.ConfigureWebHost` (`SharpMUSH.Tests/ServerTestWebApplicationBuilderFactory.cs`) does `RemoveAll<IOptionsWrapper<SharpMUSHOptions>>()` and replaces it with an NSubstitute stub whose `CurrentValue` is a **fixed snapshot captured once at test-session start** — it never reflects DB writes or `ConfigurationReloadService.SignalChange()`, no matter how many times it fires. This is session-shared (`ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)`), so it's not test-local either. `IOptionsMonitor<SharpMUSHOptions>` (the un-overridden, real, DB-reload-aware path — same one `ConfigurationControllerTests.ImportConfiguration_UpdatesOptionsMonitor` already relies on) is unaffected. **Fix**: the test file reads `IOptionsMonitor<SharpMUSHOptions>.CurrentValue` (named `Configuration` for readability) to assert post-command state, not `IOptionsWrapper`. No delay was needed — `ConfigurationReloadService.SignalChange()` → `CancellationTokenSource.Cancel()` invalidates `IOptionsMonitor`'s cache synchronously, so the very next `.CurrentValue` read recomputes via `OptionsService.Create()` (a synchronous DB read).

**2. A real (non-test-only) correctness gap this exposed.** The `SitelockController` pattern the brief said to mirror builds `newRules` from `options.CurrentValue.SitelockRules.Rules` (the wrapper). Doing the same for REMOVE surfaced a genuine bug during GREEN: seeding a rule directly via DB, then running `@sitelock/remove` against it, failed the test — the command's own `Configuration!.CurrentValue` read (frozen in the test fixture) never saw the seeded key, so `Remove()` was a no-op and nothing was deleted. Since this same staleness window (however narrow in production) can in principle affect any read-modify-write against a singleton in-memory options cache, I changed the merge-base for both `AddSitelockRuleAsync` and `RemoveSitelockRuleAsync` to `Database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions))` (falling back to `Configuration.CurrentValue` only if nothing has been persisted yet — mirrors `OptionsService.Create()`'s own default-then-persist fallback). This reads the true persisted state directly rather than trusting any cache, which is strictly more correct and fixed the REMOVE test without touching shared test infrastructure. BAN/REGISTER/2-arg-add didn't visibly need this (an unconditional "add on top of whatever base" doesn't care if the base is stale), but the fix applies uniformly since all four paths reuse the same base-state helper.

## Tests (TDD)

New file: `SharpMUSH.Tests/Commands/SitelockCommandTests.cs`, class-level `[NotInParallel]` (`SitelockRulesOptions` is one whole-object DB row — concurrent mutation-tests would clobber each other's read-modify-write). Runs commands as handle `1` (God, `#1`, has `FLAG^WIZARD` per the brief's note — no separate wizard player needed).

- `Ban_AddsConnectCreateGuestRule` — `@sitelock/ban <pattern>` → asserts `Configuration.CurrentValue.SitelockRules.Rules[pattern]` equals `["!connect","!create","!guest"]`.
- `Register_AddsCreateRegisterRule` — `@sitelock/register <pattern>` → `["!create","register"]`.
- `Remove_DeletesRule` — seeds a rule directly via DB, runs `@sitelock/remove <pattern>`, asserts the key is gone.
- `TwoArgAdd_UsesProvidedFlags` — `@sitelock <pattern>=!connect suspect` → `["!connect","suspect"]`.

Each test uses a unique `*.{GenerateUniqueName}.test` pattern (won't match the shared handle-1 connection's `"localhost"` origin, so `EnforceHostRuleAsync` never disconnects the test's own connection) and cleans up in a `finally` block via a direct-DB helper (independent of REMOVE's correctness), restoring pristine state regardless of pass/fail.

**RED**: ran before implementing — all 4 failed against the `NotImplemented` stubs (`total: 4, failed: 4`).
**GREEN (1st pass)**: implemented reading base state from `Configuration.CurrentValue` (literal `SitelockController` mirror) — `total: 4, failed: 1` (`Remove_DeletesRule`, per finding #2 above).
**GREEN (final)**: switched the merge-base to `Database.GetExpandedServerData` — `total: 4, failed: 0`.

**Regression**: `dotnet build SharpMUSH.sln` — 0 errors. `NetworkCommandTests` (6 total, 1 run/passed, 5 pre-existing `[Skip]`) — pass. `SitelockGuardTests` (4/4), `SitelockMatcherTests` + `SitelockMatcherIsBlockedTests` (19/19 combined), `ConfigurationControllerTests` (4/4), `WizardCommandTests` (37 total, 28 run/passed, rest pre-existing skips) — all pass, 0 failures.

## Files changed

- `SharpMUSH.Implementation/Commands/WizardCommands.cs` — BAN/REGISTER/REMOVE/2-arg-add implementation + two private helpers + `CurrentPersistedOptionsAsync`.
- `SharpMUSH.Implementation/Commands/Commands.cs` — added `ConfigReloadService`/`BanEnforcer` statics + ctor wiring.
- `SharpMUSH.Library/Definitions/ErrorMessages.cs` — added `SitelockRuleAddedFormat`/`SitelockRuleRemovedFormat`/`SitelockRuleNotFound`; removed four now-dead `*NotImplemented` constants.
- `SharpMUSH.Tests/Commands/SitelockCommandTests.cs` — new, 4 tests.

## Self-review

- Completeness: all three mappings correct, persist/signal/enforce present on every add path, REMOVE correctly skips enforcement, success/failure notifications on every branch. ✓
- Quality: helpers avoid duplicating the SitelockController pattern beyond what's necessary; the DB-direct-read divergence from the literal controller code is deliberate and documented inline (XML doc on `CurrentPersistedOptionsAsync`) with the reasoning above, not silent. ✓
- Testing: each mutation observed post-reload via the one live-reload-aware path the test fixture leaves intact (`IOptionsMonitor`); cleanup is unconditional and independent of the command under test; `[NotInParallel]` guards the shared config resource. Ran real DB-backed tests via Testcontainers/Podman throughout (no mocking of the persistence layer).

## Concerns

- The `IOptionsWrapper<SharpMUSHOptions>` test-fixture staleness (finding #1) is a pre-existing property of `ServerTestWebApplicationBuilderFactory` shared by every test in the session, not something this task fixes at the root — any *future* command reading `Configuration!.CurrentValue` and expecting to see another command's just-persisted change, within this same test harness, will hit the same wall unless it also reads the DB directly. Worth flagging for whoever next writes a live-reload test against this fixture.
- Did not touch `@sitelock/name` (banned-name management) — explicitly out of scope per the brief (`SitelockNameNotImplemented` untouched).
- Note: this `task-16-report.md` path already contained an unrelated prior report (`/admin/accounts` portal page, a different Phase's Task 16). This report **overwrites** that content per the current brief's instruction to write the full report to this exact path; the prior content is preserved in git history if needed.

## Podman handling

Checked `podman ps -aq | wc -l` before every DB-backed run; it was `0` each time (no stale containers to clear), and returned to `0` after each run without manual cleanup being required — one run left 2 containers momentarily mid-teardown which cleared on their own by the next check. No run hung; longest was ~27s.

## Fix Report (SitelockController read basis)

**Finding.** Task 16 made the in-game `@sitelock` command base its read-modify-write on the CURRENT PERSISTED options (`Database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions))`), but `SitelockController.AddSitelockRule`/`DeleteSitelockRule` still merged against the cached `options.CurrentValue` (`IOptionsWrapper<SharpMUSHOptions>`). With both surfaces writing the same `SetExpandedServerData(nameof(SharpMUSHOptions), …)` resource, the mismatched read bases created a lost-update race: an in-game ban could be silently reverted by a near-concurrent web-admin edit that had read a stale `CurrentValue`.

**What changed.** In `SharpMUSH.Server/Controllers/SitelockController.cs`, added a private helper `CurrentPersistedOptionsAsync()` that mirrors `WizardCommands.CurrentPersistedOptionsAsync` exactly:

```csharp
private async ValueTask<SharpMUSHOptions> CurrentPersistedOptionsAsync()
    => await database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions))
        ?? options.CurrentValue;
```

Both `AddSitelockRule` and `DeleteSitelockRule` now open their read-modify-write with `var currentOptions = await CurrentPersistedOptionsAsync();` instead of `options.CurrentValue`. Everything else is untouched: the copy-dict / mutate-one-key / `with { SitelockRules = new SitelockRulesOptions(newRules) }` / `SetExpandedServerData` / `configReloadService.SignalChange()` shape, the `IBanEnforcer.EnforceHostRuleAsync(hostPattern)` on add (and its absence on delete), the auth policy, the 400/404/500 responses, and logging. `options` remains injected purely for the null fallback and for the unchanged `GetSitelockRules` read.

**Null-fallback handling.** On a truly fresh store `GetExpandedServerData` returns null (nothing persisted yet); the helper falls back to `options.CurrentValue` so the very first rule write still succeeds — identical to the command's fallback to `Configuration!.CurrentValue`. Once anything is persisted, the DB read wins and both surfaces merge against the same persisted truth, closing the race.

WizardCommands was NOT modified (already correct); the controller was brought in line with it.

**Tests.** Added `SharpMUSH.Tests.Integration/Auth/SitelockControllerReadBasisTests.cs` (constructs `SitelockController` directly with DI-resolved collaborators, same pattern as `BanEnforcementWiringTests`):
- `DeleteSitelockRule_ReadsPersistedState_UnrelatedSeededRuleSurvives` — seeds two rules (survivor + doomed) directly via `SetExpandedServerData` (bypassing the stale `IOptionsWrapper` substitute), calls `DeleteSitelockRule(doomed)`, asserts `Ok`, then reads back via `GetExpandedServerData` and asserts the doomed key is gone AND the unrelated survivor rule persists intact — proving the merge is against persisted truth, not a stale cache lacking it.
- `AddSitelockRule_PersistsAndRoundTripsViaGetExpandedServerData` — adds a rule, asserts it round-trips through the persisted store.

**Commands / output.**
- `podman ps -aq | wc -l` → 0 before each DB-backed run (cleared self-teardown residue when it read >0).
- `dotnet build` → **0 errors** (20 pre-existing MSB3277 version-unification warnings only).
- New `SitelockControllerReadBasisTests` (filter `/*/*/SitelockControllerReadBasisTests/*`) → **total 2, failed 0, succeeded 2**.
- Regression `SitelockCommandTests` (SharpMUSH.Tests) → **Passed! total 4, failed 0**.
- Regression `AuthHttpControllerTests/AccountRegister_NewAccount_ReturnsSessionTokenAndEmptyCharacterList` (SharpMUSH.Tests.Integration) → **Passed! total 1, failed 0** (22s).
