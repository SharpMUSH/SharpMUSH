# PennMUSH-Compatible Connection Announcements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a player connects, disconnects, opens an additional connection ("reconnects"), or closes one of several open connections ("partially disconnects"), SharpMUSH must announce it exactly like PennMUSH does: a room/inventory message, a `HEAR_CONNECT`-flagged broadcast, a `SUSPECT` broadcast to wizards, `ACONNECT`/`ADISCONNECT` attribute hooks fired on the player/room/zone/master-room, channel announcements to every non-Quiet channel the player belongs to, and — when the connection is HIDDEN (via `@hide` or the `cd`/`cv`/`ch` login words) — the PennMUSH `"HIDDEN-connected."`/`"HIDDEN-disconnected."` wording instead of the normal one.

**Architecture:** A new `IConnectionAnnounceService` encapsulates the whole `announce_connect`/`announce_disconnect` port (PennMUSH `src/bsd.c:5906-6168`). It is called from the two places that already know the player's connection count — `Commands.CompletePlayerLoginAsync` (connect) and `ConnectionStateEventHandler.Handle` (disconnect) — reusing `ICommunicationService.SendToRoomAsync` for room/inventory messages, extending `IGameBroadcastService` with a two-flag-group broadcast for the `HEAR_CONNECT` case, reusing the `AttributeService.GetAttributeAsync` + fresh-`ParserState`-push pattern already established in `EventService.TriggerEventAsync` to run `ACONNECT`/`ADISCONNECT` with `%0`/`%1` bound, and publishing `ChannelMessageNotification` for the channel side. A new per-connection `Hidden` flag lives in `IConnectionService.ConnectionData.Metadata` (the codebase's existing convention for connection-scoped state — see `TerminalType`/`ColorStyle`), toggled by a rewritten `@HIDE` command and by three new `cd`/`cv`/`ch` login-screen command aliases alongside `CONNECT`.

**Tech Stack:** .NET 10, TUnit tests, NSubstitute mocks, Mediator (source-generated), `SharpMUSH.Configuration` strongly-typed options.

**Spec:** PennMUSH `src/bsd.c` functions `announce_connect` (line 5906) and `announce_disconnect` (line 6019), and `flag_broadcast` (`src/notify.c:1706`), read from the vendored PennMUSH checkout at `/home/grave/RiderProjects/SharpMUSH/pennmush` (gitignored, not part of this repo — this plan transcribes the relevant behavior inline so no access to that checkout is required to execute it).

## Global Constraints

- Match PennMUSH wording verbatim: `"<Name> has connected."` / `"<Name> has reconnected."` (connectionCount > 1) / `"<Name> has disconnected."` / `"<Name> has partially disconnected."` (remainingConnections > 0).
- `ACONNECT`/`ADISCONNECT` hooks fire **unconditionally** (not gated by the `AnnounceConnects` config option) on: the player, the player's room (only if `RoomConnects` config is true and the room is a Room or Thing), the zone (if any), and every object in the master room. This matches PennMUSH exactly — `ANNOUNCE_CONNECTS`/`ROOM_CONNECTS` only gate broadcasts, not hooks (room hook is the one exception: it additionally requires `ROOM_CONNECTS`).
- Room/inventory broadcast messages are gated by the `Cosmetic.AnnounceConnects` config option; the **room** part (not inventory) is additionally suppressed when the player has the `DARK` flag.
- The `HEAR_CONNECT` flag broadcast and the `SUSPECT`/`WIZARD` broadcast are **unconditional** of `AnnounceConnects`.
- C# style: tabs, indent size 2 (enforced by `dotnet format` gate — see CLAUDE.md). Interfaces in `SharpMUSH.Library.Services.Interfaces`, implementations in `SharpMUSH.Library.Services`, matching `IGameBroadcastService`/`GameBroadcastService`.
- Test style: TUnit (`[Test]`, `[Arguments]`), NSubstitute for mocks. Pure-unit tests `new` the class under test with substituted collaborators (see `SharpMUSH.Tests/Services/NotifyServiceTests.cs`); integration tests use the real DI container via `ServerWebAppFactory` and assert with `NotifyService.Received(n)...` (see `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs`).
- **Hidden connections (in scope — PennMUSH `src/bsd.c` `DESC.hide`, `Can_Hide`/`hide_player`/the `cd`/`cv`/`ch` login words):** a per-*connection* boolean, independent of the object-level `DARK` flag, stored as `ConnectionData.Metadata["Hidden"] = "1"/"0"` (the codebase's established per-connection state convention — see `TerminalType`/`ColorStyle` in `SocketDescriptorCommands.cs`), not a new record field. Permission to become hidden is `AnySharpObject.CanHide()` (`HelperFunctions.cs:310-311`, already implemented: `HasPower("Hide") || IsPriv()`). Wording: when the connecting/disconnecting connection is hidden, use `"HIDDEN-connected."`/`"HIDDEN-reconnected."`/`"HIDDEN-disconnected."`/`"has partially HIDDEN-disconnected."` in place of the normal four strings (PennMUSH `bsd.c:5931-5936,6131-6138`).
- **Channel-connect announcements (in scope — PennMUSH `chat_player_announce`, `src/extchat.c:3164-3171` on down):** for every channel returned by `GetOnChannelQuery(player)` that does **not** have the `"Quiet"` channel privilege, publish the same wording string used for the room broadcast via `Mediator.Publish(new ChannelMessageNotification(...))`, gated by the same `Cosmetic.AnnounceConnects` config flag as the room/inventory broadcast (PennMUSH gates `chat_player_announce` identically — `bsd.c:5950-5951` on connect, inside the `ANNOUNCE_CONNECTS` block at `:6142-6149` on disconnect). PennMUSH's per-viewer `CB_SEEALL` gate (hidden connects show only to privileged channel members) is **not** replicated — `ChannelMessageRequestHandler` has no such per-member privilege check today, and adding one is out of scope for this plan; documented as a follow-up in Task 13.
- The extra PennMUSH disconnect PE-registers `%2`-`%5` (byte/command counts) are skipped for `ADISCONNECT`/`ACONNECT` — only `%0` (unused, matches PennMUSH) and `%1` (connection count) are bound.
- Explicitly **out of scope** for this plan: fixing every other DARK-keyed visibility site (`lwho()`, `lwhoid()`, the `w`/`x`/`z`/`n`/`m`-`who` softcode function family) for Hidden-awareness beyond `WHO` and `hidden()` themselves — those two are fixed in Task 12 because they're the most user-visible and because `hidden()` would otherwise actively lie about the new state; the rest route through `PermissionService.CanSee`, which this plan deliberately does **not** touch (see Task 12 rationale — `CanSee` is a general room-perception check, not a connected-list check, and conflating the two would be a semantic regression, not a fix).

---

## File Structure

- **Create** `SharpMUSH.Library/Services/Interfaces/IConnectionAnnounceService.cs` — the two-method interface.
- **Create** `SharpMUSH.Library/Services/ConnectionAnnounceService.cs` — the port of `announce_connect`/`announce_disconnect`.
- **Modify** `SharpMUSH.Library/Services/Interfaces/IGameBroadcastService.cs` — add the two-flag-group overload.
- **Modify** `SharpMUSH.Library/Services/GameBroadcastService.cs` — implement it.
- **Modify** `SharpMUSH.Library/Definitions/ErrorMessages.cs` — add one missing constant (`GameSuspectActivity`); the four connect/disconnect wording constants already exist (lines 540-543) but are currently unreferenced.
- **Modify** `SharpMUSH.Implementation/Commands/SocketCommands.cs` — call `AnnounceConnectAsync` from `CompletePlayerLoginAsync`.
- **Modify** `SharpMUSH.Implementation/Handlers/ConnectionStateEventHandler.cs` — call `AnnounceDisconnectAsync` from the disconnect branch.
- **Modify** `SharpMUSH.Server/Startup.cs` — register `IConnectionAnnounceService` as a singleton next to `IGameBroadcastService`.
- **Create** `SharpMUSH.Tests/Services/GameBroadcastServiceTests.cs` — unit tests for the new overload.
- **Create** `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs` — unit tests for the new service.
- **Modify** `SharpMUSH.Library/Services/Interfaces/IConnectionService.cs` — add the `Hidden` metadata-backed computed property to `ConnectionData`, and an `IsPlayerHiddenAsync`-style aggregate helper.
- **Modify** `SharpMUSH.Library/Services/ConnectionService.cs` — nothing structural; `Update(handle, "Hidden", ...)` already works via the existing generic metadata setter.
- **Modify** `SharpMUSH.Implementation/Commands/WizardCommands.cs` — rewrite `@HIDE` to toggle connection-level `Hidden` (permission-gated by `CanHide()`) instead of the `DARK` flag.
- **Modify** `SharpMUSH.Implementation/Commands/SocketCommands.cs` — extract `Connect`'s body into a shared `ConnectCoreAsync(parser, mode)`; add `CD`/`CV`/`CH` command methods; update `WHO`'s per-row filtering for connection-level `Hidden`.
- **Modify** `SharpMUSH.Implementation/Functions/ConnectionFunctions.cs` — update `hidden()` to consult connection-level `Hidden`, not just the `DARK` flag.
- **Modify** `SharpMUSH.Library/Services/ConnectionAnnounceService.cs` (already created in Task 3) — extended in Task 12 (HIDDEN- wording) and Task 13 (channel announcements).
- **Create** `SharpMUSH.Tests/Services/ConnectionServiceHiddenTests.cs` — unit tests for the new `Hidden` metadata helpers.
- **Create** `SharpMUSH.Tests/Commands/HideCommandTests.cs` — tests for the rewritten `@HIDE` and the `cd`/`cv`/`ch` login words.
- **Create** `SharpMUSH.Tests/Commands/ConnectionAnnounceIntegrationTests.cs` — end-to-end connect/reconnect/disconnect/partial-disconnect/hidden/channel-announcement tests via the real command parser and DI container.

---

### Task 1: Two-flag-group broadcast on `IGameBroadcastService`

PennMUSH's `flag_broadcast(flag1, flag2, fmt)` (`src/notify.c:1706`) sends to every connected player who has *any* flag from `flag1`'s list AND *any* flag from `flag2`'s list (either list may be absent, meaning "no restriction"). We need this to reproduce `flag_broadcast("ROYALTY WIZARD", "HEAR_CONNECT", ...)` — the existing `BroadcastToFlagAsync(string flagName, string message)` only supports a single required flag. Add an overload that also takes an optional "any of these" group.

**Files:**
- Modify: `SharpMUSH.Library/Services/Interfaces/IGameBroadcastService.cs`
- Modify: `SharpMUSH.Library/Services/GameBroadcastService.cs`
- Test: `SharpMUSH.Tests/Services/GameBroadcastServiceTests.cs`

**Interfaces:**
- Produces: `ValueTask BroadcastToFlagAsync(IReadOnlyCollection<string>? anyOfFlags, string requiredFlag, string message)` on `IGameBroadcastService` — used by Task 3/5 for the `HEAR_CONNECT` broadcast.

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/Services/GameBroadcastServiceTests.cs
using Mediator;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Tests.Services;

public class GameBroadcastServiceTests
{
	[Test]
	public async Task BroadcastToFlagAsync_TwoFlagGroups_RequiresAnyOfFirstGroupAndTheSecondFlag()
	{
		var connectionService = Substitute.For<IConnectionService>();
		var notifyService = Substitute.For<INotifyService>();
		var mediator = Substitute.For<IMediator>();

		var royaltyPlayerRef = new DBRef(10, null);
		var mortalPlayerRef = new DBRef(11, null);

		var royaltyConn = new IConnectionService.ConnectionData(
			Handle: 1, Ref: royaltyPlayerRef, State: IConnectionService.ConnectionState.LoggedIn,
			Metadata: new(), Connected: null, Idle: null, PresenceClass: PresenceClasses.Interactive);
		var mortalConn = new IConnectionService.ConnectionData(
			Handle: 2, Ref: mortalPlayerRef, State: IConnectionService.ConnectionState.LoggedIn,
			Metadata: new(), Connected: null, Idle: null, PresenceClass: PresenceClasses.Interactive);

		connectionService.GetAll().Returns(new[] { royaltyConn, mortalConn }.ToAsyncEnumerable());

		var royaltyPlayer = TestHelpers.FakePlayerNode(royaltyPlayerRef, flags: ["ROYALTY", "HEAR_CONNECT"]);
		var mortalPlayer = TestHelpers.FakePlayerNode(mortalPlayerRef, flags: ["HEAR_CONNECT"]);

		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == royaltyPlayerRef), Arg.Any<CancellationToken>())
			.Returns(royaltyPlayer);
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == mortalPlayerRef), Arg.Any<CancellationToken>())
			.Returns(mortalPlayer);

		var service = new GameBroadcastService(connectionService, notifyService, mediator);

		await service.BroadcastToFlagAsync(["ROYALTY", "WIZARD"], "HEAR_CONNECT", "GAME: Someone has connected.");

		await notifyService.Received(1).Notify(1, "GAME: Someone has connected.");
		await notifyService.DidNotReceive().Notify(2, Arg.Any<string>());
	}
}
```

If `TestHelpers.FakePlayerNode(...)` doesn't already exist in `SharpMUSH.Tests.Infrastructure`, build the `GetObjectNodeResult`-equivalent by hand instead — check `SharpMUSH.Tests/Services/EventServiceTests.cs` and `SharpMUSH.Tests.Infrastructure/TestHelpers.cs` for the actual helper name before writing this (they were skipped/unused in this codebase as of this plan's writing, so the exact helper may not exist — construct a `SharpPlayer` + wrap in `GetObjectNodeQuery`'s result type directly if not, following whatever pattern `EventServiceTests.cs` uses even though its tests are marked `[Skip]`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/GameBroadcastServiceTests/*"`
Expected: FAIL — `BroadcastToFlagAsync` with this overload does not exist (compile error).

