# Session State & Nav Account Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Model the active character as real state so the nav card stops showing a stale name, replace the duplicated top-right chrome with a bottom-left account panel, and switch the terminals from reconnect to dispose+recreate.

**Architecture:** `AccountAuthService` gains `ActiveCharacter` + a change event + centralized gates — the single source of truth every consumer reads instead of re-deriving. The terminal singletons become stable facades (`TerminalServiceHost`) wrapping a swappable inner instance, so a character switch can dispose and rebuild the connection without breaking the five `@inject` sites and `MushQueryService`'s constructor capture. The nav profile card becomes a popover panel owning all account actions; the top-right `AccountChrome` is deleted.

**Tech Stack:** .NET 10, Blazor WASM, MudBlazor 9.x, TUnit (not xUnit), bUnit for components, source-generated Mediator.

**Spec:** `docs/superpowers/specs/2026-07-16-session-state-nav-panel-design.md`

## Global Constraints

- **C# files:** tabs, indent size 2. **Razor files:** spaces, indent size 4.
- `TreatWarningsAsErrors` is enabled in every project — a warning fails the build.
- Prefer `var` throughout; no `this.` qualifier.
- Services return `OneOf<T1,T2>`, never nullable, **except** where existing signatures already return nullable (`SwitchCharacterAsync` returns `string?` today — do not change it).
- Test framework is **TUnit**: `[Test]` attributes, `await Assert.That(x).IsEqualTo(y)`.
- Component tests: `dotnet run --project SharpMUSH.Tests.BUnit`. Engine tests: `dotnet run --project SharpMUSH.Tests`.
- Filter syntax: `--treenode-filter "/*/*/ClassName/*"`.
- Existing suites must stay green: 124 bUnit tests, full local run 4808/4809.
- Do **not** add `[Authorize]`, change auth schemes, or touch server projects. This plan is client-only except where noted.

---

### Task 1: `ActiveCharacter` state on `AccountAuthService`

Fixes spec defect 3 (no active-character state exists). This is the root cause of the reported bug.

**Files:**
- Modify: `SharpMUSH.Client/Services/AccountAuthService.cs`
- Modify: `SharpMUSH.Client/Services/IAccountAuthState.cs`
- Test: `SharpMUSH.Tests.BUnit/Services/AccountAuthServiceActiveCharacterTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `AccountAuthService.ActiveCharacter` → `CharacterSummary?`
  - `AccountAuthService.ActiveCharacterChanged` → `event Action?`
  - `AccountAuthService.HasCharacters` → `bool`
  - `AccountAuthService.CanUseTerminal` → `bool`
  - `AccountAuthService.SetActiveCharacter(CharacterSummary? character)` → `void`
  - Same four members on `IAccountAuthState`.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.BUnit/Services/AccountAuthServiceActiveCharacterTests.cs`.

Look at an existing test in `SharpMUSH.Tests.BUnit/` first to copy the namespace and DI/substitute setup conventions — `AccountAuthService`'s constructor takes `(IHttpClientFactory, IJSRuntime, ILogger<AccountAuthService>)`, all of which need NSubstitute substitutes.

```csharp
using NSubstitute;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

public class AccountAuthServiceActiveCharacterTests
{
	private static AccountAuthService MakeService() =>
		new(Substitute.For<IHttpClientFactory>(),
			Substitute.For<IJSRuntime>(),
			Substitute.For<ILogger<AccountAuthService>>());

	[Test]
	public async Task SetActiveCharacter_raises_ActiveCharacterChanged()
	{
		var sut = MakeService();
		var raised = 0;
		sut.ActiveCharacterChanged += () => raised++;

		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(raised).IsEqualTo(1);
		await Assert.That(sut.ActiveCharacter!.Name).IsEqualTo("Wizard");
	}

	[Test]
	public async Task SetActiveCharacter_to_same_character_does_not_re_raise()
	{
		var sut = MakeService();
		var ch = new CharacterSummary(7, 1000L, "Wizard", "");
		sut.SetActiveCharacter(ch);
		var raised = 0;
		sut.ActiveCharacterChanged += () => raised++;

		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(raised).IsEqualTo(0);
	}

	[Test]
	public async Task CanUseTerminal_is_false_without_a_session()
	{
		var sut = MakeService();
		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		// IsLoggedIn is false: AccountSessionToken was never set.
		await Assert.That(sut.CanUseTerminal).IsFalse();
	}

	[Test]
	public async Task HasCharacters_is_false_on_an_empty_roster()
	{
		var sut = MakeService();
		await Assert.That(sut.HasCharacters).IsFalse();
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AccountAuthServiceActiveCharacterTests/*"`
Expected: FAIL — build error, `'AccountAuthService' does not contain a definition for 'SetActiveCharacter'`.

- [ ] **Step 3: Add the state to `AccountAuthService`**

In `SharpMUSH.Client/Services/AccountAuthService.cs`, immediately after the `Permissions` property (~line 56):

```csharp
	/// <summary>
	/// The character this tab is currently acting as. Defaults to the first character on the
	/// roster when a session hydrates, and is reassigned by <see cref="SwitchCharacterAsync"/>.
	/// Null when the account holds no characters.
	/// </summary>
	/// <remarks>
	/// Blazor WASM gives each browser tab its own DI container, so this singleton field is
	/// already tab-scoped — two tabs may hold different active characters on one account.
	/// </remarks>
	public CharacterSummary? ActiveCharacter { get; private set; }

	/// <summary>True when the account holds at least one character.</summary>
	public bool HasCharacters => Characters.Count > 0;

	/// <summary>
	/// The single gate for "may this tab drive a terminal?". Read this instead of re-deriving
	/// the condition per component — divergent local derivations are what left the nav card
	/// showing a stale name.
	/// </summary>
	public bool CanUseTerminal => IsLoggedIn && ActiveCharacter is not null;

	/// <summary>Raised whenever <see cref="ActiveCharacter"/> changes to a different character.</summary>
	public event Action? ActiveCharacterChanged;

	/// <summary>
	/// Sets the active character and raises <see cref="ActiveCharacterChanged"/> if it actually
	/// changed. Idempotent: re-setting the same character raises nothing, so callers may set
	/// defensively without causing render storms.
	/// </summary>
	public void SetActiveCharacter(CharacterSummary? character)
	{
		if (ActiveCharacter?.DbrefNumber == character?.DbrefNumber
		    && ActiveCharacter?.CreationTime == character?.CreationTime)
			return;

		ActiveCharacter = character;
		RaiseActiveCharacterChanged();
	}

	/// <summary>
	/// Raises <see cref="ActiveCharacterChanged"/> defensively — mirrors
	/// <see cref="RaiseAuthStateChanged"/>: a subscriber's render exception must never propagate
	/// back into the caller mid-switch.
	/// </summary>
	private void RaiseActiveCharacterChanged()
	{
		try
		{
			ActiveCharacterChanged?.Invoke();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "An ActiveCharacterChanged subscriber threw; swallowed");
		}
	}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AccountAuthServiceActiveCharacterTests/*"`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Services/AccountAuthService.cs SharpMUSH.Tests.BUnit/Services/AccountAuthServiceActiveCharacterTests.cs
