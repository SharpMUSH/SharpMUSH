# Session State & Nav Account Panel

**Date:** 2026-07-16
**Status:** Approved, ready for planning

## Problem

Two reported defects share one root cause.

1. Switching characters updates the top-right chrome and the available options, but the bottom-left nav profile card keeps showing the old name.
2. Login/character status is duplicated across the top-right and bottom-left, and the bottom-left is only a link to `/account` rather than a menu.

Investigation showed (1) is not a refresh bug. It is three compounding defects:

- **No notification path.** `MainLayout` owns the active character as a private field `_terminalPlayerName` and calls `StateHasChanged()`, re-rendering its own subtree and pushing fresh parameters into `AccountChrome`. `NavMenu` is a sibling, not a child, so nothing reaches it. `NavMenu` subscribes only to terminal connection events and `LocationChanged` (NavMenu.razor:231-240), never to auth state.
- **Wrong source even if it re-rendered.** `NavMenu.DisplayName` reads `AccountAuth.Characters.FirstOrDefault()` (NavMenu.razor:213-220) — the account roster in server order, which switching never reorders. `AvatarInitial` (:195) and `UserTag` (:225) share the bug. This is a correct *default* that was never updated after init.
- **No active-character state exists.** `AccountAuthService` exposes `Username`, `Characters`, `Role`, `Permissions` — no `ActiveCharacter`. The active identity lives only as `MainLayout._terminalPlayerName` and, on the other switch path, as `ITerminalService.ConnectedPlayerName`. The paths disagree: `CharacterPicker` sets `ConnectedPlayerName` (CharacterPicker.razor:119), `MainLayout.SwitchCharacterAsync` does not.

The `AuthorizeView` gate on the card (NavMenu.razor:121) *does* update on login/logout, because that path raises a real event. Only character identity is unmodeled — so every consumer improvises its own guess.

Two further defects surfaced during exploration and are in scope because the fix touches the same code:

- **Session-rebind hazard.** `WebSocketClientService._resumeToken`/`_lastSeq` survive `DisconnectAsync`. The reconnect in `MainLayout.SwitchCharacterAsync` therefore sends a **resume** frame rather than hello, so the server may rebind to the *old character's* session. `HandleReattached` (TerminalService.cs:325-329) then cancels the login wait and the freshly minted `connect token <newOtt>` can be ignored entirely.
- **Play terminal never switches.** Nothing in the codebase ever calls `PlayTerminal.DisconnectAsync()`/`ConnectWithOttAsync`. After a character switch the play terminal remains logged in as the old character indefinitely.

Also: `TerminalService._lines` (scrollback) is never cleared, so the old character's output survives a switch; and `_myPort` is stale between disconnect and `InitializePortAsync` completing, so `SendCommandAsync` can route end-markers to the old port.

## Constraints discovered

These shape the design and were verified against the code:

- **The client is already tab-scoped by design.** Session token, role, permissions, and `mustChangePassword` live in `sessionStorage` (AccountAuthService.cs:18-23, :549-569), with comments stating the intent (`:104-107`: "a returning user in a new tab would otherwise get a phantom identity"). In Blazor WASM each tab is its own runtime and DI container, so `Singleton` is already per-tab.
- **The server has no "current character" concept.** `AuthController.SwitchCharacterAsync` (AuthController.cs:166-196) performs **no writes** — it validates account→character linkage and mints a 60s single-use OTT bound to a DBRef. Switching in tab A cannot affect tab B.
- **Multi-connection is supported.** `ConnectionService` is keyed by handle, not DBRef; `Get(DBRef)` returns all matching connections. `SocketCommands.cs:497` counts connections purely to report them, per PennMUSH `player\`connect`. Two characters on one account, simultaneously connected, is expected behavior. `MaxLogins` (120) is a global cap.
- **Logout is per-token.** `AccountController` revokes only the presented bearer token, so logging out in tab A leaves tab B alive.
- **New tabs inherit a copy of `sessionStorage`** when opened via `window.open`/`target=_blank` — including, critically, the opener's active character.

Consequence: **no server changes are required.**

One genuine defect in the same area: `username` is the lone `localStorage` outlier (AccountAuthService.cs:557, :499) and `LogoutAsync` never removes it. Benign for same-account switching, in blast radius, two-line fix.

## Non-goals