- [ ] **Step 3: Implement**

```csharp
// IGameBroadcastService.cs — add after the existing BroadcastToFlagAsync
/// <summary>
/// Broadcast a message to connected players who have any flag in <paramref name="anyOfFlags"/>
/// (or unconditionally, if null) AND have <paramref name="requiredFlag"/>.
/// Equivalent to PennMUSH's <c>flag_broadcast("ROYALTY WIZARD", "HEAR_CONNECT", T("..."))</c>.
/// </summary>
ValueTask BroadcastToFlagAsync(IReadOnlyCollection<string>? anyOfFlags, string requiredFlag, string message);
```

```csharp
// GameBroadcastService.cs
/// <inheritdoc />
public ValueTask BroadcastToFlagAsync(string flagName, string message)
	=> BroadcastToFlagAsync(null, flagName, message);

/// <inheritdoc />
public async ValueTask BroadcastToFlagAsync(IReadOnlyCollection<string>? anyOfFlags, string requiredFlag, string message)
{
	await foreach (var conn in connectionService.GetAll())
	{
		if (conn.State != IConnectionService.ConnectionState.LoggedIn || conn.Ref is null)
		{
			continue;
		}

		try
		{
			var playerResult = await mediator.Send(new GetObjectNodeQuery(conn.Ref.Value));
			if (playerResult.IsNone)
			{
				continue;
			}

			var player = playerResult.Known;

			if (anyOfFlags is not null)
			{
				var hasAny = false;
				foreach (var flag in anyOfFlags)
				{
					if (await player.HasFlag(flag))
					{
						hasAny = true;
						break;
					}
				}
				if (!hasAny)
				{
					continue;
				}
			}

			if (await player.HasFlag(requiredFlag))
			{
				await notifyService.Notify(conn.Handle, message);
			}
		}
		catch
		{
			// Skip connections where we can't resolve the player object.
		}
	}
}
```

Replace the existing `BroadcastToFlagAsync(string flagName, string message)` body with the one-line delegation above (keep it — it's still the right API for the WIZARD/`SUSPECT` case, which needs no `anyOfFlags` restriction).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/GameBroadcastServiceTests/*"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/Interfaces/IGameBroadcastService.cs SharpMUSH.Library/Services/GameBroadcastService.cs SharpMUSH.Tests/Services/GameBroadcastServiceTests.cs
git commit -m "feat: add two-flag-group broadcast for HEAR_CONNECT-style messages"
```

---

### Task 2: Missing `ErrorMessages` constant + verify existing wording constants

`SharpMUSH.Library/Definitions/ErrorMessages.cs:540-543` already has `GameHasConnected`, `GameHasReconnected`, `GameHasDisconnected`, `GameHasPartiallyDisconnected`. There's a `GameSuspectCreated = "GAME: Suspect {0} created."` (line 552) for player creation, but we need a generic one for connect/disconnect activity (PennMUSH: `flag_broadcast("WIZARD", 0, T("GAME: Suspect %s"), tbuf1)` where `tbuf1` already contains `"<Name> has connected."` etc — so the format string only wraps once).

**Files:**
- Modify: `SharpMUSH.Library/Definitions/ErrorMessages.cs`

- [ ] **Step 1: Add the constant**

In the `Notifications` class, right after `GameSuspectCreated` (line 552):

```csharp
[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
public const string GameSuspectActivity = "GAME: Suspect {0}";
```

- [ ] **Step 2: Build to confirm no errors**

Run: `dotnet build SharpMUSH.Library`
Expected: builds clean (this is a pure addition, nothing consumes it yet).

- [ ] **Step 3: Commit**

```bash
git add SharpMUSH.Library/Definitions/ErrorMessages.cs
git commit -m "feat: add GameSuspectActivity broadcast format string"
```

---

### Task 3: `IConnectionAnnounceService` — connect side (player hook + broadcasts)

Port `announce_connect` (`src/bsd.c:5906-6017`), minus the `isnew` (player-creation) branch — that's a different code path already handled elsewhere — and minus zone/room/master-room hook dispatch (Task 4).

**Files:**
- Create: `SharpMUSH.Library/Services/Interfaces/IConnectionAnnounceService.cs`
- Create: `SharpMUSH.Library/Services/ConnectionAnnounceService.cs`
- Test: `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs`

**Interfaces:**
- Consumes: `ICommunicationService.SendToRoomAsync(AnySharpObject executor, AnySharpContainer room, Func<AnySharpObject, OneOf<MString,string>> messageFunc, INotifyService.NotificationType notificationType, AnySharpObject? sender = null, IEnumerable<AnySharpObject>? excludeObjects = null)`; `IGameBroadcastService.BroadcastToFlagAsync(string, string)` and the new `BroadcastToFlagAsync(IReadOnlyCollection<string>?, string, string)` from Task 1; `IAttributeService.GetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, IAttributeService.AttributeMode mode, bool parent = true)`; `AnySharpObject.Where()` → `ValueTask<AnySharpContainer>`; `AnySharpObject.AsContainer` → `AnySharpContainer`; `AnySharpObject.IsDark()`, `.HasFlag(string)`.
- Produces: `IConnectionAnnounceService.AnnounceConnectAsync(IMUSHCodeParser parser, AnySharpObject player, int connectionCount)` and `.AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections)` (disconnect body is a stub in this task, filled in by Task 5) — signatures other tasks depend on.

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class ConnectionAnnounceServiceTests
{
	[Test]
	public async Task AnnounceConnectAsync_FirstConnection_BroadcastsHasConnected()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		var mediator = Substitute.For<Mediator.IMediator>();
		var configuration = TestHelpers.FakeOptionsWrapper(); // AnnounceConnects=true, RoomConnects=true by default

		var service = new ConnectionAnnounceService(communicationService, gameBroadcastService, attributeService, mediator, configuration);

		var player = TestHelpers.FakeConnectedPlayer("Bob"); // AnySharpObject, DARK unset, not SUSPECT
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1);

		await communicationService.Received(1).SendToRoomAsync(
			player, player.AsContainer,
			Arg.Any<Func<AnySharpObject, OneOf<MString, string>>>(),
			INotifyService.NotificationType.Announce,
			null, null);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(
			null, "HEAR_CONNECT", "GAME: Bob has connected.");
	}

	[Test]
	public async Task AnnounceConnectAsync_SecondConnection_UsesReconnectedWording()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		var mediator = Substitute.For<Mediator.IMediator>();
		var configuration = TestHelpers.FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(communicationService, gameBroadcastService, attributeService, mediator, configuration);
		var player = TestHelpers.FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 2);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(
			null, "HEAR_CONNECT", "GAME: Bob has reconnected.");
	}
}
```