git commit -m "feat(client): model ActiveCharacter as state on AccountAuthService"
```

- [ ] **Step 6: Write the failing test for the hydrate default**

Append to the same test file:

```csharp
	[Test]
	public async Task SwitchCharacterAsync_without_a_session_leaves_ActiveCharacter_untouched()
	{
		var sut = MakeService();
		var before = sut.ActiveCharacter;

		var ott = await sut.SwitchCharacterAsync(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(ott).IsNull();
		await Assert.That(sut.ActiveCharacter).IsEqualTo(before);
	}
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AccountAuthServiceActiveCharacterTests/SwitchCharacterAsync_without_a_session_leaves_ActiveCharacter_untouched"`
Expected: this may already PASS — `SwitchCharacterAsync` returns null early when `AccountSessionToken is null`. That is fine: it pins the guard so Step 8 cannot regress it.

- [ ] **Step 8: Set `ActiveCharacter` on a successful switch and on hydrate**

In `SwitchCharacterAsync`, replace the success return (~line 388):

```csharp
			var result = await response.Content.ReadFromJsonAsync<SwitchCharacterResponse>();
			if (result?.Ott is null) return null;

			// The switch is authoritative for identity: every consumer reads ActiveCharacter
			// rather than deriving its own answer.
			SetActiveCharacter(character);
			return result.Ott;
```

In `InitCoreAsync`, in the `AccountSessionToken is null` branch (~line 108), null the active character alongside the other identity fields:

```csharp
			Username = null;
			MustChangePassword = false;
			Role = null;
			Permissions = [];
			SetActiveCharacter(null);
			RaiseAuthStateChanged();
			return;
```

In `LogoutAsync`, alongside the existing sessionStorage removals, add:

```csharp
		SetActiveCharacter(null);
```

- [ ] **Step 9: Default `ActiveCharacter` to the first character when the roster loads**

`Characters` is assigned in `LoginAsync`, `GetCharactersAsync`, and wherever `AccountLoginResponse` is unpacked. Rather than patching each, add a single setter used by all of them. Find every `Characters = ` assignment in `AccountAuthService.cs` and route it through:

```csharp
	/// <summary>
	/// Assigns the roster and defaults <see cref="ActiveCharacter"/> to its first entry when
	/// nothing is active yet. First-character-is-the-default is correct at hydrate; the bug this
	/// replaces was re-deriving it on every render, which froze it forever after a switch.
	/// </summary>
	private void SetCharacters(IReadOnlyList<CharacterSummary> characters)
	{
		Characters = characters;
		if (ActiveCharacter is null)
			SetActiveCharacter(characters.FirstOrDefault());
	}
```

Replace each `Characters = <expr>;` with `SetCharacters(<expr>);`. Use `grep -n 'Characters = ' SharpMUSH.Client/Services/AccountAuthService.cs` to find them all; leave the property's initializer (`= [];`) alone.

- [ ] **Step 10: Write the failing test for the default**

Append:

```csharp
	[Test]
	public async Task SetActiveCharacter_null_then_roster_default_picks_first()
	{
		var sut = MakeService();
		sut.SetActiveCharacter(new CharacterSummary(1, 1L, "First", ""));
		await Assert.That(sut.ActiveCharacter!.Name).IsEqualTo("First");

		// Switching to a different character replaces it — the default does not stick.
		sut.SetActiveCharacter(new CharacterSummary(2, 2L, "Second", ""));
		await Assert.That(sut.ActiveCharacter!.Name).IsEqualTo("Second");
	}
```

- [ ] **Step 11: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AccountAuthServiceActiveCharacterTests/*"`
Expected: PASS — 6 tests.

- [ ] **Step 12: Expose the members on `IAccountAuthState`**

In `SharpMUSH.Client/Services/IAccountAuthState.cs`, add alongside the existing `AuthStateChanged` event:

```csharp
	/// <summary>The character this tab is currently acting as, or null when none.</summary>
	AccountAuthService.CharacterSummary? ActiveCharacter { get; }

	/// <summary>True when the account holds at least one character.</summary>
	bool HasCharacters { get; }

	/// <summary>The single gate for "may this tab drive a terminal?".</summary>
	bool CanUseTerminal { get; }

	/// <summary>Raised whenever <see cref="ActiveCharacter"/> changes to a different character.</summary>
	event Action? ActiveCharacterChanged;
```

- [ ] **Step 13: Build and run the full bUnit suite**

Run: `dotnet build` then `dotnet run --project SharpMUSH.Tests.BUnit`
Expected: build clean (warnings are errors), all existing tests still pass plus the 6 new ones.

- [ ] **Step 14: Commit**

```bash
git add SharpMUSH.Client/Services/AccountAuthService.cs SharpMUSH.Client/Services/IAccountAuthState.cs SharpMUSH.Tests.BUnit/Services/AccountAuthServiceActiveCharacterTests.cs
git commit -m "feat(client): set ActiveCharacter on switch, hydrate, and logout"
```

---

### Task 2: Move `username` from `localStorage` to `sessionStorage`

Spec §7 (incidental fix). Independent of everything else — land it early while the file is fresh.

**Files:**
- Modify: `SharpMUSH.Client/Services/AccountAuthService.cs:557` (persist), `:499` (read), `LogoutAsync` (~`:504-547`)

**Interfaces:**
- Consumes: nothing. Produces: nothing (behavioral only).

- [ ] **Step 1: Change the persist call**

In `PersistSessionAsync`, change:

```csharp
		await js.InvokeVoidAsync("localStorage.setItem", UsernameKey, username);
```

to:

```csharp
		await js.InvokeVoidAsync("sessionStorage.setItem", UsernameKey, username);
```

- [ ] **Step 2: Change the read in `InitCoreAsync`**

```csharp
		Username = await js.InvokeAsync<string?>("sessionStorage.getItem", UsernameKey);
```

- [ ] **Step 3: Remove the key on logout**

In `LogoutAsync`, alongside the other `sessionStorage.removeItem` calls, add:

```csharp
		await js.InvokeVoidAsync("sessionStorage.removeItem", UsernameKey);
```

- [ ] **Step 4: Check for other readers**

Run: `grep -rn 'UsernameKey\|account.username' SharpMUSH.Client/`
Expected: only the three sites above. If a fourth exists, update it too — a mixed read/write across the two storages is worse than either.

- [ ] **Step 5: Build and test**

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: clean build, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/Services/AccountAuthService.cs
git commit -m "fix(client): store username in sessionStorage and clear it on logout

The lone localStorage outlier: with tab A on account X and tab B on account
Y, the shared key held Y, so tab A's next reload hydrated a mismatched
identity. Logout never removed it either."
```

---

### Task 3: `NavMenu` reads `ActiveCharacter`

**This is the task that fixes the reported bug.** Spec defects 1 and 2.

**Files:**
- Modify: `SharpMUSH.Client/Layout/NavMenu.razor` (`:191-229` derivations, `:231-240` subscriptions, `:294-300` dispose)
- Test: `SharpMUSH.Tests.BUnit/Layout/NavMenuActiveCharacterTests.cs` (create)

**Interfaces:**
- Consumes: `AccountAuth.ActiveCharacter`, `AccountAuth.ActiveCharacterChanged` (Task 1).
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.BUnit/Layout/NavMenuActiveCharacterTests.cs`. Copy the render/DI setup from an existing NavMenu or layout test in `SharpMUSH.Tests.BUnit/` — `NavMenu` injects `ITerminalService`, `IPlayTerminalService`, `NavigationManager`, `AccountAuthService`, an application registry, and takes a cascading `Task<AuthenticationState>`.

```csharp
	[Test]
	public async Task DisplayName_tracks_ActiveCharacter_not_roster_order()
	{
		// The regression test for the reported bug: the roster is in server order and never
		// reorders on switch, so FirstOrDefault() was a constant.
		var auth = ...;   // authenticated AccountAuthService substitute/fake, roster = [Alpha(#1), Beta(#2)]
		auth.SetActiveCharacter(new CharacterSummary(2, 2L, "Beta", ""));

		var cut = RenderNavMenu(auth, isCollapsed: false);

		await Assert.That(cut.Find(".phosphor-profile-name").TextContent).IsEqualTo("Beta");
	}

	[Test]
	public async Task Card_updates_when_ActiveCharacterChanged_fires_with_no_parent_rerender()
	{
		var auth = ...;   // roster = [Alpha(#1), Beta(#2)], active = Alpha
		var cut = RenderNavMenu(auth, isCollapsed: false);
		await Assert.That(cut.Find(".phosphor-profile-name").TextContent).IsEqualTo("Alpha");

		// No parameter change, no parent render — exactly the sibling-component situation
		// that left the card stale.
		cut.InvokeAsync(() => auth.SetActiveCharacter(new CharacterSummary(2, 2L, "Beta", "")));

		await Assert.That(cut.Find(".phosphor-profile-name").TextContent).IsEqualTo("Beta");
		await Assert.That(cut.Find(".phosphor-profile-sub").TextContent).Contains("#2");
	}

	[Test]
	public async Task Avatar_initial_tracks_ActiveCharacter()
	{
		var auth = ...;   // roster = [Alpha(#1), Beta(#2)]
		auth.SetActiveCharacter(new CharacterSummary(2, 2L, "Beta", ""));

		var cut = RenderNavMenu(auth, isCollapsed: false);

		await Assert.That(cut.Find(".phosphor-avatar").TextContent.Trim()).IsEqualTo("B");
	}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/NavMenuActiveCharacterTests/*"`
Expected: FAIL — `DisplayName_tracks_ActiveCharacter_not_roster_order` asserts "Beta" but gets "Alpha". **This failure is the reported bug reproduced.** Confirm you see exactly that before proceeding.

- [ ] **Step 3: Replace the three derivations**

In `NavMenu.razor`, replace `AvatarInitial`, `DisplayName`, and `UserTag` (`:191-229`) with:

```csharp
    private string AvatarInitial
    {
        get
        {
            var ch = AccountAuth.ActiveCharacter;
            if (ch is not null && ch.Name.Length > 0)
                return ch.Name[0].ToString().ToUpper();
            var user = AccountAuth.Username;
            return !string.IsNullOrEmpty(user) ? user[0].ToString().ToUpper() : "?";
        }
    }

    private string DisplayName => AccountAuth.ActiveCharacter?.Name ?? AccountAuth.Username ?? "DebugAdmin";

    private string UserTag
    {
        get
        {
            var ch = AccountAuth.ActiveCharacter;
            return ch is not null ? $"#{ch.DbrefNumber}" : "account";
        }
    }
```

Leave `AvatarHue` alone — it already reads `DisplayName` and inherits the fix.

- [ ] **Step 4: Subscribe to the change event**

In `OnInitialized` (`:231`), add after the existing subscriptions:

```csharp
        AccountAuth.ActiveCharacterChanged += OnActiveCharacterChanged;
```

Add the handler next to the other handlers:

```csharp
    // NavMenu is a sibling of MainLayout, not a child: no parameter flow reaches it, so it must
    // subscribe. This is the notification path whose absence left the card stale.
    private void OnActiveCharacterChanged()
        => InvokeAsync(StateHasChanged);
```

In `Dispose` (`:294`), add:

```csharp
        AccountAuth.ActiveCharacterChanged -= OnActiveCharacterChanged;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/NavMenuActiveCharacterTests/*"`
Expected: PASS — 3 tests. **The reported bug is now fixed.**

- [ ] **Step 6: Run the full bUnit suite**

Run: `dotnet run --project SharpMUSH.Tests.BUnit`
Expected: all pass. If an existing NavMenu test asserted the old first-character behavior, it was encoding the bug — update it and say so in the commit.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Client/Layout/NavMenu.razor SharpMUSH.Tests.BUnit/Layout/NavMenuActiveCharacterTests.cs
git commit -m "fix(client): nav card tracks the active character

DisplayName/AvatarInitial/UserTag read Characters.FirstOrDefault() — the
roster in server order, which switching never reorders, so it was a frozen
constant. NavMenu is also a sibling of MainLayout, so no parameter flow
reached it; it now subscribes to ActiveCharacterChanged."
```

---

### Task 4: `ITerminalService` gains `IAsyncDisposable`

Prerequisite for recreation. Spec §2.

**Files:**
- Modify: `SharpMUSH.Client/Services/ITerminalService.cs`
- Modify: `SharpMUSH.Client/Services/TerminalService.cs`
- Test: `SharpMUSH.Tests.BUnit/Services/TerminalServiceDisposalTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `ITerminalService : IAsyncDisposable` — `ValueTask DisposeAsync()`.

- [ ] **Step 1: Write the failing test**

```csharp
	[Test]
	public async Task DisposeAsync_disposes_the_websocket_client()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var sut = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>());

		await sut.DisposeAsync();

		await ws.Received(1).DisposeAsync();
	}

	[Test]
	public async Task DisposeAsync_clears_LineReceived_subscribers()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var sut = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>());
		var fired = 0;
		sut.LineReceived += _ => fired++;

		await sut.DisposeAsync();
		sut.AddSystemLine("post-dispose");

		await Assert.That(fired).IsEqualTo(0);
	}

	[Test]
	public async Task DisposeAsync_is_idempotent()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var sut = new TerminalService(ws, Substitute.For<ILogger<TerminalService>>());

		await sut.DisposeAsync();
		await sut.DisposeAsync();

		await ws.Received(1).DisposeAsync();
	}
```

If `AddSystemLine` is not public, use whatever public path raises `LineReceived`, or make the test assert on `Lines` instead.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/TerminalServiceDisposalTests/*"`
Expected: FAIL — `'TerminalService' does not contain a definition for 'DisposeAsync'`.

- [ ] **Step 3: Extend the interface**

In `ITerminalService.cs`, change the declaration:

```csharp
public interface ITerminalService : IAsyncDisposable
```

- [ ] **Step 4: Implement disposal on `TerminalService`**

`TerminalService` is `partial` and not sealed (`PlayTerminalService` derives from it), so make disposal virtual-safe. Add to `TerminalService.cs`:

```csharp
	private bool _disposed;

	/// <summary>
	/// Tears the instance down so a replacement can be built cleanly. Unsubscribes the websocket
	/// handlers wired in <see cref="ConnectAsync"/>, cancels the login wait, and disposes the
	/// send semaphore and the websocket client.
	/// </summary>
	/// <remarks>
	/// Recreation — rather than reconnection — is what makes a character switch safe: a fresh
	/// <see cref="IWebSocketClientService"/> starts with a null resume token and therefore sends
	/// hello instead of resume, so the server cannot rebind the socket to the previous
	/// character's session.
	/// </remarks>
	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;
		_disposed = true;

		UnsubscribeWebSocketHandlers();

		_loginCts?.Cancel();
		_loginCts?.Dispose();
		_loginCts = null;

		_sendSemaphore.Dispose();

		LineReceived = null;
		ConnectionStateChanged = null;

		await wsService.DisposeAsync();
		GC.SuppressFinalize(this);
	}
```

`ConnectAsync` (`:54-70`) already unsubscribes-then-resubscribes the three handlers (`HandleMessage`, `HandleStateChange`, `HandleReattached`). Extract that unsubscribe block into a private method and call it from both places:

```csharp
	private void UnsubscribeWebSocketHandlers()
	{
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		wsService.Reattached -= HandleReattached;
	}
```

Match the exact event names on `IWebSocketClientService` — read `ConnectAsync` and copy them rather than trusting these.

Because `TerminalService` uses a primary constructor, `wsService` is already in scope. `LineReceived`/`ConnectionStateChanged` are `event` members, so `= null` is only legal inside the declaring class — that is where this code lives, so it compiles.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/TerminalServiceDisposalTests/*"`
Expected: PASS — 3 tests.

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build`
Expected: clean. Adding `IAsyncDisposable` to the interface may surface warnings-as-errors at `@inject` sites that now see a disposable — if so, do **not** add `@implements IAsyncDisposable` to components; they must not dispose a service they don't own. The facade in Task 6 owns the lifetime.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Client/Services/ITerminalService.cs SharpMUSH.Client/Services/TerminalService.cs SharpMUSH.Tests.BUnit/Services/TerminalServiceDisposalTests.cs
git commit -m "feat(client): make ITerminalService async-disposable"
```

---

### Task 5: Proxying `IOobChannelStore`

`Play.razor:184` subscribes to `PlayTerminal.OobChannels.ChannelUpdated` — it captures the **store object by reference**, so a swapped inner instance would leave it listening to a dead store. The facade must hand out a stable proxy.

**Files:**
- Create: `SharpMUSH.Client/Services/OobChannelStoreProxy.cs`
- Test: `SharpMUSH.Tests.BUnit/Services/OobChannelStoreProxyTests.cs` (create)

**Interfaces:**
- Consumes: `IOobChannelStore`.
- Produces: `OobChannelStoreProxy : IOobChannelStore` with `void SetInner(IOobChannelStore inner)`.

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

public class OobChannelStoreProxyTests
{
	[Test]
	public async Task Subscriber_taken_before_a_swap_still_hears_the_new_inner()
	{
		var first = new OobChannelStore();
		var sut = new OobChannelStoreProxy();
		sut.SetInner(first);

		string? heard = null;
		sut.ChannelUpdated += p => heard = p;

		var second = new OobChannelStore();
		sut.SetInner(second);
		second.Set("room", "{}");

		await Assert.That(heard).IsEqualTo("room");
	}

	[Test]
	public async Task Old_inner_stops_reaching_subscribers_after_a_swap()
	{
		var first = new OobChannelStore();
		var sut = new OobChannelStoreProxy();
		sut.SetInner(first);

		var heard = 0;
		sut.ChannelUpdated += _ => heard++;

		sut.SetInner(new OobChannelStore());
		first.Set("room", "{}");

		await Assert.That(heard).IsEqualTo(0);
	}

	[Test]
	public async Task Get_reads_through_to_the_current_inner()
	{
		var inner = new OobChannelStore();
		inner.Set("room", "{\"a\":1}");
		var sut = new OobChannelStoreProxy();
		sut.SetInner(inner);

		await Assert.That(sut.Get("room")).IsEqualTo("{\"a\":1}");
	}
}
```

Check the concrete store's class name first: `grep -rn 'class OobChannelStore' SharpMUSH.Client/`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/OobChannelStoreProxyTests/*"`
Expected: FAIL — `OobChannelStoreProxy` does not exist.

- [ ] **Step 3: Implement the proxy**

Create `SharpMUSH.Client/Services/OobChannelStoreProxy.cs`:

```csharp
namespace SharpMUSH.Client.Services;

/// <summary>
/// A stable <see cref="IOobChannelStore"/> that forwards to a swappable inner store.
/// </summary>
/// <remarks>
/// Consumers capture the store object itself — <c>Play.razor</c> subscribes to
/// <c>PlayTerminal.OobChannels.ChannelUpdated</c> at init and holds that reference for its
/// lifetime. Handing out the inner store directly would leave those subscribers attached to a
/// dead object the moment the terminal is recreated, so the facade hands out this instead.
/// </remarks>
public sealed class OobChannelStoreProxy : IOobChannelStore
{
	private IOobChannelStore? _inner;

	public event Action<string>? ChannelUpdated;

	/// <summary>Swaps the backing store, re-pointing the forwarder without touching subscribers.</summary>
	public void SetInner(IOobChannelStore inner)
	{
		if (_inner is not null)
			_inner.ChannelUpdated -= Forward;

		_inner = inner;
		_inner.ChannelUpdated += Forward;
	}

	private void Forward(string package) => ChannelUpdated?.Invoke(package);

	public void Set(string package, string dataJson) => _inner?.Set(package, dataJson);

	public string? Get(string package) => _inner?.Get(package);

	public IReadOnlyCollection<string> Packages => _inner?.Packages ?? [];

	public void Clear() => _inner?.Clear();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/OobChannelStoreProxyTests/*"`
Expected: PASS — 3 tests.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Services/OobChannelStoreProxy.cs SharpMUSH.Tests.BUnit/Services/OobChannelStoreProxyTests.cs
git commit -m "feat(client): add a swappable OobChannelStore proxy"
```

---

### Task 6: `TerminalServiceHost` facade + DI

Spec §2. This is the load-bearing task: it makes recreation possible without touching the five `@inject` sites or `MushQueryService`'s constructor capture.

**Files:**
- Create: `SharpMUSH.Client/Services/TerminalServiceHost.cs`
- Modify: `SharpMUSH.Client/Program.cs:46-51`
- Test: `SharpMUSH.Tests.BUnit/Services/TerminalServiceHostTests.cs` (create)

**Interfaces:**
- Consumes: `ITerminalService`, `IPlayTerminalService`, `OobChannelStoreProxy` (Task 5), `DisposeAsync` (Task 4).
- Produces:
  - `TerminalServiceHost : ITerminalService` with `Task RecreateAsync()`
  - `PlayTerminalServiceHost : IPlayTerminalService` with `Task RecreateAsync()`

- [ ] **Step 1: Write the failing test**

```csharp
	[Test]
	public async Task RecreateAsync_disposes_the_previous_inner()
	{
		var first = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, Substitute.For<ITerminalService>()]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());

		await sut.RecreateAsync();

		await first.Received(1).DisposeAsync();
	}

	[Test]
	public async Task Events_re_raise_from_the_current_inner_after_a_recreate()
	{
		var first = new TerminalService(Substitute.For<IWebSocketClientService>(), Substitute.For<ILogger<TerminalService>>());
		var second = new TerminalService(Substitute.For<IWebSocketClientService>(), Substitute.For<ILogger<TerminalService>>());
		var queue = new Queue<ITerminalService>([first, second]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());

		// Subscriber taken BEFORE the recreate — the @inject sites' situation exactly.
		var seen = 0;
		sut.ConnectionStateChanged += _ => seen++;

		await sut.RecreateAsync();
		second.RaiseConnectionStateChangedForTest(true);

		await Assert.That(seen).IsEqualTo(1);
	}

	[Test]
	public async Task Calls_forward_to_the_current_inner()
	{
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, second]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());
		await sut.RecreateAsync();

		await sut.SendAsync("look");

		await second.Received(1).SendAsync("look");
		await first.DidNotReceive().SendAsync("look");
	}
```

`RaiseConnectionStateChangedForTest` does not exist — if `TerminalService` offers no public way to raise the event, use an `ITerminalService` substitute and NSubstitute's `Raise.Event<Action<bool>>(true)` instead, which is the cleaner approach anyway. Do **not** add a test-only method to production code.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/TerminalServiceHostTests/*"`
Expected: FAIL — `TerminalServiceHost` does not exist.

- [ ] **Step 3: Implement the facade**

Create `SharpMUSH.Client/Services/TerminalServiceHost.cs`:

```csharp
namespace SharpMUSH.Client.Services;

/// <summary>
/// A stable <see cref="ITerminalService"/> wrapping a swappable inner terminal, so the connection
/// can be torn down and rebuilt without any consumer re-resolving the service.
/// </summary>
/// <remarks>
/// A plain DI swap is not possible: every <c>@inject</c> resolves once at component init and holds
/// the reference for the component's lifetime, and <see cref="MushQueryService"/> constructor-captures
/// it for the app's lifetime with no re-injection path. Replacing the singleton would leave all of
/// them pointed at a dead instance. This facade keeps one object identity forever and moves the
/// churn behind it.
/// </remarks>
public class TerminalServiceHost : ITerminalService
{
	private readonly Func<ITerminalService> _factory;
	private readonly OobChannelStoreProxy _oob = new();
	private ITerminalService _inner;

	public TerminalServiceHost(Func<ITerminalService> factory)
	{
		_factory = factory;
		_inner = _factory();
		Attach(_inner);
	}

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;

	/// <summary>
	/// Disposes the current inner terminal and builds a fresh one. Subscribers keep their
	/// subscriptions to this facade and are re-pointed transparently.
	/// </summary>
	public async Task RecreateAsync()
	{
		Detach(_inner);
		await _inner.DisposeAsync();

		_inner = _factory();
		Attach(_inner);
	}

	private void Attach(ITerminalService inner)
	{
		inner.LineReceived += OnLine;
		inner.ConnectionStateChanged += OnConnectionState;
		_oob.SetInner(inner.OobChannels);
	}

	private void Detach(ITerminalService inner)
	{
		inner.LineReceived -= OnLine;
		inner.ConnectionStateChanged -= OnConnectionState;
	}

	private void OnLine(TerminalLine line) => LineReceived?.Invoke(line);
	private void OnConnectionState(bool connected) => ConnectionStateChanged?.Invoke(connected);

	public bool IsConnected => _inner.IsConnected;

	public string? ConnectedPlayerName
	{
		get => _inner.ConnectedPlayerName;
		set => _inner.ConnectedPlayerName = value;
	}

	public string? ServerUri => _inner.ServerUri;
	public long? MyPort => _inner.MyPort;
	public IReadOnlyList<TerminalLine> Lines => _inner.Lines;
	public IOobChannelStore OobChannels => _oob;

	public Task ConnectAsync(string serverUri) => _inner.ConnectAsync(serverUri);

	public Task ConnectAndLoginAsync(string serverUri, string playerName, string password, OttAuthService ottAuth)
		=> _inner.ConnectAndLoginAsync(serverUri, playerName, password, ottAuth);

	public Task ConnectWithOttAsync(string serverUri, string ott) => _inner.ConnectWithOttAsync(serverUri, ott);
	public Task DisconnectAsync() => _inner.DisconnectAsync();
	public Task SendAsync(string command) => _inner.SendAsync(command);
	public Task SendControlAsync(string controlJson) => _inner.SendControlAsync(controlJson);
	public Task InitializePortAsync() => _inner.InitializePortAsync();
	public Task<string[]> SendCommandAsync(string command, int timeoutMs = 5000)
		=> _inner.SendCommandAsync(command, timeoutMs);

	public async ValueTask DisposeAsync()
	{
		Detach(_inner);
		await _inner.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}

/// <summary>The play-terminal facade. Same swap mechanics, distinct DI identity.</summary>
public sealed class PlayTerminalServiceHost(Func<IPlayTerminalService> factory)
	: TerminalServiceHost(factory), IPlayTerminalService;
```

The `PlayTerminalServiceHost` primary constructor passes a `Func<IPlayTerminalService>` where the base wants `Func<ITerminalService>`. That is **not** covariant for delegates over a class type parameter in this position — if the compiler rejects it, wrap explicitly:

```csharp
public sealed class PlayTerminalServiceHost : TerminalServiceHost, IPlayTerminalService
{
	public PlayTerminalServiceHost(Func<IPlayTerminalService> factory)
		: base(() => factory())
	{
	}
}
```

Use whichever compiles; prefer the explicit form if in doubt.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/TerminalServiceHostTests/*"`
Expected: PASS — 3 tests.

- [ ] **Step 5: Register the facades in DI**

In `SharpMUSH.Client/Program.cs`, replace lines 46-51:

```csharp
builder.Services.AddSingleton<IWebSocketClientService, WebSocketClientService>();
builder.Services.AddSingleton<ITerminalService, TerminalService>();
// Second, independent connection for the /play page (player interactions), separate from the
// command/softcode terminal above. Both are singletons so each survives navigation.
builder.Services.AddSingleton<IPlayWebSocketClientService, PlayWebSocketClientService>();
builder.Services.AddSingleton<IPlayTerminalService, PlayTerminalService>();
```

with:

```csharp
// The registered singletons are stable FACADES. Each holds a swappable inner terminal so a
// character switch can dispose and rebuild the connection — every @inject site and
// MushQueryService's constructor capture keep pointing at the facade, which never changes identity.
// The websocket clients are transient: each recreated terminal gets a brand-new one, which is what
// clears the resume token and forces hello instead of resume.
builder.Services.AddTransient<IWebSocketClientService, WebSocketClientService>();
builder.Services.AddSingleton<ITerminalService>(sp => new TerminalServiceHost(
	() => new TerminalService(
		sp.GetRequiredService<IWebSocketClientService>(),
		sp.GetRequiredService<ILogger<TerminalService>>())));

// Second, independent connection for the /play page (player interactions), separate from the
// command/softcode terminal above. Both are singletons so each survives navigation.
builder.Services.AddTransient<IPlayWebSocketClientService, PlayWebSocketClientService>();
builder.Services.AddSingleton<IPlayTerminalService>(sp => new PlayTerminalServiceHost(
	() => new PlayTerminalService(
		sp.GetRequiredService<IPlayWebSocketClientService>(),
		sp.GetRequiredService<ILogger<TerminalService>>())));
```

`Pages/WebSocketTest.razor:4` injects `IWebSocketClientService` directly. It now gets its own transient instance rather than the terminal's shared one. Check that page still behaves — if it needs the terminal's socket, it should go through `ITerminalService` instead; note it in the commit if you change it.

- [ ] **Step 6: Build and run everything**

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: clean build, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Client/Services/TerminalServiceHost.cs SharpMUSH.Client/Program.cs SharpMUSH.Tests.BUnit/Services/TerminalServiceHostTests.cs
git commit -m "feat(client): stable terminal facades with swappable inners

A plain DI swap is impossible: @inject resolves once per component lifetime
and MushQueryService constructor-captures the service with no re-injection
path. The facade keeps one identity and moves the churn behind it."
```

---

### Task 7: Switch flow recreates both terminals

Spec §3. Fixes the resume-token rebind hazard, the stale scrollback, and the never-switched play terminal.

**Files:**
- Modify: `SharpMUSH.Client/Layout/MainLayout.razor:226-239`
- Modify: `SharpMUSH.Client/Services/AccountAuthService.cs` (`_debugOttTask` invalidation)
- Test: `SharpMUSH.Tests.BUnit/Layout/SwitchCharacterFlowTests.cs` (create)

**Interfaces:**
- Consumes: `TerminalServiceHost.RecreateAsync()` (Task 6), `SetActiveCharacter` (Task 1).
- Produces: `AccountAuthService.InvalidateDebugOtt()` → `void`.

- [ ] **Step 1: Write the failing test**

```csharp
	[Test]
	public async Task Switching_recreates_both_terminals()
	{
		// Assert that RecreateAsync ran on the command terminal AND the play terminal —
		// nothing in the codebase ever disconnected the play terminal on switch, so it stayed
		// logged in as the old character indefinitely.
	}

	[Test]
	public async Task Switching_opens_no_terminal_window()
	{
		// _terminalOpen must remain false: the switch connects in the background.
	}

	[Test]
	public async Task Switching_sets_ActiveCharacter_even_if_the_connect_fails()
	{
		// Identity commits regardless; a failed auto-login surfaces in the terminal, not as a rollback.
	}
```

Fill these in against the real `MainLayout` render setup — copy DI/cascading setup from an existing MainLayout bUnit test. If `MainLayout` is impractical to render in bUnit, extract `SwitchCharacterAsync` into a small injectable service (`CharacterSwitchService`) and test that instead; the spec's §3 does not require the logic live in the layout, and a service is the better boundary anyway.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/SwitchCharacterFlowTests/*"`
Expected: FAIL.

- [ ] **Step 3: Add debug-OTT invalidation**

In `AccountAuthService.cs`, next to `_debugOttTask`:

```csharp
	/// <summary>
	/// Drops the cached debug OTT. The token is single-use server-side but cached for the app
	/// lifetime, so a recreated terminal in dev cannot reuse it — the next caller must mint a fresh one.
	/// </summary>
	public void InvalidateDebugOtt() => _debugOttTask = null;
```

- [ ] **Step 4: Rewrite the switch flow**

Replace `MainLayout.SwitchCharacterAsync` (`:226-239`):

```csharp
    private async Task SwitchCharacterAsync(AccountAuthService.CharacterSummary character)
    {
        var token = await AccountAuth.SwitchCharacterAsync(character);
        if (token is null) return;

        // Recreate rather than reconnect. WebSocketClientService's resume token survives a
        // DisconnectAsync, so reconnecting sends a resume frame and the server may rebind the
        // socket to the PREVIOUS character's session — silently discarding the new OTT. A fresh
        // client starts with a null resume token and sends hello, which cannot rebind.
        var serverUri = Terminal.ServerUri ?? "ws://localhost:4202/ws";
        AccountAuth.InvalidateDebugOtt();

        await ((TerminalServiceHost)Terminal).RecreateAsync();
        // The play terminal was never switched before this: nothing in the codebase disconnected
        // it, so it stayed logged in as the old character. /play re-auto-starts via its own logic.
        await ((TerminalServiceHost)PlayTerminal).RecreateAsync();

        // Identity commits regardless of whether the connection succeeds; a failed auto-login
        // surfaces as a terminal error with a retry, not a rollback.
        await Terminal.ConnectWithOttAsync(serverUri, token);

        // Deliberately do NOT open the terminal drawer: switching connects in the background;
        // the toolbar toggle opens the terminal when the player wants it.
        StateHasChanged();
    }
```

The `(TerminalServiceHost)` casts are ugly. Prefer adding `Task RecreateAsync();` to `ITerminalService` so no cast is needed — do that if it doesn't force test doubles to grow awkwardly, and drop the casts here.

`_terminalPlayerName` is now dead: `AccountAuth.SwitchCharacterAsync` sets `ActiveCharacter` (Task 1). Remove the field and replace its two readers (`:73` in the `AccountChrome` binding, `:161` in `UserInitial`) with `AccountAuth.ActiveCharacter?.Name`. The `AccountChrome` binding disappears entirely in Task 10 — leave it compiling until then.

Add `@inject IPlayTerminalService PlayTerminal` to `MainLayout.razor` if it isn't already injected.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/SwitchCharacterFlowTests/*"`
Expected: PASS.

- [ ] **Step 6: Align the other switch path**

`Components/CharacterPicker.razor:108-120` is a second, independent switch path used by `GlobalTerminal.razor:65`. It sets `Terminal.ConnectedPlayerName` directly, which `MainLayout`'s path never did — the two disagree. Make it call the same flow: mint OTT, recreate, connect, and let `AccountAuth.SetActiveCharacter` own identity. Remove its direct `ConnectedPlayerName` write.

- [ ] **Step 7: Run the full suite**

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Client/Layout/MainLayout.razor SharpMUSH.Client/Components/CharacterPicker.razor SharpMUSH.Client/Services/AccountAuthService.cs SharpMUSH.Tests.BUnit/Layout/SwitchCharacterFlowTests.cs
git commit -m "fix(client): recreate both terminals on character switch

Reconnecting sent a resume frame (the resume token survives DisconnectAsync),
so the server could rebind to the old character's session and drop the new
OTT. Also switches the play terminal, which nothing ever disconnected."
```

---

### Task 8: The nav account panel

Spec §4.

**Files:**
- Modify: `SharpMUSH.Client/Layout/NavMenu.razor:121-160`
- Create: `SharpMUSH.Client/Layout/AccountPanel.razor`
- Create: `SharpMUSH.Client/Layout/AccountPanel.razor.css`
- Test: `SharpMUSH.Tests.BUnit/Layout/AccountPanelTests.cs` (create)

**Interfaces:**
- Consumes: `AccountAuth.ActiveCharacter`, `AccountAuth.Characters`, `AccountAuth.HasCharacters` (Task 1).
- Produces: `AccountPanel` component with parameters:
  - `bool IsOpen`, `EventCallback<bool> IsOpenChanged`
  - `IReadOnlyList<CharacterSummary> Characters`
  - `CharacterSummary? ActiveCharacter`
  - `EventCallback<CharacterSummary> OnSwitchCharacter`
  - `EventCallback<CharacterSummary> OnOpenInNewTab`
  - `EventCallback OnLogout`

- [ ] **Step 1: Write the failing tests**

```csharp
	[Test] public async Task Panel_is_closed_until_the_card_is_clicked() { }
	[Test] public async Task Clicking_the_card_opens_the_panel() { }
	[Test] public async Task Escape_closes_the_panel() { }
	[Test] public async Task Clicking_outside_closes_the_panel() { }
	[Test] public async Task Switch_Character_opens_the_submenu() { }
	[Test] public async Task Active_character_carries_a_checkmark() { }
	[Test] public async Task Choosing_a_character_invokes_OnSwitchCharacter() { }
	[Test] public async Task Account_Management_routes_to_slash_account() { }
	[Test] public async Task Logout_invokes_OnLogout() { }
	[Test] public async Task Panel_opens_when_the_sidebar_is_collapsed() { }
	[Test] public async Task No_characters_renders_the_NoCharacter_state_without_a_submenu() { }
```

Write each with real assertions against rendered markup — no empty bodies in the final code. The `NoCharacter` state is spec'd as deliberate-but-inert: assert the submenu is **absent**, not that a create affordance exists (that's the character-lifecycle spec).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AccountPanelTests/*"`
Expected: FAIL — `AccountPanel` does not exist.

- [ ] **Step 3: Build `AccountPanel.razor`**

A popover anchored **above** the card, overlaying content, unaffected by sidebar scroll (the Slack/Linear/Vercel pattern). Two levels: root actions, and a character submenu that slides in.

Use MudBlazor's `MudPopover` with `AnchorOrigin="Origin.TopLeft" TransformOrigin="Origin.BottomLeft"` and `Open="@IsOpen"`, plus a `MudOverlay` for outside-click dismissal. Read `AccountChrome.razor` before deleting it in Task 10 — its `MudMenu` already solves the character-list rendering and is the closest prior art in this codebase.

Root level: `⇄ Switch Character` (opens submenu), `⚙ Account Management` (`/account`), `⏻ Logout`.
Submenu: back affordance, then one row per character with a checkmark on `ActiveCharacter` and a secondary "open in new tab" icon button per row invoking `OnOpenInNewTab`.

Match the existing `phosphor-*` class conventions; put animation in `AccountPanel.razor.css` (scoped CSS, as the codebase already does for `DynamicApplication.razor.css`).

- [ ] **Step 4: Rewire the card in `NavMenu.razor`**

Replace the `<a href="/account" class="phosphor-profile-card">` (`:124`) with a `<button type="button" class="phosphor-profile-card" @onclick="TogglePanel">` carrying the same inner markup and an affordance chevron. Render `<AccountPanel ... />` inside `phosphor-profile-wrap`.

Leave the `<NotAuthorized>` branch (`:142-159`) exactly as-is — anonymous users still get the `/login` link, and there is no panel.

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AccountPanelTests/*"`
Expected: PASS.

- [ ] **Step 6: Run the full suite**

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Client/Layout/AccountPanel.razor SharpMUSH.Client/Layout/AccountPanel.razor.css SharpMUSH.Client/Layout/NavMenu.razor SharpMUSH.Tests.BUnit/Layout/AccountPanelTests.cs
git commit -m "feat(client): account panel popover on the nav profile card"
```

---

### Task 9: Open a new tab as a different character

Spec §5.

**Files:**
- Modify: `SharpMUSH.Client/Layout/NavMenu.razor` (wire `OnOpenInNewTab`)
- Modify: `SharpMUSH.Client/Layout/MainLayout.razor` (consume `?as=`)
- Test: `SharpMUSH.Tests.BUnit/Layout/NewTabCharacterTests.cs` (create)

**Interfaces:**
- Consumes: `AccountPanel.OnOpenInNewTab` (Task 8).
- Produces: nothing.

- [ ] **Step 1: Write the failing tests**

```csharp
	[Test] public async Task OnOpenInNewTab_calls_window_open_with_the_as_hint() { }
	[Test] public async Task An_as_hint_matching_a_character_sets_it_active() { }
	[Test] public async Task An_as_hint_is_stripped_from_the_url_after_consumption() { }
	[Test] public async Task An_as_hint_naming_an_unowned_character_is_ignored() { }
```

Assert `window.open` via the bUnit `JSInterop` mock: `Context.JSInterop.SetupVoid("open", ...)`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/NewTabCharacterTests/*"`
Expected: FAIL.

- [ ] **Step 3: Implement the opener**

In `NavMenu.razor`:

```csharp
    // A new tab inherits a COPY of this tab's sessionStorage — including the opener's active
    // character — so the target must be passed explicitly. The hint is an entry parameter only:
    // switching never touches the URL.
    private async Task OpenCharacterInNewTabAsync(AccountAuthService.CharacterSummary character)
        => await JS.InvokeVoidAsync("open", $"/?as={character.DbrefNumber}-{character.CreationTime}", "_blank");
```

Inject `IJSRuntime JS` if not already present. The key is `{DbrefNumber}-{CreationTime}` — a dbref alone is ambiguous across a recycled object, and both halves are what `SwitchCharacterRequest` already needs.

- [ ] **Step 4: Implement the consumer**

In `MainLayout.OnInitializedAsync`, after `await AccountAuth.InitAsync()` and after the roster is loaded:

```csharp
        await ConsumeCharacterHintAsync();
```

```csharp
    /// <summary>
    /// Consumes a one-shot <c>?as=&lt;dbref&gt;-&lt;creationTime&gt;</c> entry hint written by
    /// "open in new tab", then strips it from the URL. A hint naming a character this account does
    /// not own is ignored — the roster is the authority, and the server would reject it anyway.
    /// </summary>
    private async Task ConsumeCharacterHintAsync()
    {
        var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
        var hint = System.Web.HttpUtility.ParseQueryString(uri.Query)["as"];
        if (string.IsNullOrEmpty(hint)) return;

        // Strip first: the hint must not survive a reload or be re-consumed.
        NavigationManager.NavigateTo(uri.GetLeftPart(UriPartial.Path), replace: true);

        var parts = hint.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var dbref)
            || !long.TryParse(parts[1], out var creationTime))
            return;

        var target = AccountAuth.Characters
            .FirstOrDefault(c => c.DbrefNumber == dbref && c.CreationTime == creationTime);
        if (target is null) return;

        await SwitchCharacterAsync(target);
    }
```

If `System.Web.HttpUtility` isn't available in WASM, use `Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)` — check which the codebase already uses before adding a package reference.

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/NewTabCharacterTests/*"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/Layout/NavMenu.razor SharpMUSH.Client/Layout/MainLayout.razor SharpMUSH.Tests.BUnit/Layout/NewTabCharacterTests.cs
git commit -m "feat(client): open a new tab as a different character"
```

---

### Task 10: Delete `AccountChrome`

Spec §6. Last, so the panel is proven before the old surface goes.

**Files:**
- Delete: `SharpMUSH.Client/Layout/AccountChrome.razor`
- Modify: `SharpMUSH.Client/Layout/MainLayout.razor:71-78`
- Delete: any `AccountChrome` bUnit test file

**Interfaces:**
- Consumes: a working `AccountPanel` (Task 8). Produces: nothing.

- [ ] **Step 1: Find every reference**

Run: `grep -rn 'AccountChrome' --include='*.razor' --include='*.cs' .`
Expected: `MainLayout.razor:71-78`, the component itself, possibly a test.

- [ ] **Step 2: Remove the usage**

Delete the `<AccountChrome ... />` block at `MainLayout.razor:71-78`. Keep the `phosphor-topbar-divider` only if the top bar still has something to its right; if the divider now dangles at the end, remove it too.

Remove `UserInitial` (`:158-164`) and `_isDebugAuth` if nothing else reads them — `grep -n 'UserInitial\|_isDebugAuth' SharpMUSH.Client/Layout/MainLayout.razor` first. Do not remove `_isDebugAuth` if the debug auto-connect logic (`:183-195`) still uses it.

- [ ] **Step 3: Delete the component and its test**

```bash
git rm SharpMUSH.Client/Layout/AccountChrome.razor
```

Delete its bUnit test file if one exists.

- [ ] **Step 4: Build and run everything**

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: clean build, all tests pass. A build error naming `AccountChrome` means a reference was missed.

- [ ] **Step 5: Verify in the real app**

Run the server and connect a browser. Confirm: top-right holds no auth UI; the bottom-left card shows the active character; clicking it opens the panel; switching updates **both** the card and the terminal; "open in new tab" lands a second tab on a different character with the first tab unaffected.

This is the acceptance check for the whole plan — the `/verify` skill covers driving the app if you need it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(client): delete AccountChrome, bottom-left owns account UI

Top-right is the convention for websites; bottom-left is the convention for
app shells with a persistent left rail. The portal is an app shell, and a
second surface could disagree with the first."
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| §1 State model (`ActiveCharacter`, event, gates) | 1 |
| §2 Terminal facade + recreation | 4, 5, 6 |
| §3 Switch flow (both hosts, no window, debug OTT) | 7 |
| §4 Nav panel | 8 |
| §5 New tab | 9 |
| §6 Removals (`AccountChrome`, `_terminalPlayerName`) | 7 (field), 10 (component) |
| §7 Incidental username fix | 2 |
| Known gap: `GOING` characters listed | *None — deliberately deferred to the character-lifecycle spec.* |

**Type consistency:** `SetActiveCharacter(CharacterSummary?)`, `ActiveCharacterChanged` (`Action?`), `HasCharacters`/`CanUseTerminal` (`bool`), `RecreateAsync()` (`Task`), `InvalidateDebugOtt()` (`void`), `OobChannelStoreProxy.SetInner(IOobChannelStore)` — used consistently across tasks 1, 3, 6, 7.

**Known soft spots, flagged rather than hidden:**
- Task 6's `PlayTerminalServiceHost` delegate variance may not compile as a primary constructor; the explicit fallback is given.
- Task 7's `(TerminalServiceHost)` casts are noted as undesirable with the preferred alternative (`RecreateAsync` on the interface).
- Task 4's `UnsubscribeWebSocketHandlers` uses event names that must be read off the real `ConnectAsync` rather than trusted from this plan.
- Tasks 7, 8, 9 have test names without full bodies where the render harness must be copied from existing tests. Write real assertions — an empty test body is a plan failure carried into code.