- **New-user registration and character creation.** The `NoCharacters` state is named and rendered deliberately here; what it should *offer* is deferred to its own spec, to be brainstormed immediately after this one. That surface has its own open questions (name legality and collision arbitration, in-portal HTTP vs in-game `@pcreate`, quota/permission gates, interaction with the #691 unclaimed-admin bootstrap and first-run wizard) that available exploration does not answer.
- **Character in the URL.** Switching stays pure state. The new-tab query hint is an entry hint only, not routed state.

## Design

### 1. State model on `AccountAuthService`

Extend the existing service rather than introducing a new one — it already owns username, characters, role, and permissions, so active character sits naturally beside them.

Add:

- `ActiveCharacter` (nullable `CharacterSummary`) — initialized to `Characters.FirstOrDefault()` when the session hydrates, reassigned by `SwitchCharacterAsync`.
- `event Action? ActiveCharacterChanged`.
- Computed gates: `HasCharacters`, `CanUseTerminal => IsLoggedIn && ActiveCharacter is not null`.

The gates live **on the service**, not in components. Components re-deriving their own answers is the failure mode being fixed; a single computed gate is the point of the change.

The resulting states:

| State | Condition |
|---|---|
| Logged out | `!IsLoggedIn` |
| No characters | `IsLoggedIn && !HasCharacters` |
| Active | `IsLoggedIn && ActiveCharacter is not null` |
| Terminal / Play status | Meaningful only under **Active**; gated by `CanUseTerminal` |

`NavMenu` subscribes to `ActiveCharacterChanged` alongside its existing terminal subscriptions (disposing symmetrically at :296-298) and drops the `FirstOrDefault()` derivations in `DisplayName`, `AvatarInitial`, and `UserTag` in favor of `ActiveCharacter`.

`MainLayout._terminalPlayerName` and `CharacterPicker`'s `ConnectedPlayerName` write both collapse into this single source of truth.

### 2. Terminal facade and re-creation

Dispose-and-recreate is not feasible against the current DI shape: `MushQueryService` constructor-captures `ITerminalService` and is itself a singleton (Program.cs:52) with no re-injection path; five components hold `@inject`ed references; `Play.razor:184` captures the nested `OobChannels` store *by reference*. Replacing a singleton would leave all of them pointing at a dead instance.

Introduce stable facades as the registered singletons:

- `TerminalServiceHost : ITerminalService`, `PlayTerminalServiceHost : IPlayTerminalService`
- Each wraps a swappable inner service, re-raising `LineReceived`/`ConnectionStateChanged` from the current inner.
- Each exposes a stable proxying `IOobChannelStore`, since `Play.razor` captures it by reference.
- `RecreateAsync()` disposes the inner and constructs a fresh one.

`ITerminalService` gains `IAsyncDisposable`. The inner's disposal must: unsubscribe its three websocket handlers (TerminalService.cs:60-65), cancel and dispose `_loginCts`, dispose `_sendSemaphore`, dispose the websocket client, and clear `LineReceived`/`ConnectionStateChanged`.

All six existing consumers keep working unchanged. `GlobalTerminal`'s existing `TerminalOverride` seam (:198/:222) already accommodates the two-instance shape.

Re-creation — rather than the current reconnect — is what fixes the resume-token rebind hazard: a fresh `WebSocketClientService` starts with a null `_resumeToken` and sends hello, so it cannot bind to the old character's session. It also gives scrollback (`_lines`) and `_myPort` a natural reset point.

### 3. Switch flow

1. Mint OTT via `AccountAuth.SwitchCharacterAsync` (unchanged server call).
2. `RecreateAsync()` on **both** hosts — including play, which today is never touched.
3. Set `ActiveCharacter`; raise `ActiveCharacterChanged`.
4. Auto-connect the command terminal in the background with the new OTT. **No window opens.**
5. The play terminal is recreated and **left disconnected**.

> **Correction (2026-07-16, during implementation).** This step originally read "Play reconnects only through `/play`'s existing auto-start logic, and only if the user is on that page." **That path does not exist.** The play terminal's only auto-start is `GlobalTerminal.OnInitializedAsync`, which runs at component init and never again; `GlobalTerminal` exposes no reconnect entry point. So a recreated play terminal stays disconnected until the component re-initializes.
>
> Accepted deliberately rather than fixed here: after a switch the play terminal shows as disconnected, and the player reconnects by navigating to `/play` afresh or reloading. `TerminalServiceHost.RecreateAsync` announces the disconnection to facade subscribers so the UI reports the truth instead of accepting input into a dead socket.
>
> This remains strictly better than the pre-change behavior, where the play terminal was never switched at all and stayed logged in as the **old character** indefinitely. Giving `/play` a real reconnect entry point is follow-up work.

Open terminal windows close on switch; none reopen. The connection is still established — "close all, open none" applies to windows, not the socket.

**Failure handling:** identity commits to the new character regardless. A failed auto-login surfaces as a connection error in the terminal with a retry, not a rollback. The nav card reads the new character.

**Knock-on:** `AccountAuthService._debugOttTask` (:71, :261) caches a single-use OTT for the app lifetime. A recreated terminal in dev cannot reuse it — null `_debugOttTask` on recreate, or route the dev path through `GetOttForCharacterAsync`.

### 4. Nav account panel

The profile card (NavMenu.razor:121-160) stops being `<a href="/account">` and becomes a button toggling a popover **anchored above it**, overlaying content and staying put when the sidebar scrolls (the Slack/Linear/Vercel pattern):

```
┌──────────────────────┐
│ ⇄ Switch Character   │
│ ⚙ Account Management │
│ ⏻ Logout             │
└──────────────────────┘
 ┌─────────────────────┐
 │ ◯ Wizard      ▴     │  ← the card
 └─────────────────────┘
```

- **Switch Character** opens a submenu *within the same panel* (second level, slides in). Active character carries a checkmark. Each row carries a secondary "open in new tab" affordance.
- **Account Management** routes to `/account`.
- **Logout** calls the existing logout path.
- Panel animates out of the card. Dismisses on outside click and on `Escape`.
- Collapsed-sidebar state (`IsCollapsed`) must still open the panel correctly.

Under **No characters**, the card renders a deliberate "No character" state and the submenu is absent. Behavior deferred (see Non-goals).

### 5. New tab as a different character

Each character row offers "open in new tab" → `window.open("/?as=<characterKey>")` (portal home).

The new tab inherits a copy of `sessionStorage`, so it already holds the account session token — but it also inherits the **opener's** active character, which is why the target must be passed explicitly. On load, the new tab consumes `?as=`, resolves the character, sets `ActiveCharacter`, mints its own OTT, auto-connects, and strips the parameter from the URL via a replace-style navigation.

A query hint is preferred over a `sessionStorage` handoff key: the latter is left behind in the opener's storage too and needs clearing on both sides. The hint has no such coupling. Switching itself never touches the URL.

### 6. Removals

- Delete `Layout/AccountChrome.razor` and its usage at `MainLayout.razor:71-78`.
- Delete `MainLayout._terminalPlayerName`.
- Top-right holds no auth UI — no name, no avatar, no login/logout button.

Rationale: top-right is the convention for *websites*; bottom-left is the convention for *app shells* with a persistent left rail (Slack, Discord, VS Code, Linear, Notion, Vercel, Supabase). The portal is an app shell. A top-right logout button would re-create the duplication this change removes, and give two surfaces that can disagree.

### 7. Incidental fix

Move `UsernameKey` from `localStorage` to `sessionStorage` (AccountAuthService.cs:557, :499) and remove it in `LogoutAsync` (:504-547).

## Testing

- **`AccountAuthService`:** `ActiveCharacter` defaults to first character on hydrate; `SwitchCharacterAsync` reassigns it and raises `ActiveCharacterChanged`; gates return correctly across all four states; `_debugOttTask` invalidation.
- **`NavMenu` (bUnit):** name/avatar/tag track `ActiveCharacter`, not roster order — the direct regression test for the reported bug; card updates on `ActiveCharacterChanged` without a parent re-render; `NoCharacters` renders the deliberate state; subscriptions disposed.
- **Panel (bUnit):** opens/closes, submenu navigation, checkmark on active, outside-click and `Escape` dismissal, collapsed-sidebar behavior, each action invokes its handler.
- **Facade:** events re-raise from the current inner across a recreate; `IOobChannelStore` proxy survives a recreate (guards the `Play.razor` capture); `RecreateAsync` disposes the old inner; a recreated service sends hello, not resume — the direct regression test for the rebind hazard.
- **Switch flow:** both hosts recreated; scrollback cleared; no window opens; failed auto-login leaves identity committed and surfaces an error.
- **Persistence:** username round-trips through `sessionStorage` and is cleared on logout.

Existing suites must stay green: the 124 bUnit tests from #691 and the full local run (4808/4809). Per project convention, integration/Explicit suites run under Podman — clear stale containers first.

## Known gaps shipped by this spec

Accepted deliberately, fixed in the character-lifecycle spec that follows:

- **The switcher lists nuked characters.** `@destroy`/`@nuke` only flag `GOING`/`GOING_TWICE` — nothing is ever deleted (`BuildingCommands.cs:451`: actual deletion "requires a garbage collection system"), and `GetCharactersForAccountAsync` (ArangoDatabase.Accounts.cs:161) does not filter `GOING`. Nuke also never unlinks the account edge — `UnlinkCharacterFromAccountAsync` is called only from `AccountController.cs:168` and `AdminAccountsController.cs:121`. So a nuked character remains listed and selectable in the panel built here. This spec does not make it worse; it does surface it more prominently.
- **`NoCharacters` renders but offers nothing.** By design — see Non-goals.

## Open questions

None. Character lifecycle and account protection are explicitly deferred to follow-up specs.