`TestHelpers.FakeOptionsWrapper()` and `TestHelpers.FakeConnectedPlayer(name)` almost certainly don't exist yet — check `SharpMUSH.Tests.Infrastructure/TestHelpers.cs` for the closest existing equivalents (e.g. how other pure-unit service tests fake `IOptionsWrapper<SharpMUSHOptions>` and an `AnySharpObject` with flags) and either reuse what's there or add small private builders local to this test file instead of touching shared infra — this is a plan-writing-time unknown, resolve it by reading the file at execution time before writing the test body verbatim as shown.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*"`
Expected: FAIL — `ConnectionAnnounceService` doesn't exist (compile error).

- [ ] **Step 3: Implement the interface and connect-side skeleton**

```csharp
// SharpMUSH.Library/Services/Interfaces/IConnectionAnnounceService.cs
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Ports PennMUSH's announce_connect/announce_disconnect (src/bsd.c): room/inventory broadcasts,
/// HEAR_CONNECT and SUSPECT/WIZARD broadcasts, and ACONNECT/ADISCONNECT hook dispatch to the
/// player, their room, their zone, and the master room.
/// </summary>
public interface IConnectionAnnounceService
{
	/// <summary>Called once a login completes. <paramref name="connectionCount"/> is the player's total connection count AFTER this one.</summary>
	ValueTask AnnounceConnectAsync(IMUSHCodeParser parser, AnySharpObject player, int connectionCount);

	/// <summary>Called once a socket leaves LoggedIn. <paramref name="remainingConnections"/> excludes the disconnecting socket.</summary>
	ValueTask AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections);
}
```

```csharp
// SharpMUSH.Library/Services/ConnectionAnnounceService.cs
using Mediator;
using OneOf;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class ConnectionAnnounceService(
	ICommunicationService communicationService,
	IGameBroadcastService gameBroadcastService,
	IAttributeService attributeService,
	IMediator mediator,
	IOptionsWrapper<SharpMUSHOptions> configuration) : IConnectionAnnounceService
{
	public async ValueTask AnnounceConnectAsync(IMUSHCodeParser parser, AnySharpObject player, int connectionCount)
	{
		var isDark = await player.IsDark();
		var name = player.Object().Name;
		var wording = connectionCount > 1
			? ErrorMessages.Notifications.GameHasReconnected
			: ErrorMessages.Notifications.GameHasConnected;
		var fullMessage = $"{name} {wording}";

		if (await player.HasFlag("SUSPECT"))
		{
			await gameBroadcastService.BroadcastToFlagAsync(
				"WIZARD", string.Format(ErrorMessages.Notifications.GameSuspectActivity, fullMessage));
		}

		var gameLine = $"GAME: {fullMessage}";
		if (isDark)
		{
			await gameBroadcastService.BroadcastToFlagAsync(["ROYALTY", "WIZARD"], "HEAR_CONNECT", gameLine);
		}
		else
		{
			await gameBroadcastService.BroadcastToFlagAsync(null, "HEAR_CONNECT", gameLine);
		}

		if (configuration.CurrentValue.Cosmetic.AnnounceConnects)
		{
			await communicationService.SendToRoomAsync(
				player, player.AsContainer, _ => fullMessage, INotifyService.NotificationType.Announce);

			if (!isDark)
			{
				var loc = await player.Where();
				await communicationService.SendToRoomAsync(
					player, loc, _ => fullMessage, INotifyService.NotificationType.Announce,
					excludeObjects: [player]);
			}
		}

		await QueueHookAsync(parser, player, player, "ACONNECT", connectionCount.ToString());

		if (configuration.CurrentValue.Attribute.RoomConnects)
		{
			var loc = await player.Where();
			var locObj = new AnySharpObject(loc);
			if (locObj.IsRoom || locObj.IsThing)
			{
				await QueueHookAsync(parser, locObj, player, "ACONNECT", connectionCount.ToString());
			}
		}

		await DispatchZoneAndMasterRoomHooksAsync(parser, player, "ACONNECT", connectionCount.ToString());
	}

	public ValueTask AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections)
		=> throw new NotImplementedException("Implemented in Task 5");

	private async ValueTask DispatchZoneAndMasterRoomHooksAsync(
		IMUSHCodeParser parser, AnySharpObject player, string attrName, string countArg)
	{
		// Implemented in Task 4.
		await ValueTask.CompletedTask;
	}

	private static async ValueTask QueueHookAsync(
		IMUSHCodeParser parser, AnySharpObject owner, AnySharpObject player, string attrName, string countArg)
	{
		// Implemented fully in this task's Step 3 continuation — see below.
	}
}
```

`AnySharpObject.IsRoom`/`.IsThing` — check `SharpMUSH.Library/DiscriminatedUnions/AnySharpObject.cs` for the exact property names (this codebase consistently exposes `IsPlayer`/`IsRoom`/`IsExit`/`IsThing` booleans on the OneOf wrapper — confirm before using verbatim).

Now flesh out `QueueHookAsync` for real — this is the port of PennMUSH's `queue_attribute_base` plus the `%0`/`%1` `PE_REGS` binding, copying the fresh-`ParserState`-push pattern from `SharpMUSH.Library/Services/EventService.cs:118-144` (that method pushes a brand-new state when `parser.State.IsEmpty`, binding `EnvironmentRegisters`/`Arguments` to the hook's positional args, `Executor` to the hook owner, `Enactor`/`Caller` to the triggering player):

```csharp
	private static async ValueTask QueueHookAsync(
		IMUSHCodeParser parser, AnySharpObject owner, AnySharpObject player, string attrName, string countArg)
	{
		var attrResult = await AttributeServiceHolder.Value!.GetAttributeAsync(
			player, owner, attrName, IAttributeService.AttributeMode.Execute, parent: true);

		if (!attrResult.IsAttribute || attrResult.AsAttribute.Length == 0)
		{
			return;
		}

		// %0 is reserved (PennMUSH leaves it unset for ACONNECT/ADISCONNECT), %1 is the connection count.
		var argsDict = new Dictionary<string, CallState> { ["0"] = new(string.Empty), ["1"] = new(countArg) };

		var ownerRef = owner.Object().DBRef;
		var playerRef = player.Object().DBRef;
		var isEmpty = parser.State.IsEmpty;

		var evalParser = parser.Push(new ParserState(
			Registers: new([[]]),
			IterationRegisters: [],
			RegexRegisters: [],
			SwitchStack: [],
			ExecutionStack: [],
			EnvironmentRegisters: argsDict,
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: null,
			CommandInvoker: isEmpty
				? _ => ValueTask.FromResult(new OneOf.Types.Option<CallState>(new OneOf.Types.None()))
				: parser.CurrentState.CommandInvoker,
			Switches: [],
			Arguments: argsDict,
			Executor: ownerRef,
			Enactor: playerRef,
			Caller: playerRef,
			Handle: isEmpty ? null : parser.CurrentState.Handle,
			CallDepth: isEmpty ? new InvocationCounter() : parser.CurrentState.CallDepth ?? new InvocationCounter(),
			FunctionRecursionDepths: isEmpty
				? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
				: parser.CurrentState.FunctionRecursionDepths ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
			TotalInvocations: isEmpty ? new InvocationCounter() : parser.CurrentState.TotalInvocations ?? new InvocationCounter(),
			LimitExceeded: isEmpty ? new LimitExceededFlag() : parser.CurrentState.LimitExceeded ?? new LimitExceededFlag()));

		var attributeText = attrResult.AsAttribute.Last().Value.ToPlainText();
		await evalParser.CommandListParse(MarkupText.Plain(attributeText));
	}
```

