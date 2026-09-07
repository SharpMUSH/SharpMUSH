# PR #902 Codex Review Fix Report

Branch: `claude/pennmush-connection-announcements-66e826`
Scope: 3 findings from an automated (Codex) review of PR #902.

## Finding 1 (P1) — Hooks checked the wrong object's permission to read the attribute

**File:** `SharpMUSH.Library/Services/ConnectionAnnounceService.cs:286-287` (in `QueueHookAsync`)

**Change:**
```csharp
// before
var attrResult = await attributeService.GetAttributeAsync(
    player, owner, attrName, IAttributeService.AttributeMode.Execute, parent: true);

// after
var attrResult = await attributeService.GetAttributeAsync(
    owner, owner, attrName, IAttributeService.AttributeMode.Execute, parent: true);
```

**Why it's correct:** `GetAttributeAsync(executor, obj, ...)` runs the permission check as
`executor` reading/executing `obj`'s attribute (`AttributeService.cs:30-51`, dispatching to
`PermissionService.CanExecuteAttribute` → `CanEvalAttr` → `CanEval` for `AttributeMode.Execute`).
Passing the connecting/disconnecting `player` as executor made the check
"can `player` execute this on `owner`" — which PennMUSH's `queue_attribute_base` never does; the
hook runs as the owner's own automatic, system-triggered execution.

I independently verified the self-evaluation claim by reading
`SharpMUSH.Library/Services/PermissionService.cs:386-391`:

```csharp
public static async ValueTask<bool> CanEval(AnySharpObject evaluator, AnySharpObject evaluationTarget)
    => !await evaluationTarget.IsPriv()
         || evaluator.IsGod()
         || ((await evaluator.IsWizard()
                    || (await evaluator.IsRoyalty() && !await evaluationTarget.IsWizard()))
                && !evaluationTarget.IsGod());
```

Walking all three cases with `evaluator == evaluationTarget == owner`:
- `owner` not privileged (`!IsPriv()` true) → passes on the first clause regardless.
- `owner` is God → `evaluator.IsGod()` is true → passes on the second clause.
- `owner` is Wizard (not God) → `evaluator.IsWizard()` is true and `!evaluationTarget.IsGod()` is
  true → third clause passes.