This is written as a `static` method above using a placeholder `AttributeServiceHolder` — **fix that**: make it an instance method (drop `static`) so it can call `attributeService` directly (the constructor-injected field), matching how `communicationService`/`gameBroadcastService` are called elsewhere in this class. Re-read `SharpMUSH.Library/ParserInterfaces/ParserState.cs` for the exact record field list/order before typing this out (this plan's field list is transcribed from `EventService.cs:119-144` — verify it still matches at execution time; the two must stay structurally identical or the state push will fail to compile).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*"`
Expected: PASS for both tests (the `AnnounceDisconnectAsync` stub throwing `NotImplementedException` is fine — no test calls it yet).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/Interfaces/IConnectionAnnounceService.cs SharpMUSH.Library/Services/ConnectionAnnounceService.cs SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs
git commit -m "feat: add ConnectionAnnounceService connect-side (player hook + broadcasts)"
```

---

### Task 4: Zone and master-room `ACONNECT`/`ADISCONNECT` dispatch

Port the zone (`src/bsd.c:5994-6011`) and master-room (`:6012-6015`) traversal from `announce_connect`. `ADISCONNECT`'s equivalent (`:6085-6127`) does the same traversal — write this once, parameterized by attribute name, and call it from both `AnnounceConnectAsync` (Task 3) and `AnnounceDisconnectAsync` (Task 5).

**Files:**
- Modify: `SharpMUSH.Library/Services/ConnectionAnnounceService.cs`
- Test: `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs`

**Interfaces:**
- Consumes: `AnySharpObject.Zone` → resolved via `SharpObject.Zone` (`AsyncRelation<AnyOptionalSharpObject>`, e.g. `await player.Object().Zone.WithCancellation(ct)` → `.IsNone`/`.Known`); `configuration.CurrentValue.Database.MasterRoom` (`uint`); `AnySharpContainer.Content(IMediator)` → `IAsyncEnumerable<AnySharpContent>`; `mediator.Send(new GetObjectNodeQuery(dbref))`.
- Produces: `DispatchZoneAndMasterRoomHooksAsync(IMUSHCodeParser, AnySharpObject player, string attrName, string countArg)` fully implemented (was a no-op stub from Task 3).

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async Task AnnounceConnectAsync_MasterRoomObjectsWithAconnect_AreQueued()
{
	// Arrange a fake master room (configured dbref) containing one object with an ACONNECT attribute,
	// and a fake IMediator that returns it for GetObjectNodeQuery + resolves that object's Content().
	// Assert AttributeService.GetAttributeAsync was called with (player, thatObject, "ACONNECT", Execute, true).
	// Exact fakes depend on what SharpMUSH.Tests.Infrastructure already provides for AnySharpContainer.Content()
	// (grep existing tests exercising @dolist/room contents, e.g. under SharpMUSH.Tests/Commands/, for the pattern) —
	// resolve this at execution time rather than guessing the mock shape here.
}
```

Write this test for real once Step 3's implementation exists — because faking `AnySharpContent`/room-contents iteration needs the exact test double already used elsewhere in this codebase (found by grep at execution time), a full literal test body isn't safe to prescribe sight-unseen. The following steps are not optional: do not skip writing this test just because the exact mock shape needed research.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*MasterRoom*"`
Expected: FAIL (the stub does nothing, so `GetAttributeAsync` is never called for the master-room object).

- [ ] **Step 3: Implement**

```csharp
	private async ValueTask DispatchZoneAndMasterRoomHooksAsync(
		IMUSHCodeParser parser, AnySharpObject player, string attrName, string countArg)
	{
		var zoneRelation = await player.Object().Zone.WithCancellation(CancellationToken.None);
		if (!zoneRelation.IsNone)
		{
			var zone = zoneRelation.Known;
			if (zone.IsThing)
			{
				await QueueHookAsync(parser, zone, player, attrName, countArg);
			}
			else if (zone.IsRoom)
			{
				await foreach (var content in zone.AsContainer.Content(mediator))
				{
					await QueueHookAsync(parser, content.WithRoomOption(), player, attrName, countArg);
				}
			}
		}

		var masterRoomDbref = configuration.CurrentValue.Database.MasterRoom;
		if (masterRoomDbref is not null)
		{
			var masterRoomResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)masterRoomDbref.Value, null)));
			if (!masterRoomResult.IsNone)
			{
				await foreach (var content in masterRoomResult.Known.AsContainer.Content(mediator))
				{
					await QueueHookAsync(parser, content.WithRoomOption(), player, attrName, countArg);
				}
			}
		}
	}
```

`AnySharpContent.WithRoomOption()` — confirm this is the right conversion back to `AnySharpObject` by checking its usage in `SharpMUSH.Library/Services/CommunicationService.cs:75,87` (already read during planning — it's the pattern `SendToRoomAsync` uses for exactly this: turning room-contents entries into `AnySharpObject`s). `zone.IsThing`/`zone.IsRoom` and `player.Object().Zone` — re-verify exact property/method names on `AnyOptionalSharpObject`/`SharpObject` at execution time (`AnyOptionalSharpObject.Known` and `.IsNone` were confirmed during planning via `PermissionService.cs:328-330`).

Also update `AnnounceConnectAsync` (Task 3) to actually call this instead of the no-op stub — it already has the call site `await DispatchZoneAndMasterRoomHooksAsync(parser, player, "ACONNECT", connectionCount.ToString());`, just delete the stub body and the `// Implemented in Task 4` comment.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*"`
Expected: PASS, all tests in the file green.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/ConnectionAnnounceService.cs SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs
git commit -m "feat: dispatch ACONNECT/ADISCONNECT hooks to zone and master room"
```

---

### Task 5: `AnnounceDisconnectAsync` — disconnect side

Port `announce_disconnect` (`src/bsd.c:6019-6168`): mirrors Task 3's connect side, plus writing `LASTLOGOUT` when the last connection closes.

**Files:**
- Modify: `SharpMUSH.Library/Services/ConnectionAnnounceService.cs`
- Test: `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs`

**Interfaces:**
- Consumes: `IAttributeService.SetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, MString value)` (returns `ValueTask<OneOf<Success, Error<string>>>`, from `SharpMUSH.Library/Services/Interfaces/IAttributeService.cs:30`).
- Produces: `AnnounceDisconnectAsync` fully implemented (no longer throws).

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task AnnounceDisconnectAsync_LastConnection_BroadcastsHasDisconnectedAndSetsLastLogout()
{
	var communicationService = Substitute.For<ICommunicationService>();
	var gameBroadcastService = Substitute.For<IGameBroadcastService>();
	var attributeService = Substitute.For<IAttributeService>();
	var mediator = Substitute.For<Mediator.IMediator>();
	var configuration = TestHelpers.FakeOptionsWrapper();

	var service = new ConnectionAnnounceService(communicationService, gameBroadcastService, attributeService, mediator, configuration);
	var player = TestHelpers.FakeConnectedPlayer("Bob");
	var parser = Substitute.For<IMUSHCodeParser>();

	await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 0);

	await gameBroadcastService.Received(1).BroadcastToFlagAsync(null, "HEAR_CONNECT", "GAME: Bob has disconnected.");
	await attributeService.Received(1).SetAttributeAsync(player, player, "LASTLOGOUT", Arg.Any<MarkupText>());
}

[Test]
public async Task AnnounceDisconnectAsync_OtherConnectionsRemain_UsesPartiallyDisconnectedWordingAndSkipsLastLogout()
{
	var communicationService = Substitute.For<ICommunicationService>();
	var gameBroadcastService = Substitute.For<IGameBroadcastService>();
	var attributeService = Substitute.For<IAttributeService>();
	var mediator = Substitute.For<Mediator.IMediator>();
	var configuration = TestHelpers.FakeOptionsWrapper();

	var service = new ConnectionAnnounceService(communicationService, gameBroadcastService, attributeService, mediator, configuration);
	var player = TestHelpers.FakeConnectedPlayer("Bob");
	var parser = Substitute.For<IMUSHCodeParser>();

	await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 1);

	await gameBroadcastService.Received(1).BroadcastToFlagAsync(null, "HEAR_CONNECT", "GAME: Bob has partially disconnected.");
	await attributeService.DidNotReceive().SetAttributeAsync(player, player, "LASTLOGOUT", Arg.Any<MarkupText>());
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*Disconnect*"`
Expected: FAIL — `NotImplementedException` from the Task 3 stub.

- [ ] **Step 3: Implement**

```csharp
	public async ValueTask AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections)
	{
		var isDark = await player.IsDark();
		var name = player.Object().Name;
		var wording = remainingConnections > 0
			? ErrorMessages.Notifications.GameHasPartiallyDisconnected
			: ErrorMessages.Notifications.GameHasDisconnected;
		var fullMessage = $"{name} {wording}";

		if (await player.HasFlag("SUSPECT"))
		{
			await gameBroadcastService.BroadcastToFlagAsync(
				"WIZARD", string.Format(ErrorMessages.Notifications.GameSuspectActivity, fullMessage));
		}

		var gameLine = $"GAME: {fullMessage}";
		if (isDark)
		{
			await gameBroadcastService.BroadcastToFlagAsync(["ROYALTY", "WIZARD"], "HEAR_CONNECT", gameLine);
		}
		else
		{
			await gameBroadcastService.BroadcastToFlagAsync(null, "HEAR_CONNECT", gameLine);
		}

		if (configuration.CurrentValue.Cosmetic.AnnounceConnects)
		{
			if (!isDark)
			{
				var loc = await player.Where();
				await communicationService.SendToRoomAsync(
					player, loc, _ => fullMessage, INotifyService.NotificationType.Announce,
					excludeObjects: [player]);
			}

			await communicationService.SendToRoomAsync(
				player, player.AsContainer, _ => fullMessage, INotifyService.NotificationType.Announce);
		}

		await QueueHookAsync(parser, player, player, "ADISCONNECT", remainingConnections.ToString());

		if (configuration.CurrentValue.Attribute.RoomConnects)
		{
			var loc = await player.Where();
			var locObj = new AnySharpObject(loc);
			if (locObj.IsRoom || locObj.IsThing)
			{
				await QueueHookAsync(parser, locObj, player, "ADISCONNECT", remainingConnections.ToString());
			}
		}

		await DispatchZoneAndMasterRoomHooksAsync(parser, player, "ADISCONNECT", remainingConnections.ToString());

		if (remainingConnections == 0)
		{
			var lastLogout = DateTimeOffset.UtcNow.ToLocalTime()
				.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture);
			await attributeService.SetAttributeAsync(player, player, "LASTLOGOUT", MarkupText.Plain(lastLogout));
		}
	}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*"`
Expected: PASS, whole file green.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/ConnectionAnnounceService.cs SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs
git commit -m "feat: implement ConnectionAnnounceService disconnect side + LASTLOGOUT"
```

---

### Task 6: DI registration and wiring into the connect path

**Files:**
- Modify: `SharpMUSH.Server/Startup.cs`
- Modify: `SharpMUSH.Implementation/Commands/SocketCommands.cs`

- [ ] **Step 1: Register the service**

In `SharpMUSH.Server/Startup.cs`, right after line 459 (`services.AddSingleton<IGameBroadcastService, GameBroadcastService>();`):

```csharp
services.AddSingleton<IConnectionAnnounceService, ConnectionAnnounceService>();
```

Add the `using SharpMUSH.Library.Services.Interfaces;`/`using SharpMUSH.Library.Services;` usings if not already present in the file (they almost certainly already are, since `IGameBroadcastService` is registered two lines above).

- [ ] **Step 2: Inject it into `Commands` and call it from `CompletePlayerLoginAsync`**

`Commands` is DI-constructed (per CLAUDE.md: "non-null members, never static and never null-forgiven"). Find its primary constructor (likely a large parameter list at the top of `SharpMUSH.Implementation/Commands/Commands.cs` or similar partial-class-defining file) and add `IConnectionAnnounceService ConnectionAnnounceService` alongside the existing `IEventService EventService` etc. — grep `EventService` as a field/property name across `Commands.*.cs` to find the exact constructor file and naming convention (property, not field, capitalized, matching `EventService`/`NotifyService`/`Mediator`).

Then in `SharpMUSH.Implementation/Commands/SocketCommands.cs`, `CompletePlayerLoginAsync` (line 525), insert right after the existing `PLAYER`CONNECT`` event trigger block (after line 537, before the `RoomContents` refresh at line 540):

```csharp
		await ConnectionAnnounceService.AnnounceConnectAsync(parser, new AnySharpObject(player), (int)connectionCount);
```

(`connectionCount` at line 530 is already a `long` from `.CountAsync()` — cast to `int`, matching PennMUSH's `int num` and this plan's `AnnounceConnectAsync(..., int connectionCount)` signature. If `CountAsync()` already returns `int`, drop the cast — check its return type in `IConnectionService` before writing this line.)

- [ ] **Step 3: Build**

Run: `dotnet build SharpMUSH.Implementation SharpMUSH.Server`
Expected: builds clean.

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.Server/Startup.cs SharpMUSH.Implementation/Commands/Commands.cs SharpMUSH.Implementation/Commands/SocketCommands.cs
git commit -m "feat: wire ConnectionAnnounceService into the login completion path"
```

---

### Task 7: Wire into the disconnect path

**Files:**
- Modify: `SharpMUSH.Implementation/Handlers/ConnectionStateEventHandler.cs`

`ConnectionStateEventHandler` is already constructor-injected (`SharpMUSH.Implementation/Handlers/ConnectionStateEventHandler.cs:24-31`) with `connectionService, eventService, parser, notifyService, mediator, configuration` — add `IConnectionAnnounceService connectionAnnounceService` to that parameter list.

- [ ] **Step 1: Add the dependency**

```csharp
public class ConnectionStateEventHandler(
	IConnectionService connectionService,
	IEventService eventService,
	IMUSHCodeParser parser,
	INotifyService notifyService,
	IMediator mediator,
	IConnectionAnnounceService connectionAnnounceService,
	IOptionsWrapper<SharpMUSHOptions> configuration)
	: INotificationHandler<ConnectionStateChangeNotification>
```

- [ ] **Step 2: Call it in the disconnect branch**

The disconnect branch (lines 77-127) already computes `remainingConnections` (line 88) and resolves the player object later (line 114, for the room-contents refresh) — move that resolution up so both the announcement and the existing room-refresh use one lookup, and call the new service right after `remainingConnections` is known:

```csharp
			var remainingConnections = await connectionService.Get(notification.PlayerRef.Value).CountAsync();

			var playerNode = await mediator.Send(new GetObjectNodeQuery(notification.PlayerRef.Value));
			if (!playerNode.IsNone && playerNode.IsPlayer)
			{
				await connectionAnnounceService.AnnounceDisconnectAsync(parser, playerNode.AsPlayer, (int)remainingConnections);
			}
```

Then delete the now-duplicate `var playerNode = await mediator.Send(...)` at what was line 114 (the `RoomContents` block right after it stays, just reusing the `playerNode`/`roomContainer` already resolved — the existing code from line 115 (`if (!playerNode.IsNone && playerNode.IsPlayer)`) onward for the room-contents refresh can move inside or right after this new block instead of being resolved twice). Re-read the current file at execution time (it may have shifted slightly from the line numbers cited here, captured during planning) before editing, and keep the existing `SOCKET`DISCONNECT`` event trigger below untouched — it's a separate concern.

`playerNode.AsPlayer` gives a `SharpPlayer`, not `AnySharpObject` — wrap it: `new AnySharpObject(playerNode.AsPlayer)`.

- [ ] **Step 3: Build**

Run: `dotnet build SharpMUSH.Implementation`
Expected: builds clean.

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.Implementation/Handlers/ConnectionStateEventHandler.cs
git commit -m "feat: wire ConnectionAnnounceService into the disconnect path"
```

---

### Task 9: Per-connection `Hidden` state

Add the `Hidden` metadata convention and a player-level aggregate check, mirroring PennMUSH's `DESC.hide` (`bsd.c:265`).

**Files:**
- Modify: `SharpMUSH.Library/Services/Interfaces/IConnectionService.cs`
- Test: `SharpMUSH.Tests/Services/ConnectionServiceHiddenTests.cs`

**Interfaces:**
- Produces: `ConnectionData.IsHidden` (computed `bool`, reads `Metadata["Hidden"] == "1"`); `IConnectionService.IsPlayerHiddenAsync(DBRef playerRef)` → `ValueTask<bool>`, true if **any** of the player's active connections has `IsHidden == true` (this single-player aggregate is what `hidden()` and `WHO`'s "does this player count as hidden at all" checks use elsewhere; the WHO row-level check in Task 12 uses the per-connection `IsHidden` directly, not the aggregate, since WHO lists one row per connection).

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/Services/ConnectionServiceHiddenTests.cs
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;
using SharpMUSH.Messaging;

namespace SharpMUSH.Tests.Services;

public class ConnectionServiceHiddenTests
{
	[Test]
	public async Task IsPlayerHiddenAsync_NoConnectionsHidden_ReturnsFalse()
	{
		var publisher = Substitute.For<IPublisher>();
		var service = new ConnectionService(publisher);
		var playerRef = new DBRef(50, null);
		await service.Register(1, "encoding-placeholder"); // match ConnectionService.Register's real signature — verify at execution time
		await service.Bind(1, playerRef);

		var result = await service.IsPlayerHiddenAsync(playerRef);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task IsPlayerHiddenAsync_OneOfTwoConnectionsHidden_ReturnsTrue()
	{
		var publisher = Substitute.For<IPublisher>();
		var service = new ConnectionService(publisher);
		var playerRef = new DBRef(51, null);
		await service.Register(2, "encoding-placeholder");
		await service.Bind(2, playerRef);
		await service.Register(3, "encoding-placeholder");
		await service.Bind(3, playerRef);

		service.Update(3, "Hidden", "1");

		var result = await service.IsPlayerHiddenAsync(playerRef);

		await Assert.That(result).IsTrue();
	}
}
```

`ConnectionService`'s real constructor/`Register`/`Bind`/`Update` signatures were only partially captured during planning (`Register`/`Bind`/`Update` exist per prior research, exact parameter lists weren't — e.g. whether `Register` takes an encoding string or something else). Re-read `SharpMUSH.Library/Services/ConnectionService.cs:25-333` and `SharpMUSH.Tests/Services/NotifyServiceTests.cs` (which already constructs a real `ConnectionService` for its own tests) before finalizing this test body — match that file's construction pattern exactly rather than the placeholder shown here.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionServiceHiddenTests/*"`
Expected: FAIL — `IsHidden`/`IsPlayerHiddenAsync` don't exist (compile error).

- [ ] **Step 3: Implement**

In `IConnectionService.cs`, on the `ConnectionData` record (alongside the existing `CommandCount`/`Connected`/`Idle` computed properties):

```csharp
public bool IsHidden => Metadata.TryGetValue("Hidden", out var value) && value == "1";
```

On `IConnectionService` itself, add:

```csharp
/// <summary>True if any of this player's active connections is Hidden (PennMUSH DESC.hide).</summary>
ValueTask<bool> IsPlayerHiddenAsync(DBRef playerRef);
```

Implement in `ConnectionService.cs` using the existing `Get(DBRef)` (already used by `CountAsync()` call sites elsewhere in this codebase):

```csharp
public async ValueTask<bool> IsPlayerHiddenAsync(DBRef playerRef)
{
	await foreach (var conn in Get(playerRef))
	{
		if (conn.IsHidden)
		{
			return true;
		}
	}
	return false;
}
```

Confirm `Get(DBRef)`'s actual return type (`IAsyncEnumerable<ConnectionData>` vs something requiring `.CountAsync()`-style extension) by re-reading `ConnectionService.cs` before writing this — it's referenced as `ConnectionService.Get(playerRef).CountAsync()` elsewhere (`ConnectionStateEventHandler.cs:88`), confirming it's at least `IAsyncEnumerable`-shaped, but confirm the element type is `ConnectionData` before assuming `.IsHidden` is directly accessible in the loop.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionServiceHiddenTests/*"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/Interfaces/IConnectionService.cs SharpMUSH.Library/Services/ConnectionService.cs SharpMUSH.Tests/Services/ConnectionServiceHiddenTests.cs
git commit -m "feat: add per-connection Hidden state (PennMUSH DESC.hide)"
```

---

### Task 10: Rewrite `@HIDE` to toggle connection-level `Hidden`

Currently `@HIDE` (`SharpMUSH.Implementation/Commands/WizardCommands.cs:821-874`) toggles the `DARK` object flag — not PennMUSH's actual `@hide` semantics (a permission-gated, per-connection toggle, unrelated to `DARK`). Fix it to match `hide_player` (`bsd.c:7162-Ā220`-ish, already read during planning): no target → hide/unhide **all** of the executor's own active connections; permission-gated by `CanHide()`.

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/WizardCommands.cs`
- Test: `SharpMUSH.Tests/Commands/HideCommandTests.cs`