- `owner` is Royalty only (not Wizard, not God) → `evaluator.IsRoyalty() && !evaluationTarget.IsWizard()`
  is true (target isn't Wizard) and `!IsGod()` is true → third clause passes.

So self-evaluation always returns `true`, independent of the owner's own privilege level, and
`CanExecuteAttribute` → `CanEvalAttr` → `CanEval` (`PermissionService.cs:271-279`) is satisfied for
every hook owner. `AttributeEntrySeed.cs:16,20` confirms ACONNECT/ADISCONNECT carry no `public`
attribute flag, so the old code's `CanEvalAttr` fallback (`attribute.IsPublic()`) never rescued the
mortal-connecting-player case either.

I also confirmed the scenario is not contrived: `InitialObjectSeed.cs:27-28` seeds `#8 "HTTP
Handler"` and `#9 "Event Handler"` as WIZARD-flagged Things — exactly the kind of master-room
utility object the finding describes.

**Test:**
- `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs` — updated the three existing
  `GetAttributeAsync` assertions/stubs that hard-coded `player` as the executor
  (`AnnounceConnectAsync_MasterRoomObjectsWithAconnect_AreQueued`,
  `AnnounceConnectAsync_OneMasterRoomHookThrows_LaterHookInTheSameRoomStillQueued`) to expect the
  owner object as both executor and target.
- `SharpMUSH.Tests/Commands/ConnectionAnnounceIntegrationTests.cs` —
  **`Connect_MortalPlayer_WizardOwnedZoneHookStillFires`** (new, "Test 11"): real end-to-end
  fixture (DI container, real parser, real `PermissionService`/`AttributeService`), not a mock.
  Creates a zoned room with a Thing that is explicitly `@set ... =WIZARD`, gives it an ACONNECT
  hook, then connects as an ordinary freshly-created mortal (no WIZARD/ROYALTY/powers). Asserts the
  witness in the zone room sees the hook's `@emit` output. Before the fix this test fails outright
  (the mortal's `CanEvalAttr` check against the WIZARD-flagged owner fails and the hook is silently
  skipped); after the fix it passes because the permission check now runs as the (self-privileged)
  owner.

## Finding 2 (P2) — Hidden state cleared before the disconnect handler could read it, on LOGOUT

**File:** `SharpMUSH.Library/Services/ConnectionService.cs`, `Unbind` (was lines ~103-142)

**Change:** Split the single `Unbind` state mutation into two. The first `_sessionState.AddOrUpdate`
still nulls `Ref` and sets `State = Connected` (unchanged ordering, still before the publish, per the
existing comment about `PLAYER\`DISCONNECT`'s remaining-connections count). The `Hidden` metadata
key is no longer cleared inside that same mutation. Instead, a second, separate
`_sessionState.AddOrUpdate` (and the matching `stateStore.UpdateMetadataAsync(handle, "Hidden", "0")`
call) runs **after** `await publisher.Publish(new ConnectionStateChangeNotification(...))` returns.

**Why it's correct:** `ConnectionStateEventHandler.Handle` (subscribed to
`ConnectionStateChangeNotification`, `SharpMUSH.Implementation/Handlers/ConnectionStateEventHandler.cs:84-129`)
re-fetches connection data via `connectionService.Get(notification.Handle)` and reads
`connectionData.IsHidden` for both the `PLAYER\`DISCONNECT` event's `hidden?` argument and
`ConnectionAnnounceService.AnnounceDisconnectAsync`'s `isHiddenConnection` parameter, all while the
publish (i.e. all subscriber handlers) is still in flight. `Mediator`'s `Publish` awaits every
handler before returning, so as long as `Hidden` is still present in `_sessionState` when
`publisher.Publish` is called, the handler sees the pre-logout value. Moving the clear to run after
`await publisher.Publish(...)` guarantees exactly that ordering. `ConnectionService.Disconnect`
(the QUIT path) was never affected — it already removes the handle from `_sessionState` entirely
after publishing, so `IsHidden` was always read intact there.

**Test:**
- `SharpMUSH.Tests/Commands/ConnectionAnnounceIntegrationTests.cs` —
  **`Hide_ThenLogout_BroadcastsHiddenDisconnected`** (new, "Test 12"): same shape as the existing
  QUIT-side `Hide_ThenQuit_BroadcastsHiddenDisconnected` (Test 7) but drives `LOGOUT` instead of
  `QUIT`. A WIZARD-granted player `@hide/on`s, then `LOGOUT`s; asserts the witness's room broadcast
  reads `"... has HIDDEN-disconnected."` rather than the ordinary wording. This is real end-to-end
  coverage through the actual `ConnectionStateEventHandler` subscriber, not a mock — the only way to
  actually prove the ordering bug is fixed.
- Existing test `HideCommandTests.Hide_ThenLogout_DoesNotLeakHiddenStateToTheNextLogin` still passes
  unmodified: it asserts `IsHidden` is `false` **after `Parser.CommandParse(..., "LOGOUT")` returns**,
  which is after `Unbind` fully completes (including the new post-publish clear), so the assertion
  timing is unaffected by moving the clear later within the same awaited call.

## Finding 3 (P2) — A broadcast failure skipped ADISCONNECT hooks and LASTLOGOUT

**File:** `SharpMUSH.Library/Services/ConnectionAnnounceService.cs`

**Change:** Extracted the room/inventory/channel broadcast section (previously inlined, gated on
`configuration.CurrentValue.Cosmetic.AnnounceConnects`) into a new private helper,
`BroadcastAnnouncementAsync(AnySharpObject player, string fullMessage, bool isDark)`
(`ConnectionAnnounceService.cs:161-189`), with its own try/catch that logs via the existing
`ILogger<ConnectionAnnounceService>` and does not propagate — mirroring `QueueHookAsync`'s own
per-call try/catch. Both `AnnounceConnectAsync` and `AnnounceDisconnectAsync` now call
`await BroadcastAnnouncementAsync(player, fullMessage, isDark);` in place of the inlined block, so a
broadcast failure in either method can no longer prevent the ACONNECT/ADISCONNECT hook dispatch (and,
on disconnect, `LASTLOGOUT`) that follow it. A shared helper was used (not duplicated inline) because
the two call sites were byte-for-byte identical in shape — a clean, unambiguous extraction.

Per the task instructions, this fix was applied to **both** `AnnounceConnectAsync` (connect-side,
same structural risk though Codex's finding only named the disconnect side) and
`AnnounceDisconnectAsync`.

**Test:**
- `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs` — two new unit tests, mirroring the
  existing `AnnounceConnectAsync_OneMasterRoomHookThrows_LaterHookInTheSameRoomStillQueued`-style
  fixture:
  - `AnnounceConnectAsync_BroadcastThrows_AconnectHookStillDispatched`: makes
    `communicationService.SendToRoomAsync` throw, asserts `attributeService.GetAttributeAsync` for
    the player's own ACONNECT was still called once.
  - `AnnounceDisconnectAsync_BroadcastThrows_HookAndLastLogoutStillRun`: same throw, asserts both
    the player's own ADISCONNECT lookup **and** `SetAttributeAsync(..., "LASTLOGOUT", ...)` still
    ran.

## Test-suite updates for old (buggy) assumptions

`SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs` had three `GetAttributeAsync`
call-shape assertions/stubs that hard-coded `player` as the executor argument — a direct
consequence of Finding 1's bug being baked into the pre-fix test suite. All three were updated to
use the hook owner object as both executor and target (see Finding 1 above). No other test in the
repository asserted on `Unbind`'s internal ordering (`Hidden` vs. `Ref` vs. publish), so Finding 2
needed no other test updates.

## Full test results

All commands run from
`/home/grave/RiderProjects/SharpMUSH/.claude/worktrees/sharpmush-markup-string-library-b5d03b`.

| Suite | Filter | Result |
|---|---|---|
| `ConnectionAnnounceServiceTests` | `/*/*/ConnectionAnnounceServiceTests/*` | 15/15 passed |
| `ConnectionAnnounceIntegrationTests` | `/*/*/ConnectionAnnounceIntegrationTests/*` | 12/12 passed (10 pre-existing + 2 new) |
| `HideCommandTests` | `/*/*/HideCommandTests/*` | 9/9 passed |
| `ConnectionServiceHiddenTests` | `/*/*/ConnectionServiceHiddenTests/*` | 2/2 passed |
| `ConnectionServiceReconciledHandleTests` | `/*/*/ConnectionServiceReconciledHandleTests/*` | 4/4 passed |
| `ConnectionServiceAccountModeTests` | `/*/*/ConnectionServiceAccountModeTests/*` | 11/11 passed |
| `NatsConnectionStateTests` | `/*/*/NatsConnectionStateTests/*` | 10/10 passed |
| `PresenceClassPlumbingTests` | `/*/*/PresenceClassPlumbingTests/*` | 9/9 passed |
| `SocketCommandTests` | `/*/*/SocketCommandTests/*` | 35/35 passed |

Builds:
- `dotnet build SharpMUSH.Library/SharpMUSH.Library.csproj` — 0 errors, 2 pre-existing NU1603
  warnings (unrelated to this change).
- `dotnet build SharpMUSH.Implementation/SharpMUSH.Implementation.csproj` — 0 errors, pre-existing
  MSB3277 assembly-version-conflict warnings (unrelated).
- `dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj` — 0 errors, same pre-existing warning
  classes across the solution's plugin fixture projects.

## Deviations from the task brief

- None of substance. The task suggested `SharpMUSH.Tests/Commands/HideCommandTests.cs` as a likely
  home for the Finding 2 regression test; I put it in `ConnectionAnnounceIntegrationTests.cs`
  instead, alongside the existing QUIT-side `Hide_ThenQuit_BroadcastsHiddenDisconnected`, since that
  file already owns the witness/room/`MessagesTo` fixture needed to assert on the actual broadcast
  wording (the thing Finding 2 is about), whereas `HideCommandTests.cs`'s existing LOGOUT test only
  asserts on `IsHidden` state, not on the announcement text.

## Concerns

None. Scope was held to the two named service files plus the test files that directly needed
updating as a consequence (per the task's "touch only" instruction).

I also empirically verified every new/updated test actually fails on the pre-fix code, not just in
principle: `git stash push` on the two source files (keeping the updated test files in place),
re-ran the affected suites, then `git stash pop` to restore the fix and re-ran to confirm green
again.

- `ConnectionAnnounceServiceTests`: 4 failed against pre-fix source -
  `AnnounceConnectAsync_MasterRoomObjectsWithAconnect_AreQueued`,
  `AnnounceConnectAsync_OneMasterRoomHookThrows_LaterHookInTheSameRoomStillQueued` (both Finding 1's
  updated assertions), `AnnounceConnectAsync_BroadcastThrows_AconnectHookStillDispatched`, and
  `AnnounceDisconnectAsync_BroadcastThrows_HookAndLastLogoutStillRun` (both Finding 3's new tests) -
  the other 11 passed unchanged. After restoring the fix, all 15 passed.
- `ConnectionAnnounceIntegrationTests`: 2 failed against pre-fix source -
  `Connect_MortalPlayer_WizardOwnedZoneHookStillFires` (Finding 1) and
  `Hide_ThenLogout_BroadcastsHiddenDisconnected` (Finding 2) - the other 10 passed unchanged. After
  restoring the fix, all 12 passed.