**Interfaces:**
- Consumes: `AnySharpObject.CanHide()` (`HelperFunctions.cs:310-311`); `IConnectionService.Get(DBRef)` → per-connection enumeration; `IConnectionService.Update(long handle, string key, string value)` (`ConnectionService.cs:156`, confirmed existing).

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/Commands/HideCommandTests.cs
// Follow SharpMUSH.Tests/Commands/CommunicationCommandTests.cs's fixture pattern:
// [ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)], Parser.CommandParse(...),
// pull IConnectionService from WebAppFactoryArg.Services to assert connection state directly.
[Test]
public async ValueTask Hide_TogglesConnectionHiddenState_ForAPrivilegedExecutor()
{
	// Arrange a wizard-flagged connected executor (WebAppFactoryArg's default executor is already
	// wizard per other tests in this fixture — confirm via CommunicationCommandTests.cs before assuming).
	var handle = WebAppFactoryArg.ExecutorHandle; // exact property name TBD — grep the fixture for how
	                                                // other tests get "the current connection handle
	                                                // for the default executor", it's used by SOCKET-
	                                                // and connection-scoped tests elsewhere.

	await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@hide"));

	var isHidden = ConnectionService.Get(handle)?.IsHidden;
	await Assert.That(isHidden).IsEqualTo(true);

	await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@hide"));
	isHidden = ConnectionService.Get(handle)?.IsHidden;
	await Assert.That(isHidden).IsEqualTo(false);
}
```

The exact way this test fixture exposes "the connection handle behind the default test executor" is a research gap — resolve it by grep before writing this test for real (search `WebAppFactoryArg` usages across `SharpMUSH.Tests/Commands/*.cs` for anything handle-shaped), rather than guessing the property name.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/HideCommandTests/*"`
Expected: FAIL — current `@HIDE` toggles `DARK`, not connection `Hidden`.

- [ ] **Step 3: Implement**

Replace the body of `Hide` in `WizardCommands.cs:821-874`:

```csharp
	[SharpCommand(Name = "@HIDE", Switches = ["NO", "OFF", "YES", "ON"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = ["on-off"])]
	public async ValueTask<Option<CallState>> Hide(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (!await executor.CanHide())
		{
			await NotifyService.Notify(executor, "Permission denied.");
			return CallState.Empty;
		}

		var playerRef = executor.Object().DBRef;
		var connections = await ConnectionService.Get(playerRef).ToListAsync();
		var isHidden = connections.Any(c => c.IsHidden);

		bool shouldBeHidden;
		if (switches.Contains("YES") || switches.Contains("ON"))
		{
			shouldBeHidden = true;
		}
		else if (switches.Contains("NO") || switches.Contains("OFF"))
		{
			shouldBeHidden = false;
		}
		else
		{
			shouldBeHidden = !isHidden;
		}

		foreach (var connection in connections)
		{
			ConnectionService.Update(connection.Handle, "Hidden", shouldBeHidden ? "1" : "0");
		}

		await NotifyService.Notify(executor, shouldBeHidden ? "Connection hidden." : "Connection unhidden.");
		return CallState.Empty;
	}
```

Check whether `ErrorMessages.Notifications` already has `NowHiddenFromWho`/`NoLongerHiddenFromWho`/`AlreadyHiddenFromWho`/`AlreadyVisibleOnWho` (used by the old implementation) — those were about the `DARK`-flag framing ("hidden from WHO") and are semantically wrong for the new connection-level meaning ("Connection hidden." is PennMUSH's actual notify text, `bsd.c:7205,7207`); either repurpose those constants' *text* to PennMUSH's wording or add two new ones (`ConnectionHidden`/`ConnectionUnhidden`) — don't leave the old WHO-framed text pointing at the new behavior, since "hidden from WHO" underclaims what's now a real connection-visibility change (it also affects `hidden()`, channel-connect-announcement wording, etc.). Check `ErrorDarkFlagNotFound`'s only use was for the old `DARK`-flag lookup — it's dead code once this rewrite lands; remove it only if nothing else references it (grep first).

`ToListAsync()` on `IAsyncEnumerable<ConnectionData>` needs `using System.Linq;`/`using DotNext.Linq;` or whatever LINQ-async extension this codebase already uses elsewhere for the same pattern (`SocketCommands.cs` already does `.ToListAsync()` on similar streams — grep it for the exact using directive to copy).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/HideCommandTests/*"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Commands/WizardCommands.cs SharpMUSH.Tests/Commands/HideCommandTests.cs
git commit -m "fix: @HIDE now toggles connection-level Hidden state, not the DARK flag"
```

---

### Task 11: `CD`/`CV`/`CH` login-screen command aliases

Port PennMUSH's alternate login words (`bsd.c:4431-4497`): `cd` (connect + force `DARK` on + hide if permitted), `cv` (connect + force `DARK` off), `ch` (connect + hide if permitted, `DARK` untouched). `[SharpCommand]` has no alias-array support (`SharpCommandAttribute.cs` — confirmed during planning), so these are three separate command methods sharing one private core.

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/SocketCommands.cs`
- Test: `SharpMUSH.Tests/Commands/HideCommandTests.cs` (extend from Task 10)

**Interfaces:**
- Consumes: `AnySharpObject.CanHide()`; `Mediator.Send(new GetObjectFlagQuery("DARK"))`, `SetObjectFlagCommand`/`UnsetObjectFlagCommand` (all three already used verbatim in the pre-Task-10 `@HIDE` body — same call shapes apply here).
- Produces: nothing new consumed elsewhere; this task only adds login-screen entry points.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
[Arguments("cd", true, true)]   // command, expectDark, expectHidden
[Arguments("cv", false, false)]
[Arguments("ch", false, true)]
public async ValueTask ConnectAlias_SetsExpectedDarkAndHiddenState(string connectWord, bool expectDark, bool expectHidden)
{
	// Requires a fresh, unauthenticated handle (the connect screen) — follow whatever pattern
	// SharpMUSH.Tests uses for testing plain `connect` today (grep e.g. "ConnectCommandTests" or
	// similar under SharpMUSH.Tests/Commands/ for a login-flow test to copy the harness setup from);
	// resolve the exact fixture calls at execution time.
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/HideCommandTests/*ConnectAlias*"`
Expected: FAIL — `cd`/`cv`/`ch` commands don't exist.

- [ ] **Step 3: Implement**

In `SocketCommands.cs`, rename the existing `Connect` method's body into a shared private core, keeping `Connect` itself as a thin wrapper (preserves the existing `[SharpCommand(Name = "CONNECT", ...)]` call site and its `CompletePlayerLoginAsync` call unchanged in shape):

```csharp
	private enum ConnectMode { Normal, Dark, Visible, Hidden }

	[SharpCommand(Name = "CONNECT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Normal);

	[SharpCommand(Name = "CD", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public ValueTask<Option<CallState>> ConnectDark(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Dark);

	[SharpCommand(Name = "CV", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public ValueTask<Option<CallState>> ConnectVisible(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Visible);

	[SharpCommand(Name = "CH", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public ValueTask<Option<CallState>> ConnectHidden(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Hidden);

	private async ValueTask<Option<CallState>> ConnectCoreAsync(IMUSHCodeParser parser, ConnectMode mode)
	{
		// ... the entire existing body of `Connect` (SocketCommands.cs:127-249, read in full during
		// planning), UNCHANGED, up through `await ConnectionService.Bind(parser.CurrentState.Handle!.Value, playerDbRef);` —
		// then, before calling CompletePlayerLoginAsync, insert:

		if (mode != ConnectMode.Normal)
		{
			var connectedPlayer = new AnySharpObject(foundDB);
			if (mode is ConnectMode.Dark or ConnectMode.Hidden && await connectedPlayer.CanHide())
			{
				ConnectionService.Update(handle, "Hidden", "1");
			}

			if (mode is ConnectMode.Dark or ConnectMode.Visible)
			{
				var darkFlag = await Mediator.Send(new GetObjectFlagQuery("DARK"));
				if (darkFlag is not null)
				{
					if (mode == ConnectMode.Dark)
					{
						await Mediator.Send(new SetObjectFlagCommand(connectedPlayer, darkFlag));
					}
					else
					{
						await Mediator.Send(new UnsetObjectFlagCommand(connectedPlayer, darkFlag));
					}
				}
			}
		}

		await CompletePlayerLoginAsync(parser, parser.CurrentState.Handle!.Value, foundDB, playerDbRef);
		// ... rest of existing Connect body unchanged.
	}
```

Guest/token login (`HandleGuestLogin`/`HandleTokenLogin`) are separate methods, untouched by this task — PennMUSH's `cd`/`cv`/`ch` only apply to the plain username+password path.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/HideCommandTests/*"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Commands/SocketCommands.cs SharpMUSH.Tests/Commands/HideCommandTests.cs
git commit -m "feat: add cd/cv/ch login-screen aliases for dark/visible/hidden connect"
```

---

### Task 12: Wire `Hidden` into announcement wording + WHO + `hidden()`

**Files:**
- Modify: `SharpMUSH.Library/Services/ConnectionAnnounceService.cs`
- Modify: `SharpMUSH.Implementation/Commands/SocketCommands.cs` (the `Who` method)
- Modify: `SharpMUSH.Implementation/Functions/ConnectionFunctions.cs` (the `hidden()` function, around line 1449-1474)
- Modify: `SharpMUSH.Library/Definitions/ErrorMessages.cs` (four new `HIDDEN-` wording constants)
- Test: `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs`, `SharpMUSH.Tests/Commands/ConnectionAnnounceIntegrationTests.cs`

**Interfaces:**
- Consumes: `IConnectionService.IsPlayerHiddenAsync(DBRef)` (Task 9); per-connection `ConnectionData.IsHidden` (Task 9).
- Produces: `ConnectionAnnounceService.AnnounceConnectAsync`/`AnnounceDisconnectAsync` now take the caller-supplied hidden-ness into account (signature changes — see Step 3).

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async Task AnnounceConnectAsync_HiddenConnection_UsesHiddenConnectedWording()
{
	var communicationService = Substitute.For<ICommunicationService>();
	var gameBroadcastService = Substitute.For<IGameBroadcastService>();
	var attributeService = Substitute.For<IAttributeService>();
	var mediator = Substitute.For<Mediator.IMediator>();
	var configuration = TestHelpers.FakeOptionsWrapper();

	var service = new ConnectionAnnounceService(communicationService, gameBroadcastService, attributeService, mediator, configuration);
	var player = TestHelpers.FakeConnectedPlayer("Bob");
	var parser = Substitute.For<IMUSHCodeParser>();

	await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: true);

	await gameBroadcastService.Received(1).BroadcastToFlagAsync(
		null, "HEAR_CONNECT", "GAME: Bob has HIDDEN-connected.");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*Hidden*"`
Expected: FAIL — `AnnounceConnectAsync` has no `isHiddenConnection` parameter yet (compile error).

- [ ] **Step 3: Implement**

Add four constants to `ErrorMessages.Notifications` next to the existing four (`ErrorMessages.cs:540-543`):

```csharp
public const string GameHasHiddenConnected = "has HIDDEN-connected.";
public const string GameHasHiddenReconnected = "has HIDDEN-reconnected.";
public const string GameHasHiddenDisconnected = "has HIDDEN-disconnected.";
public const string GameHasPartiallyHiddenDisconnected = "has partially HIDDEN-disconnected.";
```

Change `IConnectionAnnounceService`'s two method signatures to take the hidden flag explicitly (the caller — `Commands.CompletePlayerLoginAsync` / `ConnectionStateEventHandler` — already has the connection's own `IsHidden` at hand, no need for the service to re-derive it):

```csharp
ValueTask AnnounceConnectAsync(IMUSHCodeParser parser, AnySharpObject player, int connectionCount, bool isHiddenConnection);
ValueTask AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections, bool isHiddenConnection);
```

In `ConnectionAnnounceService.AnnounceConnectAsync`, replace the wording selection:

```csharp
		var wording = (isHiddenConnection, connectionCount > 1) switch
		{
			(true, true) => ErrorMessages.Notifications.GameHasHiddenReconnected,
			(true, false) => ErrorMessages.Notifications.GameHasHiddenConnected,
			(false, true) => ErrorMessages.Notifications.GameHasReconnected,
			(false, false) => ErrorMessages.Notifications.GameHasConnected,
		};
```

And in `AnnounceDisconnectAsync`:

```csharp
		var wording = (isHiddenConnection, remainingConnections > 0) switch
		{
			(true, true) => ErrorMessages.Notifications.GameHasPartiallyHiddenDisconnected,
			(true, false) => ErrorMessages.Notifications.GameHasHiddenDisconnected,
			(false, true) => ErrorMessages.Notifications.GameHasPartiallyDisconnected,
			(false, false) => ErrorMessages.Notifications.GameHasDisconnected,
		};
```

Update the two call sites (from Tasks 6/7) to pass the flag: in `SocketCommands.cs`'s `CompletePlayerLoginAsync`, `await ConnectionAnnounceService.AnnounceConnectAsync(parser, new AnySharpObject(player), (int)connectionCount, ConnectionService.Get(handle)?.IsHidden ?? false);`. In `ConnectionStateEventHandler.cs`'s disconnect branch, `await connectionAnnounceService.AnnounceDisconnectAsync(parser, new AnySharpObject(playerNode.AsPlayer), (int)remainingConnections, connectionData.IsHidden);` (`connectionData` is already resolved earlier in that method — reuse it, don't re-fetch).

Update `SocketCommands.cs`'s `Who` method: at the existing `var isDark = await known.HasFlag("DARK");` (line 60) site, also read the row's own `player.IsHidden` — wait, at that point in the loop `player` is the `ConnectionData`/tuple entry, not the resolved object; check the exact loop variable names in the file (`entry.Player`/`known` per the code already read) and gate the row's visibility (currently via `isDark` feeding into the mortal-filter at lines 88-90) with `isDark || entry.Player.IsHidden` for the mortal-hides-the-row decision, while keeping the existing wizard row still shows a marker — extend whatever "(Dark)"-tag logic already exists (line 60 area) to also tag hidden connections, reusing the same visual convention rather than inventing a new one (re-read the surrounding ~30 lines at execution time, since this plan captured lines 25-90 only in summary during initial research, not verbatim for the tag-rendering part).

Update `ConnectionFunctions.cs`'s `hidden()` (around line 1449-1474): replace or supplement the `HasFlag("DARK")` check at line 1472 with `await connectionService.IsPlayerHiddenAsync(target.Object().DBRef) || await target.HasFlag("DARK")` — re-read the function's current body first; it may already take a `DBRef`/`AnySharpObject` parameter directly usable here, or may need `IConnectionService` added to `Functions`' constructor if not already injected (check before assuming).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*"`
Expected: PASS, and re-run the full `ConnectionAnnounceServiceTests` file (Tasks 3-5's tests) to confirm the now-required extra parameter didn't break them — update those call sites to pass `isHiddenConnection: false` explicitly.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Definitions/ErrorMessages.cs SharpMUSH.Library/Services/Interfaces/IConnectionAnnounceService.cs SharpMUSH.Library/Services/ConnectionAnnounceService.cs SharpMUSH.Implementation/Commands/SocketCommands.cs SharpMUSH.Implementation/Functions/ConnectionFunctions.cs SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs
git commit -m "feat: HIDDEN- wording for hidden connections, WHO and hidden() awareness"
```

---

### Task 13: Channel-connect announcements

Port `chat_player_announce`'s core (`extchat.c:3164-3202`, already read during planning), minus the CHATFORMAT/combine per-viewer formatting loop (out of scope, documented in Global Constraints) and minus the `CB_SEEALL` hidden-viewer gate (also documented out of scope).

**Files:**
- Modify: `SharpMUSH.Library/Services/ConnectionAnnounceService.cs`
- Test: `SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs`

**Interfaces:**
- Consumes: `Mediator.CreateStream(new GetOnChannelQuery(AnySharpObject obj))` → `IAsyncEnumerable<SharpChannel>`; `SharpChannelExtensions.HasPriv(this SharpChannel, string)` (`SharpMUSH.Library/Extensions/SharpChannelExtensions.cs:16-20`); `Mediator.Publish(new ChannelMessageNotification(SharpChannel Channel, AnyOptionalSharpObject Source, INotifyService.NotificationType MessageType, MString Message, MString Title, MString PlayerName, MString Says, string[] Options))` (`SharpMUSH.Library/Notifications/ChannelMessageNotification.cs:8-17`); `AnySharpObject.WithNoneOption()` (used identically in `ChannelCommands.cs:61-70`'s `@CEMIT`).
- Produces: a new private `AnnounceOnChannelsAsync(AnySharpObject player, string fullMessage)` called from both `AnnounceConnectAsync` and `AnnounceDisconnectAsync`, inside the existing `if (configuration.CurrentValue.Cosmetic.AnnounceConnects)` block.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async Task AnnounceConnectAsync_PlayerOnNonQuietChannel_PublishesChannelMessage()
{
	var communicationService = Substitute.For<ICommunicationService>();
	var gameBroadcastService = Substitute.For<IGameBroadcastService>();
	var attributeService = Substitute.For<IAttributeService>();
	var mediator = Substitute.For<Mediator.IMediator>();
	var configuration = TestHelpers.FakeOptionsWrapper();

	var service = new ConnectionAnnounceService(communicationService, gameBroadcastService, attributeService, mediator, configuration);
	var player = TestHelpers.FakeConnectedPlayer("Bob");
	var parser = Substitute.For<IMUSHCodeParser>();

	var channel = TestHelpers.FakeChannel("Public", privs: []); // no "Quiet" priv
	mediator.CreateStream(Arg.Any<GetOnChannelQuery>()).Returns(new[] { channel }.ToAsyncEnumerable());

	await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

	await mediator.Received(1).Publish(Arg.Is<ChannelMessageNotification>(n =>
		n.Channel == channel && n.Message.ToPlainText() == "Bob has connected."));
}

[Test]
public async Task AnnounceConnectAsync_PlayerOnQuietChannel_DoesNotPublish()
{
	var communicationService = Substitute.For<ICommunicationService>();
	var gameBroadcastService = Substitute.For<IGameBroadcastService>();
	var attributeService = Substitute.For<IAttributeService>();
	var mediator = Substitute.For<Mediator.IMediator>();
	var configuration = TestHelpers.FakeOptionsWrapper();

	var service = new ConnectionAnnounceService(communicationService, gameBroadcastService, attributeService, mediator, configuration);
	var player = TestHelpers.FakeConnectedPlayer("Bob");
	var parser = Substitute.For<IMUSHCodeParser>();

	var channel = TestHelpers.FakeChannel("Quiet Channel", privs: ["Quiet"]);
	mediator.CreateStream(Arg.Any<GetOnChannelQuery>()).Returns(new[] { channel }.ToAsyncEnumerable());

	await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

	await mediator.DidNotReceive().Publish(Arg.Any<ChannelMessageNotification>());
}
```

`TestHelpers.FakeChannel(name, privs)` almost certainly needs adding (or an inline `new SharpChannel { ... }` construction) — check `SharpChannel`'s `required` fields (`Name`, `Owner`, `Members`, `Privs`) from the model read during planning; `Owner`/`Members` are `AsyncLazy`/`Lazy<IAsyncEnumerable<...>>` and will need trivial stub values (e.g. `new(() => ...)`) even though this test never touches them — confirm the exact construction idiom used elsewhere in this codebase for a bare `SharpChannel` in tests (grep `new SharpChannel` across `SharpMUSH.Tests/`) before writing this literally.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*Channel*"`
Expected: FAIL — no channel-announcement call exists yet.

- [ ] **Step 3: Implement**

```csharp
	private async ValueTask AnnounceOnChannelsAsync(AnySharpObject player, string fullMessage)
	{
		await foreach (var channel in mediator.CreateStream(new GetOnChannelQuery(player)))
		{
			if (channel.HasPriv("Quiet"))
			{
				continue;
			}

			await mediator.Publish(new ChannelMessageNotification(
				channel,
				player.WithNoneOption(),
				INotifyService.NotificationType.Announce,
				MarkupText.Plain(fullMessage),
				MarkupText.Empty,
				MarkupText.Plain(player.Object().Name),
				MarkupText.Empty,
				[]));
		}
	}
```

Confirm `GetOnChannelQuery`'s exact constructor (`GetOnChannelQuery(AnySharpObject Obj)` per `SharpMUSH.Library/Queries/Database/GetChannelQuery.cs:10`, found during planning) and `AnySharpObject.WithNoneOption()`'s exact name/location (used in the `@CEMIT` call site `ChannelCommands.cs:61-70` found during planning — re-read that call site verbatim before typing this, since only its call shape was summarized, not the full surrounding method).

Call this from both `AnnounceConnectAsync` and `AnnounceDisconnectAsync`, inside the existing `if (configuration.CurrentValue.Cosmetic.AnnounceConnects) { ... }` block, right after the room broadcast:

```csharp
			await AnnounceOnChannelsAsync(player, fullMessage);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceServiceTests/*"`
Expected: PASS, whole file green.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/ConnectionAnnounceService.cs SharpMUSH.Tests/Services/ConnectionAnnounceServiceTests.cs
git commit -m "feat: announce connects/disconnects on the player's non-Quiet channels"
```

---

### Task 14: End-to-end integration tests

Verify the whole thing through the real parser and DI container, matching the style of `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs` (real `ServerWebAppFactory`, `NotifyService` substitute captured via DI, assert with `.Received(n)`).

**Files:**
- Create: `SharpMUSH.Tests/Commands/ConnectionAnnounceIntegrationTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// SharpMUSH.Tests/Commands/ConnectionAnnounceIntegrationTests.cs
// Follow the exact fixture pattern in SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:
// [ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)] on the class,
// WebAppFactoryArg.Services for pulling INotifyService/IConnectionService, Parser.CommandParse(...)
// to drive actual connects/disconnects, and NotifyService.Received(n)... for assertions.
//
// Test 1: connect a fresh player (single connection) — assert NotifyService received
//   "Bob has connected." (via the room-broadcast path) exactly once.
// Test 2: connect the SAME player a second time from a second handle while the first stays open —
//   assert the second connect's broadcast says "Bob has reconnected." not "has connected."
// Test 3: disconnect a player's only connection (QUIT or LOGOUT) — assert broadcast says
//   "Bob has disconnected." and (if attribute assertions are feasible via AttributeService in this
//   fixture) that LASTLOGOUT was set.
// Test 4: with two connections open, disconnect one — assert broadcast says
//   "Bob has partially disconnected."
// Test 5: connect via "ch <name> <password>" — assert the broadcast says "Bob has HIDDEN-connected."
//   and that a subsequent WHO (as a mortal viewer) does not list the row, while WHO as a wizard does.
// Test 6: connect via "cd <name> <password>" — assert both the HIDDEN- wording AND that the player's
//   DARK flag is now set (e.g. via AttributeService/HasFlag through the fixture's object query surface).
// Test 7: @hide as a wizard executor, then disconnect — assert "has HIDDEN-disconnected." wording.
// Test 8: join a non-Quiet channel, then connect — assert a ChannelMessageNotification-driven Notify
//   reached the other channel member(s) with the connect wording (or, if NotifyService is the only
//   substitute available in this fixture, assert on the notify call the channel handler ultimately
//   makes — trace ChannelMessageRequestHandler.Handle's final notifyService.Notify(...) call to confirm
//   what's observable here).
// Test 9: same as Test 8 but the channel has the "Quiet" priv set (via @channel/add <chan>=quiet) —
//   assert no channel-side Notify happened, while the room broadcast from Test 1's assertion style
//   still did.
//
// Exact command sequences (CONNECT <name> <password>, QUIT, LOGOUT) and how to open a SECOND
// simultaneous handle for the same player in this test fixture must be resolved by reading
// SharpMUSH.Tests.Infrastructure/ServerWebAppFactory.cs and any existing multi-handle test (grep
// "ConnectionService.Bind" or "second handle" across SharpMUSH.Tests) at execution time — this
// plan cannot prescribe the literal command strings without that research, since login flow
// details (exact CONNECT syntax, whether tests use OTT tokens instead) live in code not yet
// read during planning.
```

This step's literal test bodies are intentionally left for the implementer to fill in after a short, targeted grep (documented above) rather than guessed — everything else in this plan (the service under test, its dependencies, its exact wording) is fully specified; only the test *harness mechanics* for "open two simultaneous connections as the same player in `ServerWebAppFactory`" is genuinely unknown at plan-writing time.

- [ ] **Step 2: Run tests to verify they fail (or don't compile) before the wiring lands**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConnectionAnnounceIntegrationTests/*"`
Expected: FAIL if run before Task 6/7 land; if this task is executed after Task 6/7 (as the plan orders it), some assertions may already pass from the unit-level wiring — that's fine, the point of this task is the end-to-end proof, not a strict red/green boundary against Tasks 3-7's code.

- [ ] **Step 3: Fix any integration gaps the tests reveal**

If a test fails because e.g. `ServerWebAppFactory`'s test `INotifyService` substitute is per-connection rather than global, or connection-count timing races with when `AnnounceConnectAsync` reads `CountAsync()`, fix the root cause in `ConnectionAnnounceService`/its call sites (Tasks 3-7) — do not weaken the test to hide a real ordering bug.

- [ ] **Step 4: Run full suite**

Run: `dotnet run --project SharpMUSH.Tests 2>&1 | tee /tmp/claude-1000/-home-grave-RiderProjects-SharpMUSH--claude-worktrees-sharpmush-markup-string-library-b5d03b/cce047d8-cb29-4e81-b98b-e030d8329ff1/scratchpad/full-test-run.log`

Grep the log for failures rather than relying on truncated terminal output (see project convention: always write full test output to a file, then grep it).

Expected: all tests pass, including this file and every existing test (no regressions from the `ConnectionStateEventHandler` refactor in Task 7).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Tests/Commands/ConnectionAnnounceIntegrationTests.cs
git commit -m "test: add end-to-end coverage for connect/reconnect/disconnect/partial-disconnect announcements"
```

---

## Self-Review Notes

- **Spec coverage:** full-connect wording (Task 3), reconnect wording (Task 3), full-disconnect wording + LASTLOGOUT (Task 5), partial-disconnect wording (Task 5), `HEAR_CONNECT` broadcast incl. `Dark` restriction (Tasks 1, 3, 5), `SUSPECT`/`WIZARD` broadcast (Tasks 3, 5), `ACONNECT`/`ADISCONNECT` on player (Tasks 3, 5), room gated by `RoomConnects` (Tasks 3, 5), zone + master room (Task 4), `AnnounceConnects` gating of room/inventory/channel messages (Tasks 3, 5, 13), wiring into both the connect and disconnect call sites (Tasks 6, 7), per-connection `Hidden` state + aggregate check (Task 9), `@HIDE` corrected to real PennMUSH semantics (Task 10), `cd`/`cv`/`ch` login words (Task 11), `HIDDEN-` wording + WHO + `hidden()` (Task 12), channel-connect announcements with `Quiet`-channel gating (Task 13), end-to-end proof across all of the above (Task 14). Documented out-of-scope, explicitly: PE-registers `%2`-`%5`; PennMUSH's `CB_SEEALL` per-viewer hidden-channel-member gate; DARK-keyed softcode functions beyond `WHO`/`hidden()` (`lwho()` etc., which route through the intentionally-untouched `PermissionService.CanSee`).
- **Placeholder scan:** Tasks 4, 9, 10, 11, 13, and 14 contain deliberately-flagged research gaps (exact test-double shapes for room-contents iteration, `ConnectionService`'s literal constructor/`Register` signature, the test fixture's "handle behind the default executor" accessor, multi-handle/fresh-connect-screen test harness mechanics, and `SharpChannel` test construction) rather than invented fakes that would silently be wrong — each is called out explicitly with a grep to run first, not left as a bare "TODO". This is a narrower exception than "add appropriate error handling"-style placeholders: the *behavior* to test is fully specified in every case, only the *mechanical test-double construction* is deferred to a short lookup against code that exists but wasn't read verbatim during planning.
- **Type consistency:** `AnnounceConnectAsync(IMUSHCodeParser, AnySharpObject, int, bool)` / `AnnounceDisconnectAsync(IMUSHCodeParser, AnySharpObject, int, bool)` — the `bool isHiddenConnection` parameter is added in Task 12 and must be back-filled (as `false`) into every Task 3/5 call site and test at that point; Task 12's Step 4 explicitly calls this out. `QueueHookAsync` and `DispatchZoneAndMasterRoomHooksAsync` signatures introduced in Task 3 are reused unchanged in Tasks 4, 5, 12, 13. `IConnectionService.IsPlayerHiddenAsync(DBRef)` (Task 9) and `ConnectionData.IsHidden` (Task 9) are the two vocabulary items every later Hidden-related task (10-13) builds on — don't introduce a third shape (e.g. a `bool? Hidden` field) anywhere else.
