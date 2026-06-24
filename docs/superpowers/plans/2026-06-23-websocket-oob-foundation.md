# WebSocket Foundation + Event-Handler-Driven OOB — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the browser WebSocket path into a bidirectional, structured channel so the Play sidebar shows live, softcode-filtered room data, with browser NAWS sizing — mechanism in C#, policy in softcode (event handler object #9).

**Architecture:** Three phases. **Phase A (engine):** extend `oob()` into a transport-agnostic structured emit (WebSocket envelope or telnet GMCP) and add a room-scoped `ROOM`CONTENTS` event fired on move/connect/disconnect with softcode-side fan-out. **Phase B (transport + NAWS):** discriminate JSON control frames from commands at the ConnectionServer edge, and add browser grid measurement (Cascadia Mono) that reports NAWS through the existing `NAWSUpdateMessage` path. **Phase C (client):** route inbound OOB frames into a per-connection store and render the live Play sidebar.

**Tech Stack:** .NET 10, Blazor WASM, MudBlazor 9.x, TUnit + bUnit, NSubstitute, NATS, ArangoDB/Memgraph/SurrealDB, ANTLR4 MUSH parser, source-generated Mediator.

## Global Constraints

- **C# files:** tabs, indent size 2. **Razor files:** spaces, indent size 4. **JS:** match existing `wwwroot/js` style.
- `TreatWarningsAsErrors` is enabled in every project — the build fails on warnings.
- Prefer `var`; no `this.` qualifier. Services never return nullable — use `OneOf<>` (existing code).
- **Multi-DB parity:** every engine/event change must behave identically on ArangoDB, Memgraph, SurrealDB. DB-backed tests run via Podman (`DOCKER_HOST` + `RYUK_DISABLED` already configured in this environment).
- **No game policy in the client or C# engine:** the OOB store is a generic keyed cache; room/exit/vitals semantics live only in softcode on object #9.
- **Back-compat:** plain-text command input, and `markup`/`html`/`json` (`wsjson`) envelopes, must keep working unchanged.
- Run a single test: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/<Class>/<Method>"`. bUnit: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/<Class>/*"`.
- Commit after every task (each task ends in a green test).

## Discoveries that shape this plan (verified against source)

- `SharpMUSH.Client/Services/PlayTerminalService.cs`: `PlayTerminalService : TerminalService` and `PlayWebSocketClientService : WebSocketClientService` are **empty subclasses** + marker interfaces. New features added to `TerminalService`/`WebSocketClientService` are inherited by the play stack automatically. **No consolidation work is required** — only "add seams to the base."
- `SharpMUSH.Server/Consumers/InputMessageConsumers.cs:189` `NAWSUpdateConsumer` already consumes `NAWSUpdateMessage(Handle, Height, Width)` and writes `HEIGHT`/`WIDTH` connection metadata. Browser NAWS only needs the ConnectionServer to **publish** that message.
- `oob()` (`JSONFunctions.cs:502`) currently filters to GMCP telnet connections only. `wsjson()` (`HTMLFunctions.cs:63`) emits `{type:"json",data}` to websocket connections only. We unify the structured path in `oob()`.
- `IConnectionService.ConnectionData` exposes `Handle`, `Ref`, `ConnectionType` (`"websocket"`/`"telnet"`), and `Metadata["GMCP"]`. `ConnectionService.Get(DBRef)` returns `IAsyncEnumerable<ConnectionData>` for a player.
- `TerminalFrameRenderer.Parse` already maps the `json` envelope to `TerminalFrameKind.Oob` but discards the payload; `TerminalService.HandleMessage` has `case TerminalFrameKind.Oob: return;`.

---

# Phase A — Engine: transport-agnostic OOB emit + `ROOM`CONTENTS` event

### Task A1: Extract a testable WebSocket-OOB envelope builder

**Files:**
- Create: `SharpMUSH.Implementation/Functions/WebSocketOobEnvelope.cs`
- Test: `SharpMUSH.Tests/Functions/WebSocketOobEnvelopeTests.cs`

**Interfaces:**
- Produces: `public static class WebSocketOobEnvelope { public static string Build(string package, string message); }` — returns `{"type":"oob","package":<package>,"data":<message-as-json-or-string>}`. If `message` is valid JSON it is embedded as JSON; otherwise embedded as a JSON string. Empty/whitespace `message` embeds `null`.

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.Implementation.Functions;

namespace SharpMUSH.Tests.Functions;

public class WebSocketOobEnvelopeTests
{
	[Test]
	[Arguments("room.contents", "{\"who\":[\"#5\"]}", "{\"type\":\"oob\",\"package\":\"room.contents\",\"data\":{\"who\":[\"#5\"]}}")]
	[Arguments("room.exits", "north", "{\"type\":\"oob\",\"package\":\"room.exits\",\"data\":\"north\"}")]
	[Arguments("x", "", "{\"type\":\"oob\",\"package\":\"x\",\"data\":null}")]
	public async Task Build_ProducesEnvelope(string package, string message, string expected)
	{
		await Assert.That(WebSocketOobEnvelope.Build(package, message)).IsEqualTo(expected);
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WebSocketOobEnvelopeTests/*"`
Expected: FAIL — `WebSocketOobEnvelope` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// Builds the out-of-band envelope written to WebSocket (portal) connections:
/// <c>{ "type":"oob", "package":&lt;package&gt;, "data":&lt;json-or-string&gt; }</c>.
/// The browser routes it by <c>package</c> into its OOB channel store. Mechanism only —
/// it ships whatever softcode hands it, with no room/character semantics.
/// </summary>
public static class WebSocketOobEnvelope
{
	public static string Build(string package, string message)
	{
		JsonNode? data;
		if (string.IsNullOrWhiteSpace(message))
		{
			data = null;
		}
		else
		{
			try
			{
				data = JsonNode.Parse(message);
			}
			catch (JsonException)
			{
				data = JsonValue.Create(message);
			}
		}

		var envelope = new JsonObject
		{
			["type"] = "oob",
			["package"] = package,
			["data"] = data
		};

		return envelope.ToJsonString();
	}
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WebSocketOobEnvelopeTests/*"`
Expected: PASS (3 cases).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Functions/WebSocketOobEnvelope.cs SharpMUSH.Tests/Functions/WebSocketOobEnvelopeTests.cs
git commit -m "feat(engine): testable WebSocket OOB envelope builder"
```

---

### Task A2: Route `oob()` to WebSocket connections (in addition to GMCP telnet)

**Files:**
- Modify: `SharpMUSH.Implementation/Functions/JSONFunctions.cs` (the `oob` method, ~lines 558-572 — the per-connection loop)
- Test: `SharpMUSH.Tests/Functions/WebFunctionUnitTests.cs` (add a case alongside the existing `Oob` test)

**Interfaces:**
- Consumes: `WebSocketOobEnvelope.Build` (Task A1); `IConnectionService.ConnectionData.ConnectionType`, `.Metadata`, `.Handle`; `MessageBus!.Publish(...)`.
- Produces: `oob(players, package, json)` now emits `WebSocketOutputMessage(handle, WebSocketOobEnvelope.Build(package, message))` for websocket connections and `GMCPOutputMessage(handle, package, message)` for GMCP-negotiated telnet connections. Return value (sent count) semantics unchanged.

- [ ] **Step 1: Replace the connection loop body**

In `JSONFunctions.cs`, find this exact block inside `oob`:

```csharp
		await foreach (var connection in ConnectionService!.Get(located.Object().DBRef))
		{
			if (connection.Metadata.GetValueOrDefault("GMCP", "0") != "1")
			{
				continue;
			}

			await MessageBus!.Publish(new GMCPOutputMessage(
				connection.Handle,
				package,
				message));

			sentCount++;
		}
```

Replace it with:

```csharp
		await foreach (var connection in ConnectionService!.Get(located.Object().DBRef))
		{
			// WebSocket (portal) connections receive a structured OOB envelope the browser
			// routes by package; GMCP-negotiated telnet connections receive a GMCP package.
			// Any other connection (plain telnet without GMCP) is skipped.
			if (connection.ConnectionType == "websocket")
			{
				await MessageBus!.Publish(new WebSocketOutputMessage(
					connection.Handle,
					WebSocketOobEnvelope.Build(package, message)));
				sentCount++;
			}
			else if (connection.Metadata.GetValueOrDefault("GMCP", "0") == "1")
			{
				await MessageBus!.Publish(new GMCPOutputMessage(
					connection.Handle,
					package,
					message));
				sentCount++;
			}
		}
```

- [ ] **Step 2: Add the test case**

In `SharpMUSH.Tests/Functions/WebFunctionUnitTests.cs`, the existing `Oob` test asserts non-null. Add this test below it (no live websocket connection exists in the function-parser harness, so this asserts the no-connection path returns `"0"` and does not throw):

```csharp
	[Test]
	[Arguments("oob(me, room.contents, {\"who\":[]})", "0")]
	public async Task OobNoConnectionReturnsZero(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
```

- [ ] **Step 3: Run test to verify it passes (and the build compiles with the new envelope path)**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WebFunctionUnitTests/OobNoConnectionReturnsZero"`
Expected: PASS. (If FAIL with a non-zero count, the test harness has a live connection — adjust the expected count to match; the goal is "does not throw and counts sends".)

- [ ] **Step 4: Run the existing web-function tests to confirm no regression**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WebFunctionUnitTests/*"`
Expected: PASS (including the pre-existing `Oob`, `Wsjson`, `Wshtml`).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Functions/JSONFunctions.cs SharpMUSH.Tests/Functions/WebFunctionUnitTests.cs
git commit -m "feat(engine): oob() routes to WebSocket connections via OOB envelope"
```

---

### Task A3: Define the `ROOM`CONTENTS` event constant + firing helper

**Files:**
- Create: `SharpMUSH.Library/Definitions/SharpEvents.cs`
- Test: `SharpMUSH.Tests/Services/SharpEventsTests.cs`

**Interfaces:**
- Produces: `public static class SharpEvents { public const string RoomContents = "ROOM`CONTENTS"; }` — the single source of truth for the event name (a SharpMUSH extension event; not a PennMUSH-parity name).

> The backtick is part of PennMUSH event-attribute naming (e.g. `OBJECT`MOVE`). In a C# verbatim/normal string the backtick is an ordinary character.

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

public class SharpEventsTests
{
	[Test]
	public async Task RoomContentsEventNameMatchesPennMushAttributeFormat()
	{
		await Assert.That(SharpEvents.RoomContents).IsEqualTo("ROOM`CONTENTS");
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SharpEventsTests/*"`
Expected: FAIL — `SharpEvents` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Names of SharpMUSH-fired events (attributes evaluated on the configured event_handler object).
/// PennMUSH-parity events are fired with string literals at their call sites; this holds
/// SharpMUSH extension events so the name has a single definition shared by firer and tests.
/// </summary>
public static class SharpEvents
{
	/// <summary>
	/// Fired (room-scoped, not actor-scoped) whenever a room's visible contents change — an
	/// object enters/leaves, or a player connects/disconnects in it. Args: (roomobjid, cause).
	/// The handler is expected to fan out structured pushes to that room's connected occupants.
	/// </summary>
	public const string RoomContents = "ROOM`CONTENTS";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SharpEventsTests/*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Definitions/SharpEvents.cs SharpMUSH.Tests/Services/SharpEventsTests.cs
git commit -m "feat(engine): ROOM\`CONTENTS event name constant"
```

---

### Task A4: Fire `ROOM`CONTENTS` on movement (both old and new location)

**Files:**
- Modify: `SharpMUSH.Implementation/Handlers/ObjectEventHandlers.cs` (the `Handle(ObjectMovedNotification, …)` method)
- Test: `SharpMUSH.Tests/Services/RoomContentsEventTests.cs`

**Interfaces:**
- Consumes: `SharpEvents.RoomContents` (A3); `IEventService.TriggerEventAsync`; `ObjectMovedNotification.NewLocation` / `.OldLocation` / `.Enactor`.
- Produces: after an object moves, `ROOM`CONTENTS` fires once for the new location (cause `"move-in"`) and once for the old location (cause `"move-out"`), each with the room dbref as `%0`.

> `OldLocation` is a `DBRef` (see the existing `notification.OldLocation.ToString()` use). `NewLocation` is a discriminated union matched in the existing code. We pass each as the room dbref string. Firing for non-room locations is harmless — the handler softcode decides relevance — so we do not filter by type here (keeps C# policy-free).

- [ ] **Step 1: Write the failing integration test**

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Verifies ROOM`CONTENTS fires room-scoped on movement. We install a handler attribute on the
/// configured event_handler (#9) that records the room dbref it was called with, move an object,
/// then read the recorded value back. Runs against whichever DB provider the session selected.
/// </summary>
[NotInParallel]
public class RoomContentsEventTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private Task Cmd(string command) =>
		Parser.CommandParse(1, ConnectionService, MModule.single(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await WebAppFactoryArg.FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	[Test]
	public async ValueTask MoveFiresRoomContentsForNewLocation()
	{
		// Handler records the last room dbref it saw for a move-in into an attribute on #9.
		await Cmd("&ROOM`CONTENTS #9=&LAST_MOVEIN_[secure(%1)] #9=%0");
		// Create a room and a thing, then move the thing into the room.
		await Cmd("@dig/teleport RoomContentsTestRoom");
		await Cmd("@create RoomContentsTestThing");
		// Capture the room dbref, then move the thing there.
		var room = await Eval("loc(me)");
		await Cmd($"@tel RoomContentsTestThing={room}");

		var recorded = await Eval("get(#9/LAST_MOVEIN_move-in)");
		await Assert.That(recorded).IsEqualTo(room);

		// Cleanup handler attribute so it does not affect other tests.
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&LAST_MOVEIN_move-in #9=");
	}
}
```

> If `@dig/teleport`/`@tel` syntax differs in this codebase, adjust to the local building commands (see `SharpMUSH.Tests/Commands/BuildingCommandTests.cs` for the exact verbs). The assertion — handler saw the room dbref under cause `move-in` — must stay.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RoomContentsEventTests/MoveFiresRoomContentsForNewLocation"`
Expected: FAIL — `ROOM`CONTENTS` is never fired, so `LAST_MOVEIN_move-in` is empty (assertion mismatch).

- [ ] **Step 3: Add the firing to the move handler**

In `ObjectEventHandlers.cs`, at the END of `Handle(ObjectMovedNotification notification, …)` (after the existing `OBJECT`MOVE` trigger), append:

```csharp
		// Room-scoped ROOM`CONTENTS so the handler can refresh ALL occupants of the affected
		// rooms (not just the mover): one fire for the destination, one for the origin.
		var newLocDbref = notification.NewLocation.Match(
			player => player.Object.DBRef.ToString(),
			room => room.Object.DBRef.ToString(),
			thing => thing.Object.DBRef.ToString());

		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.RoomContents,
			notification.Enactor,
			newLocDbref,
			"move-in");

		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.RoomContents,
			notification.Enactor,
			notification.OldLocation.ToString(),
			"move-out");
```

Add `using SharpMUSH.Library.Definitions;` to the top of the file if not present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RoomContentsEventTests/MoveFiresRoomContentsForNewLocation"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Handlers/ObjectEventHandlers.cs SharpMUSH.Tests/Services/RoomContentsEventTests.cs
git commit -m "feat(engine): fire ROOM\`CONTENTS on movement for both locations"
```

---

### Task A5: Fire `ROOM`CONTENTS` on connect and disconnect (the player's room)

**Files:**
- Modify: `SharpMUSH.Implementation/Handlers/ConnectionStateEventHandler.cs` (disconnect path) and the `PLAYER`CONNECT` fire site in `SharpMUSH.Implementation/Commands/SocketCommands.cs` (per the event catalog; confirm the exact file by searching for `PLAYER`CONNECT`).
- Test: extend `SharpMUSH.Tests/Services/RoomContentsEventTests.cs`

**Interfaces:**
- Consumes: `SharpEvents.RoomContents`; the connecting/disconnecting player's current room. For connect, resolve via the player object's location at the `PLAYER`CONNECT` site. For disconnect, resolve in `ConnectionStateEventHandler` while `connectionData` is still present (verified available at `PLAYER`DISCONNECT`).
- Produces: `ROOM`CONTENTS` fires for the player's room with cause `"connect"` / `"disconnect"`.

- [ ] **Step 1: Write the failing test (append to RoomContentsEventTests)**

```csharp
	[Test]
	public async ValueTask ConnectFiresRoomContentsForPlayerRoom()
	{
		await Cmd("&ROOM`CONTENTS #9=&LAST_CONNECT_[secure(%1)] #9=%0");

		// Simulate the player (#1) connecting via the connect command path used in tests.
		// Use the same connect entrypoint the socket tests use; here we trigger PLAYER`CONNECT.
		await Cmd("@trigger me/dummy");   // placeholder: replace with the harness's connect trigger
		var room = await Eval("loc(#1)");

		var recorded = await Eval("get(#9/LAST_CONNECT_connect)");
		await Assert.That(recorded).IsEqualTo(room);

		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&LAST_CONNECT_connect #9=");
	}
```

> The connect path in tests differs from a raw socket; replace the placeholder line with the harness's actual login trigger (see `SharpMUSH.Tests` socket/login tests for how `PLAYER`CONNECT` is exercised). If connect cannot be simulated in-process, mark this case `[Skip("requires socket login harness")]` and rely on the disconnect case below + the move case in A4 for coverage. Do not skip silently — keep the `[Skip]` annotation visible.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RoomContentsEventTests/ConnectFiresRoomContentsForPlayerRoom"`
Expected: FAIL (event not fired).

- [ ] **Step 3: Add firing at the connect site and the disconnect site**

At the `PLAYER`CONNECT` trigger site (where `eventService.TriggerEventAsync(parser, "PLAYER`CONNECT", …)` is called), immediately after it add:

```csharp
		// Refresh everyone in the room the player just appeared in.
		var connectRoom = /* the player's current location dbref string at this site */;
		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.RoomContents,
			playerDbref,
			connectRoom,
			"connect");
```

Resolve `connectRoom` using the location lookup already available at that site (the player object is in scope; use its location dbref). In `ConnectionStateEventHandler.Handle`, inside the existing `PLAYER`DISCONNECT` block (where `connectionData` and `notification.PlayerRef` are in scope), after the `PLAYER`DISCONNECT` trigger add:

```csharp
				// Resolve the player's room while the player object is still locatable, then
				// refresh remaining occupants after the disconnect.
				var playerNode = await mediator.Send(new GetObjectNodeQuery(notification.PlayerRef.Value));
				if (!playerNode.IsNone)
				{
					var roomDbref = playerNode.Known.Object().Location?.ToString();
					if (!string.IsNullOrEmpty(roomDbref))
					{
						await eventService.TriggerEventAsync(
							parser,
							SharpEvents.RoomContents,
							notification.PlayerRef.Value,
							roomDbref,
							"disconnect");
					}
				}
```

> Confirm how location is exposed on the resolved object (`Object().Location`, a `Home`/`Location` query, or a `GetLocationQuery`). Use whichever the surrounding code uses to read an object's room; the existing `loc()` function implementation in `SharpMUSH.Implementation/Functions` shows the canonical lookup. Add `using SharpMUSH.Library.Definitions;` and inject `IMediator mediator` into `ConnectionStateEventHandler` if not already present (it already has `IConnectionService`, `IEventService`, `IMUSHCodeParser`, `INotifyService`, `IOptionsWrapper`). If `IMediator` is not constructor-injected, add it.

- [ ] **Step 4: Run the full RoomContents suite to verify pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RoomContentsEventTests/*"`
Expected: PASS (connect case PASS or visibly `[Skip]`-annotated; move + disconnect PASS).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Handlers/ConnectionStateEventHandler.cs SharpMUSH.Implementation/Commands/SocketCommands.cs SharpMUSH.Tests/Services/RoomContentsEventTests.cs
git commit -m "feat(engine): fire ROOM\`CONTENTS on connect/disconnect for the player's room"
```

---

### Task A6: Reference handler softcode + install doc (overridable policy)

**Files:**
- Create: `docs/softcode/room-contents-handler.md` (documented, installable reference policy)
- Test: `SharpMUSH.Tests/Services/RoomContentsHandlerReferenceTests.cs`

**Interfaces:**
- Consumes: `ROOM`CONTENTS` (A4/A5); `oob()` (A2). Produces no C# API — this is reference *policy* demonstrating fan-out to a room's connected occupants, applying admin-chosen exit/contents filtering.

- [ ] **Step 1: Write the doc with the reference handler**

Create `docs/softcode/room-contents-handler.md`:

````markdown
# Reference `ROOM`CONTENTS` handler

Install on the configured event handler (object #9 by default). This is *policy* — change the
filtering, packages, or payload shape freely; the client renders whatever you push.

```
&ROOM`CONTENTS #9=
  @dolist [lcon(%0,connected)]=
    {
      think oob(##, room.contents,
        [json(object,
          who, [json(array, [iter(lcon(%0),json(string,name(itext(0))))])],
          room, [json(string,name(%0))])]);
      think oob(##, room.exits,
        [json(object,
          exits, [json(array, [iter(lexits(%0),
            json(object, name, json(string,name(itext(0))), cmd, json(string,goto itext(0))))])])])
    }
```

`%0` is the affected room dbref; `%1` is the cause (`move-in`/`move-out`/`connect`/`disconnect`).
`lcon(%0,connected)` restricts fan-out to connected occupants. Adjust `lexits`/`lcon` filtering
(e.g. dark/visibility) to taste — that is exactly the "preferred exit filtering" seam.
````

- [ ] **Step 2: Write the failing test (installs handler, asserts fan-out wiring)**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class RoomContentsHandlerReferenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private Task Cmd(string c) => Parser.CommandParse(1, ConnectionService, MModule.single(c)).AsTask();
	private async Task<string> Eval(string e) =>
		(await WebAppFactoryArg.FunctionParser.FunctionParse(MModule.single(e)))!.Message!.ToPlainText();

	[Test]
	public async ValueTask HandlerIteratesConnectedOccupantsWithoutError()
	{
		// A simplified handler that records the count of connected occupants it fanned out to.
		await Cmd("&ROOM`CONTENTS #9=&FANOUT_COUNT #9=[words(lcon(%0,connected))]");
		var room = await Eval("loc(#1)");
		// Fire the event directly through the same path movement uses by moving #1 home and back,
		// or trigger the attribute as the handler:
		await Cmd($"@trigger #9/ROOM`CONTENTS={room},move-in");

		var count = await Eval("get(#9/FANOUT_COUNT)");
		await Assert.That(count).IsEqualTo(await Eval($"words(lcon({room},connected))"));

		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&FANOUT_COUNT #9=");
	}
}
```

- [ ] **Step 3: Run to verify it fails, then passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RoomContentsHandlerReferenceTests/*"`
Expected: FAIL first if `@trigger`/`lcon(...,connected)` syntax needs adjustment; iterate the softcode until the recorded count matches `words(lcon(room,connected))`. This validates that the handler can iterate connected occupants of the room arg. No C# change is needed — only the softcode/doc.

- [ ] **Step 4: Commit**

```bash
git add docs/softcode/room-contents-handler.md SharpMUSH.Tests/Services/RoomContentsHandlerReferenceTests.cs
git commit -m "docs(softcode): reference ROOM\`CONTENTS fan-out handler + wiring test"
```

---

# Phase B — Transport: control frames + browser NAWS

### Task B1: WebSocket control-frame parser (NAWS) at the ConnectionServer edge

**Files:**
- Create: `SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketControlFrame.cs`
- Test: `SharpMUSH.Tests/ConnectionServer/WebSocketControlFrameTests.cs` (create folder if absent)

**Interfaces:**
- Produces: `public static class WebSocketControlFrame { public static bool TryParseNaws(string message, out int cols, out int rows); }` — returns true only for `{"type":"naws","cols":<int>,"rows":<int>}`; clamps each to `[1,1000]`; returns false (and leaves a command to be published) for plain text, non-objects, unknown types, or malformed JSON.

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

public class WebSocketControlFrameTests
{
	[Test]
	public async Task ValidNawsFrameParsesAndClamps()
	{
		var ok = WebSocketControlFrame.TryParseNaws("{\"type\":\"naws\",\"cols\":120,\"rows\":40}", out var cols, out var rows);
		await Assert.That(ok).IsTrue();
		await Assert.That(cols).IsEqualTo(120);
		await Assert.That(rows).IsEqualTo(40);
	}

	[Test]
	public async Task OversizeClampsToThousand()
	{
		WebSocketControlFrame.TryParseNaws("{\"type\":\"naws\",\"cols\":99999,\"rows\":0}", out var cols, out var rows);
		await Assert.That(cols).IsEqualTo(1000);
		await Assert.That(rows).IsEqualTo(1);
	}

	[Test]
	[Arguments("look north")]
	[Arguments("{not json")]
	[Arguments("{\"type\":\"chat\",\"msg\":\"hi\"}")]
	[Arguments("{\"hello\":1}")]
	public async Task NonNawsReturnsFalse(string message)
	{
		await Assert.That(WebSocketControlFrame.TryParseNaws(message, out _, out _)).IsFalse();
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WebSocketControlFrameTests/*"`
Expected: FAIL — type does not exist. (Ensure `SharpMUSH.Tests` references `SharpMUSH.ConnectionServer`; if not, add the project reference in `SharpMUSH.Tests/SharpMUSH.Tests.csproj` `<ProjectReference>` and note it in the commit.)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.Json;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Discriminates browser-sent JSON control frames from ordinary command text on the WebSocket.
/// Plain text and any JSON that is not a recognized control frame are treated as commands.
/// </summary>
public static class WebSocketControlFrame
{
	private static int Clamp(int v) => v < 1 ? 1 : v > 1000 ? 1000 : v;

	public static bool TryParseNaws(string message, out int cols, out int rows)
	{
		cols = 0;
		rows = 0;

		var trimmed = message.AsSpan().TrimStart();
		if (trimmed.Length == 0 || trimmed[0] != '{')
			return false;

		try
		{
			using var doc = JsonDocument.Parse(message);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("type", out var typeEl)
				|| typeEl.ValueKind != JsonValueKind.String
				|| typeEl.GetString() != "naws"
				|| !root.TryGetProperty("cols", out var colsEl) || colsEl.ValueKind != JsonValueKind.Number
				|| !root.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Number)
				return false;

			cols = Clamp(colsEl.GetInt32());
			rows = Clamp(rowsEl.GetInt32());
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WebSocketControlFrameTests/*"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketControlFrame.cs SharpMUSH.Tests/ConnectionServer/WebSocketControlFrameTests.cs
git commit -m "feat(connectionserver): WebSocket NAWS control-frame parser"
```

---

### Task B2: Dispatch NAWS frames in the WebSocket receive loop

**Files:**
- Modify: `SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketServer.cs` (the `WebSocketMessageType.Text` branch in `HandleWebSocketAsync`)

**Interfaces:**
- Consumes: `WebSocketControlFrame.TryParseNaws` (B1); `_publishEndpoint.Publish(new NAWSUpdateMessage(handle, height, width), ct)` — note `NAWSUpdateMessage(Handle, Height, Width)` so `Height=rows`, `Width=cols`.
- Produces: NAWS frames become `NAWSUpdateMessage` (consumed by the existing `NAWSUpdateConsumer`); everything else still becomes `WebSocketInputMessage`. Back-compat preserved.

- [ ] **Step 1: Edit the receive branch**

In `WebSocketServer.cs`, replace this block:

```csharp
				if (result.MessageType == WebSocketMessageType.Text)
				{
					var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

					// Publish user input to MainProcess
					await _publishEndpoint.Publish(
						new WebSocketInputMessage(nextPort, message), ct);
				}
```

with:

```csharp
				if (result.MessageType == WebSocketMessageType.Text)
				{
					var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

					// Browser-sent JSON control frames are handled here and NOT forwarded as commands.
					// NAWS reuses the same NAWSUpdateMessage path telnet uses (Height=rows, Width=cols).
					if (WebSocketControlFrame.TryParseNaws(message, out var cols, out var rows))
					{
						await _publishEndpoint.Publish(
							new NAWSUpdateMessage(nextPort, rows, cols), ct);
					}
					else
					{
						// Publish user input to MainProcess
						await _publishEndpoint.Publish(
							new WebSocketInputMessage(nextPort, message), ct);
					}
				}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build SharpMUSH.ConnectionServer`
Expected: Build succeeded (0 warnings — `TreatWarningsAsErrors`).

- [ ] **Step 3: Run the control-frame unit tests + ConnectionServer build as the regression gate**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WebSocketControlFrameTests/*"`
Expected: PASS. (The dispatch wiring is covered end-to-end in Phase B integration via the client NAWS test; the parser logic is unit-covered in B1.)

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketServer.cs
git commit -m "feat(connectionserver): dispatch WebSocket NAWS frames to NAWSUpdateMessage"
```

---

### Task B3: `SendControlAsync` on the terminal/websocket client (raw, no echo)

**Files:**
- Modify: `SharpMUSH.Client/Services/ITerminalService.cs` (add method)
- Modify: `SharpMUSH.Client/Services/TerminalService.cs` (implement)
- Test: `SharpMUSH.Tests/Client/Services/TerminalServiceControlTests.cs`

**Interfaces:**
- Produces: `Task ITerminalService.SendControlAsync(string controlJson)` — sends the raw JSON over the socket via `wsService.SendAsync`, **without** adding a `TerminalLine` (control frames must not appear in scrollback) and without command-echo.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class TerminalServiceControlTests
{
	[Test]
	public async Task SendControlAsync_SendsRaw_AndDoesNotAddLine()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		var svc = new TerminalService(ws, logger);

		await svc.SendControlAsync("{\"type\":\"naws\",\"cols\":80,\"rows\":24}");

		await ws.Received(1).SendAsync("{\"type\":\"naws\",\"cols\":80,\"rows\":24}");
		await Assert.That(svc.Lines.Count).IsEqualTo(0);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/TerminalServiceControlTests/*"`
Expected: FAIL — `SendControlAsync` not defined.

- [ ] **Step 3: Add to the interface and implementation**

In `ITerminalService.cs`, add below `SendAsync`:

```csharp
	/// <summary>
	/// Send a raw control frame (JSON envelope) to the server without echoing it as a terminal
	/// line. Used for client→server control messages such as NAWS window-size reports.
	/// </summary>
	Task SendControlAsync(string controlJson);
```

In `TerminalService.cs`, add:

```csharp
	/// <inheritdoc/>
	public async Task SendControlAsync(string controlJson)
	{
		await wsService.SendAsync(controlJson);
	}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/TerminalServiceControlTests/*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Services/ITerminalService.cs SharpMUSH.Client/Services/TerminalService.cs SharpMUSH.Tests/Client/Services/TerminalServiceControlTests.cs
git commit -m "feat(client): SendControlAsync for raw control frames"
```

---

### Task B4: Self-host Cascadia Mono + terminal grid CSS

**Files:**
- Create: `SharpMUSH.Client/wwwroot/fonts/cascadia-mono.woff2` (subset: Latin + box-drawing U+2500–257F + block U+2580–259F)
- Modify: `SharpMUSH.Client/wwwroot/index.html` (preload font + ensure `terminalMetrics.js` script tag — added in B5)
- Modify: the terminal stylesheet (find the `.terminal-output`/terminal CSS — `GlobalTerminal.razor` references `_outputId`; locate its CSS in `SharpMUSH.Client/wwwroot/css` or the component's scoped css) to declare `@font-face` and apply the font + grid rules.

**Interfaces:** none (asset + CSS).

- [ ] **Step 1: Add the `@font-face` and terminal font rules**

In the terminal CSS, add:

```css
@font-face {
    font-family: "Cascadia Mono";
    src: url("/fonts/cascadia-mono.woff2") format("woff2");
    font-display: swap;
}

.terminal-output, .terminal-input {
    font-family: "Cascadia Mono", "Sarasa Mono SC", "DejaVu Sans Mono", monospace;
    font-variant-ligatures: none;
    font-feature-settings: "liga" 0, "calt" 0;
    font-kerning: none;
    letter-spacing: 0;
}

.terminal-output {
    scrollbar-gutter: stable;
}
```

> Match the actual class names used by `GlobalTerminal.razor` (inspect its markup for the output container and input element class names) — apply the font rules to those exact selectors.

- [ ] **Step 2: Verify the font loads (manual)**

Run the client (`SharpMUSH.Client` is served on :7102 per project notes) and confirm in DevTools Network that `cascadia-mono.woff2` returns 200 and the terminal renders in Cascadia Mono. Document the check in the commit body.

- [ ] **Step 3: Commit**

```bash
git add SharpMUSH.Client/wwwroot/fonts/cascadia-mono.woff2 SharpMUSH.Client/wwwroot/index.html <terminal-css-file>
git commit -m "feat(client): self-host Cascadia Mono + fixed-grid terminal CSS"
```

---

### Task B5: `terminalMetrics.js` — measure grid + observe resize

**Files:**
- Create: `SharpMUSH.Client/wwwroot/js/terminalMetrics.js`
- Modify: `SharpMUSH.Client/wwwroot/index.html` (add `<script src="js/terminalMetrics.js"></script>`)

**Interfaces:**
- Produces (JS, on `window.SharpMUSH.Metrics`): `measure(elementId) -> {cols, rows}`; `observe(elementId, dotNetRef) -> { dispose }` which debounces (150 ms), dedupes, gates the first measure on `document.fonts.ready`, and calls `dotNetRef.invokeMethodAsync('OnTerminalResize', cols, rows)` on each *changed* size.

- [ ] **Step 1: Create the module**

```javascript
window.SharpMUSH = window.SharpMUSH || {};

// Measures a character grid for a terminal output element rendered in a monospace font, and
// reports NAWS-style {cols, rows} on resize. Advance/line-height are measured from a hidden
// probe (long run averages sub-pixel rounding); never derived from font-size.
window.SharpMUSH.Metrics = {
    measure: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el) return { cols: 80, rows: 24 };

        var cs = getComputedStyle(el);
        var probe = document.createElement('span');
        probe.style.position = 'absolute';
        probe.style.visibility = 'hidden';
        probe.style.whiteSpace = 'pre';
        probe.style.fontFamily = cs.fontFamily;
        probe.style.fontSize = cs.fontSize;
        probe.style.lineHeight = cs.lineHeight;
        probe.style.letterSpacing = cs.letterSpacing;
        probe.style.fontFeatureSettings = cs.fontFeatureSettings;
        el.appendChild(probe);

        probe.textContent = '0'.repeat(200);
        var advance = probe.getBoundingClientRect().width / 200;

        probe.textContent = '0\n0\n0\n0\n0';
        var lineHeight = probe.getBoundingClientRect().height / 5;

        el.removeChild(probe);

        var padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
        var padY = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);
        var contentW = el.clientWidth - padX;
        var contentH = el.clientHeight - padY;

        var clamp = function (v) { return v < 1 ? 1 : (v > 1000 ? 1000 : v); };
        if (!advance || advance <= 0) return { cols: 80, rows: 24 };
        if (!lineHeight || lineHeight <= 0) return { cols: 80, rows: 24 };

        return {
            cols: clamp(Math.floor(contentW / advance)),
            rows: clamp(Math.floor(contentH / lineHeight))
        };
    },

    observe: function (elementId, dotNetRef) {
        var self = this;
        var el = document.getElementById(elementId);
        if (!el) return { dispose: function () { } };

        var last = { cols: 0, rows: 0 };
        var timer = null;

        function fire() {
            var g = self.measure(elementId);
            if (g.cols === last.cols && g.rows === last.rows) return;
            last = g;
            dotNetRef.invokeMethodAsync('OnTerminalResize', g.cols, g.rows);
        }
        function schedule() {
            if (timer) clearTimeout(timer);
            timer = setTimeout(fire, 150);
        }

        var ro = new ResizeObserver(schedule);
        ro.observe(el);

        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(fire);
        } else {
            fire();
        }

        return {
            dispose: function () {
                if (timer) clearTimeout(timer);
                ro.disconnect();
            }
        };
    }
};
```

- [ ] **Step 2: Reference the script in index.html**

In `SharpMUSH.Client/wwwroot/index.html`, alongside the existing `<script src="js/mush-monaco.js">` tag, add:

```html
<script src="js/terminalMetrics.js"></script>
```

- [ ] **Step 3: Manual smoke check**

Load the portal, open the terminal, and in DevTools run `SharpMUSH.Metrics.measure('<output-element-id>')` → expect a sane `{cols, rows}` (e.g. cols 60–200). Document in commit.

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.Client/wwwroot/js/terminalMetrics.js SharpMUSH.Client/wwwroot/index.html
git commit -m "feat(client): terminalMetrics.js grid measurement + resize observer"
```

---

### Task B6: Wire NAWS from `GlobalTerminal` to the server

**Files:**
- Modify: `SharpMUSH.Client/Components/GlobalTerminal.razor` (`@code`: add JS-invokable resize handler, start observing on first render, dispose on teardown)

**Interfaces:**
- Consumes: `SharpMUSH.Metrics.observe` (B5); `ITerminalService.SendControlAsync` (B3); the output element id (`_outputId`).
- Produces: `[JSInvokable] Task OnTerminalResize(int cols, int rows)` → sends `{"type":"naws","cols":C,"rows":R}` when connected.

- [ ] **Step 1: Add the resize wiring to `GlobalTerminal.razor` `@code`**

Add fields and methods (mirroring the existing `attachCommandLinks` interop pattern using `DotNetObjectReference` and `IJSObjectReference _cmdLinkHandle`):

```csharp
	private DotNetObjectReference<GlobalTerminal>? _metricsRef;
	private IJSObjectReference? _metricsHandle;

	[JSInvokable]
	public async Task OnTerminalResize(int cols, int rows)
	{
		if (!Terminal.IsConnected) return;
		await Terminal.SendControlAsync($"{{\"type\":\"naws\",\"cols\":{cols},\"rows\":{rows}}}");
	}
```

In the existing `OnAfterRenderAsync(bool firstRender)` (where `attachCommandLinks` is already attached), after that attach add:

```csharp
			_metricsRef = DotNetObjectReference.Create(this);
			_metricsHandle = await JsRuntime.InvokeAsync<IJSObjectReference>(
				"SharpMUSH.Metrics.observe", _outputId, _metricsRef);
```

In the component's `DisposeAsync`/`Dispose` (where `_cmdLinkHandle` is disposed), add:

```csharp
		if (_metricsHandle is not null)
		{
			try { await _metricsHandle.InvokeVoidAsync("dispose"); } catch { /* JS gone */ }
			await _metricsHandle.DisposeAsync();
		}
		_metricsRef?.Dispose();
```

> `Terminal` is the component's terminal-service field (the injected `ITerminalService`, or the `TerminalOverride` when set). Use the same field the component already uses to call `SendAsync`.

- [ ] **Step 2: Build the client**

Run: `dotnet build SharpMUSH.Client`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Manual end-to-end check**

Run Server + ConnectionServer + Client, connect the terminal, resize the window/drawer, and confirm (server logs at debug) `NAWSUpdateMessage received` with changing Width/Height, and that `width()`/`height()` in-game reflect the pane. Document in commit.

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.Client/Components/GlobalTerminal.razor
git commit -m "feat(client): report browser NAWS window size to the server"
```

---

# Phase C — Client: OOB store + live Play sidebar

### Task C1: Surface `(package, data)` from `TerminalFrameRenderer`

**Files:**
- Modify: `SharpMUSH.Client/Services/TerminalFrameRenderer.cs` (the `TerminalFrame` record + `oob`/`json` parsing)
- Test: `SharpMUSH.Tests/Client/Services/TerminalFrameRendererTests.cs`

**Interfaces:**
- Produces: `readonly record struct TerminalFrame(TerminalFrameKind Kind, string Plain, string Html, string Package, string DataJson)`. For `oob` and legacy `json` envelopes, `Kind = Oob`, `Package` = the `package` string (empty for legacy `json`), `DataJson` = the raw JSON text of `data`. All other frame kinds set `Package`/`DataJson` to empty.

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class TerminalFrameRendererTests
{
	[Test]
	public async Task OobEnvelopeSurfacesPackageAndData()
	{
		var frame = TerminalFrameRenderer.Parse("{\"type\":\"oob\",\"package\":\"room.contents\",\"data\":{\"who\":[\"#5\"]}}");
		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Oob);
		await Assert.That(frame.Package).IsEqualTo("room.contents");
		await Assert.That(frame.DataJson).IsEqualTo("{\"who\":[\"#5\"]}");
	}

	[Test]
	public async Task LegacyJsonEnvelopeHasEmptyPackage()
	{
		var frame = TerminalFrameRenderer.Parse("{\"type\":\"json\",\"data\":{\"x\":1}}");
		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Oob);
		await Assert.That(frame.Package).IsEqualTo(string.Empty);
		await Assert.That(frame.DataJson).IsEqualTo("{\"x\":1}");
	}

	[Test]
	public async Task MarkupEnvelopeStillRenders()
	{
		var frame = TerminalFrameRenderer.Parse("{\"type\":\"markup\",\"data\":\"\\\"hello\\\"\"}");
		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Markup);
		await Assert.That(frame.Package).IsEqualTo(string.Empty);
	}
}
```

> The exact serialized-MString form for the markup case may need adjusting to a valid `MModule.deserialize` input; if unsure, keep only the first two tests (the `Oob`/`json` paths are the change under test) and leave the existing markup behavior covered by integration.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/TerminalFrameRendererTests/*"`
Expected: FAIL — `TerminalFrame` has no `Package`/`DataJson`.

- [ ] **Step 3: Edit the renderer**

Change the record:

```csharp
public readonly record struct TerminalFrame(TerminalFrameKind Kind, string Plain, string Html, string Package, string DataJson);
```

Update every existing `new TerminalFrame(...)` 3-arg construction to pass `string.Empty, string.Empty` for the two new fields. Then replace the `case "json":` arm with handling for both `oob` and `json`:

```csharp
				case "oob":
				case "json":
				{
					var package = GetStringProperty(root, "package");
					var dataJson = root.TryGetProperty("data", out var dataEl)
						? dataEl.GetRawText()
						: string.Empty;
					return new TerminalFrame(TerminalFrameKind.Oob, string.Empty, string.Empty, package, dataJson);
				}
```

For the other arms (`markup`, `html`, default, plaintext, and the early plaintext returns), add `, string.Empty, string.Empty`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/TerminalFrameRendererTests/*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Services/TerminalFrameRenderer.cs SharpMUSH.Tests/Client/Services/TerminalFrameRendererTests.cs
git commit -m "feat(client): surface package+data from OOB frames"
```

---

### Task C2: `OobChannelStore` — per-connection keyed cache

**Files:**
- Create: `SharpMUSH.Client/Services/IOobChannelStore.cs`
- Create: `SharpMUSH.Client/Services/OobChannelStore.cs`
- Test: `SharpMUSH.Tests/Client/Services/OobChannelStoreTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IOobChannelStore
{
	event Action<string>? ChannelUpdated;       // raised with the package name
	void Set(string package, string dataJson);  // ignores empty package
	string? Get(string package);                // latest raw JSON, or null
	IReadOnlyCollection<string> Packages { get; }
}
```

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class OobChannelStoreTests
{
	[Test]
	public async Task SetThenGetReturnsLatestAndRaisesEvent()
	{
		var store = new OobChannelStore();
		string? raised = null;
		store.ChannelUpdated += p => raised = p;

		store.Set("room.contents", "{\"who\":[]}");
		store.Set("room.contents", "{\"who\":[\"#5\"]}");

		await Assert.That(store.Get("room.contents")).IsEqualTo("{\"who\":[\"#5\"]}");
		await Assert.That(raised).IsEqualTo("room.contents");
		await Assert.That(store.Packages).Contains("room.contents");
	}

	[Test]
	public async Task EmptyPackageIsIgnored()
	{
		var store = new OobChannelStore();
		store.Set("", "{\"x\":1}");
		await Assert.That(store.Packages.Count).IsEqualTo(0);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/OobChannelStoreTests/*"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`IOobChannelStore.cs`:

```csharp
namespace SharpMUSH.Client.Services;

/// <summary>
/// A generic, per-connection cache of the latest payload for each OOB package/channel. Holds no
/// knowledge of room/character semantics — consumers (e.g. the Play sidebar) interpret the JSON.
/// </summary>
public interface IOobChannelStore
{
	event Action<string>? ChannelUpdated;
	void Set(string package, string dataJson);
	string? Get(string package);
	IReadOnlyCollection<string> Packages { get; }
}
```

`OobChannelStore.cs`:

```csharp
using System.Collections.Concurrent;

namespace SharpMUSH.Client.Services;

public sealed class OobChannelStore : IOobChannelStore
{
	private readonly ConcurrentDictionary<string, string> _channels = new();

	public event Action<string>? ChannelUpdated;

	public void Set(string package, string dataJson)
	{
		if (string.IsNullOrEmpty(package)) return;
		_channels[package] = dataJson;
		ChannelUpdated?.Invoke(package);
	}

	public string? Get(string package) =>
		_channels.TryGetValue(package, out var v) ? v : null;

	public IReadOnlyCollection<string> Packages => _channels.Keys.ToArray();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/OobChannelStoreTests/*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Services/IOobChannelStore.cs SharpMUSH.Client/Services/OobChannelStore.cs SharpMUSH.Tests/Client/Services/OobChannelStoreTests.cs
git commit -m "feat(client): generic OOB channel store"
```

---

### Task C3: Route OOB frames from `TerminalService` into the store

**Files:**
- Modify: `SharpMUSH.Client/Services/ITerminalService.cs` (expose `IOobChannelStore OobChannels { get; }`)
- Modify: `SharpMUSH.Client/Services/TerminalService.cs` (own a store; route `Oob` frames into it)
- Test: `SharpMUSH.Tests/Client/Services/TerminalServiceOobTests.cs`

**Interfaces:**
- Consumes: `OobChannelStore` (C2); `TerminalFrame.Package`/`.DataJson` (C1).
- Produces: `ITerminalService.OobChannels` — a store **owned per terminal instance** (so the command and play connections have independent stores). The `case TerminalFrameKind.Oob:` arm of `HandleMessage` calls `_oob.Set(frame.Package, frame.DataJson)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class TerminalServiceOobTests
{
	[Test]
	public async Task IncomingOobFrameIsRoutedToStore()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		var logger = Substitute.For<ILogger<TerminalService>>();
		var svc = new TerminalService(ws, logger);

		// Drive a message through the ws MessageReceived event after connecting wires handlers.
		await svc.ConnectAsync("ws://localhost:4202/ws");
		ws.MessageReceived += Raise.Event<EventHandler<string>>(ws,
			"{\"type\":\"oob\",\"package\":\"room.contents\",\"data\":{\"who\":[\"#7\"]}}");

		await Assert.That(svc.OobChannels.Get("room.contents")).IsEqualTo("{\"who\":[\"#7\"]}");
		await Assert.That(svc.Lines.Any(l => l.Text.Contains("room.contents"))).IsFalse();
	}
}
```

> `ConnectAsync` calls `wsService.ConnectAsync`, which on the substitute is a no-op returning a completed task — fine. The substitute's `MessageReceived` is raised manually to simulate a server frame.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/TerminalServiceOobTests/*"`
Expected: FAIL — `OobChannels` not defined.

- [ ] **Step 3: Implement**

In `ITerminalService.cs`, add:

```csharp
	/// <summary>Latest out-of-band channel payloads received on this connection.</summary>
	IOobChannelStore OobChannels { get; }
```

In `TerminalService.cs`, add a field and property:

```csharp
	private readonly OobChannelStore _oob = new();

	/// <inheritdoc/>
	public IOobChannelStore OobChannels => _oob;
```

Replace the existing OOB arm:

```csharp
			case TerminalFrameKind.Oob:
				// Structured out-of-band data: not for direct display (future hook).
				return;
```

with:

```csharp
			case TerminalFrameKind.Oob:
				// Structured out-of-band data: route to the channel store; never displayed.
				_oob.Set(frame.Package, frame.DataJson);
				return;
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/TerminalServiceOobTests/*"`
Expected: PASS. Also run the prior client service tests to confirm no regression:
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/TerminalServiceControlTests/*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Services/ITerminalService.cs SharpMUSH.Client/Services/TerminalService.cs SharpMUSH.Tests/Client/Services/TerminalServiceOobTests.cs
git commit -m "feat(client): route OOB frames into per-connection channel store"
```

---

### Task C4: Sidebar entry model + parser

**Files:**
- Create: `SharpMUSH.Client/Models/OobEntry.cs`
- Create: `SharpMUSH.Client/Services/OobEntryParser.cs`
- Test: `SharpMUSH.Tests/Client/Services/OobEntryParserTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record OobEntry(string Dbref, string Name, string? Cmd);
public static class OobEntryParser
{
	// Parses a room.contents/room.exits payload of shape {"who":[{...}]} or {"exits":[{...}]}
	// where each item is {"dbref"?,"name"?,"cmd"?} or a bare string name. Returns [] on malformed input.
	public static IReadOnlyList<OobEntry> Parse(string? dataJson, string arrayProperty);
}
```
Entries with no name render as untitled (caller decides); the parser keeps blanks rather than dropping them, per "no game policy."

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class OobEntryParserTests
{
	[Test]
	public async Task ParsesObjectEntries()
	{
		var json = "{\"who\":[{\"dbref\":\"#5\",\"name\":\"Bob\",\"cmd\":\"look #5\"}]}";
		var entries = OobEntryParser.Parse(json, "who");
		await Assert.That(entries.Count).IsEqualTo(1);
		await Assert.That(entries[0].Name).IsEqualTo("Bob");
		await Assert.That(entries[0].Cmd).IsEqualTo("look #5");
	}

	[Test]
	public async Task ParsesBareStringEntries()
	{
		var entries = OobEntryParser.Parse("{\"exits\":[\"north\",\"south\"]}", "exits");
		await Assert.That(entries.Count).IsEqualTo(2);
		await Assert.That(entries[1].Name).IsEqualTo("south");
	}

	[Test]
	[Arguments(null)]
	[Arguments("not json")]
	[Arguments("{\"who\":\"oops\"}")]
	public async Task MalformedReturnsEmpty(string? json)
	{
		await Assert.That(OobEntryParser.Parse(json, "who").Count).IsEqualTo(0);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/OobEntryParserTests/*"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`OobEntry.cs`:

```csharp
namespace SharpMUSH.Client.Models;

/// <summary>A single clickable sidebar entry pushed by softcode (shape is softcode-defined).</summary>
public sealed record OobEntry(string Dbref, string Name, string? Cmd);
```

`OobEntryParser.cs`:

```csharp
using System.Text.Json;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Parses an OOB array payload into <see cref="OobEntry"/> items. The client imposes no schema
/// beyond "an array under <paramref name="arrayProperty"/> of objects or bare strings"; anything
/// else yields an empty list rather than throwing.
/// </summary>
public static class OobEntryParser
{
	public static IReadOnlyList<OobEntry> Parse(string? dataJson, string arrayProperty)
	{
		if (string.IsNullOrWhiteSpace(dataJson)) return [];

		try
		{
			using var doc = JsonDocument.Parse(dataJson);
			if (doc.RootElement.ValueKind != JsonValueKind.Object
				|| !doc.RootElement.TryGetProperty(arrayProperty, out var arr)
				|| arr.ValueKind != JsonValueKind.Array)
				return [];

			var result = new List<OobEntry>();
			foreach (var item in arr.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					result.Add(new OobEntry(string.Empty, item.GetString() ?? string.Empty, null));
				}
				else if (item.ValueKind == JsonValueKind.Object)
				{
					var dbref = item.TryGetProperty("dbref", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : string.Empty;
					var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : string.Empty;
					var cmd = item.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
					result.Add(new OobEntry(dbref, name, cmd));
				}
			}
			return result;
		}
		catch (JsonException)
		{
			return [];
		}
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/OobEntryParserTests/*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Models/OobEntry.cs SharpMUSH.Client/Services/OobEntryParser.cs SharpMUSH.Tests/Client/Services/OobEntryParserTests.cs
git commit -m "feat(client): OOB sidebar entry model + tolerant parser"
```

---

### Task C5: Live Play sidebar ("Here" / "Exits")

**Files:**
- Modify: `SharpMUSH.Client/Pages/Play.razor` (replace the static "Here"/"Exits" cards with live, subscribed content)
- Test: `SharpMUSH.Tests.BUnit/Components/PlaySidebarTests.cs`

**Interfaces:**
- Consumes: `IPlayTerminalService.OobChannels` (C3); `OobEntryParser` (C4); `OobEntry` (C4). Packages: `room.contents` (array prop `who`), `room.exits` (array prop `exits`). Clicking an entry with a `Cmd` calls `PlayTerminal.SendAsync(entry.Cmd)`.

- [ ] **Step 1: Write the failing bUnit test**

```csharp
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

public class PlaySidebarTests : BunitContext
{
	public PlaySidebarTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task Sidebar_RendersPushedRoomContents()
	{
		var store = new OobChannelStore();
		var play = Substitute.For<IPlayTerminalService>();
		play.OobChannels.Returns(store);
		Services.AddSingleton(play);
		// Register any other services Play.razor injects with substitutes as needed.

		var cut = Render<Play>();

		store.Set("room.contents", "{\"who\":[{\"dbref\":\"#5\",\"name\":\"Bob\",\"cmd\":\"look #5\"}]}");

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Bob"))
				throw new InvalidOperationException("contents not rendered yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("Bob");
	}
}
```

> `Play.razor` likely injects several services (terminal, connection-state, nav). Register substitutes for each constructor/inject dependency so `Render<Play>()` succeeds — inspect the `@inject` lines at the top of `Play.razor` and add a `Substitute.For<…>()` for each. Keep the assertion (pushed contents render).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/PlaySidebarTests/*"`
Expected: FAIL — sidebar still static, "Bob" not present.

- [ ] **Step 3: Implement the live sidebar in `Play.razor`**

Add `@inject` if not present and `@implements IDisposable`. Replace the two static cards:

```razor
<div class="play-card">
    <div class="play-fieldlabel">Here</div>
    @if (_here.Count == 0)
    {
        <div class="play-empty">— Connect to see who's here —</div>
    }
    else
    {
        @foreach (var e in _here)
        {
            <div class="play-entry @(e.Cmd is null ? "" : "play-clickable")"
                 @onclick="@(() => RunEntry(e))">
                @(string.IsNullOrWhiteSpace(e.Name) ? "(untitled)" : e.Name)
            </div>
        }
    }
</div>

<div class="play-card">
    <div class="play-fieldlabel">Exits</div>
    @if (_exits.Count == 0)
    {
        <div class="play-empty">— No exits available —</div>
    }
    else
    {
        @foreach (var e in _exits)
        {
            <div class="play-entry @(e.Cmd is null ? "" : "play-clickable")"
                 @onclick="@(() => RunEntry(e))">
                @(string.IsNullOrWhiteSpace(e.Name) ? "(untitled)" : e.Name)
            </div>
        }
    }
</div>
```

In `@code`:

```csharp
	[Inject] private IPlayTerminalService PlayTerminal { get; set; } = default!;

	private IReadOnlyList<OobEntry> _here = [];
	private IReadOnlyList<OobEntry> _exits = [];

	protected override void OnInitialized()
	{
		PlayTerminal.OobChannels.ChannelUpdated += OnChannelUpdated;
		RefreshFromStore("room.contents");
		RefreshFromStore("room.exits");
	}

	private void OnChannelUpdated(string package) => RefreshFromStore(package);

	private void RefreshFromStore(string package)
	{
		switch (package)
		{
			case "room.contents":
				_here = OobEntryParser.Parse(PlayTerminal.OobChannels.Get(package), "who");
				break;
			case "room.exits":
				_exits = OobEntryParser.Parse(PlayTerminal.OobChannels.Get(package), "exits");
				break;
			default:
				return;
		}
		InvokeAsync(StateHasChanged);
	}

	private async Task RunEntry(OobEntry entry)
	{
		if (!string.IsNullOrEmpty(entry.Cmd))
			await PlayTerminal.SendAsync(entry.Cmd);
	}

	public void Dispose() => PlayTerminal.OobChannels.ChannelUpdated -= OnChannelUpdated;
```

> If `Play.razor` already injects `IPlayTerminalService` under another name, reuse it instead of adding a second inject. Match the existing `play-card`/`play-empty` CSS classes; add `.play-entry`/`.play-clickable` styles (cursor pointer + hover) to the Play page CSS.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/PlaySidebarTests/*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/Pages/Play.razor SharpMUSH.Tests.BUnit/Components/PlaySidebarTests.cs
git commit -m "feat(client): live Play sidebar from OOB room.contents/room.exits"
```

---

### Task C6: Full-suite regression + manual end-to-end verification

**Files:** none (verification task).

- [ ] **Step 1: Run the full unit + integration suite (selected provider)**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*/*" --output detailed 2>&1 | tee /tmp/claude-1000/-home-grave-RiderProjects-SharpMUSH-web-portal/c41dca3e-2fa6-4b28-a760-a96f7885a6cb/scratchpad/full-test.log`
Then grep failures: `grep -iE "fail|error" .../full-test.log`
Expected: no failures attributable to this work. (Per `[[test-output-to-logfile]]`, always write full output to a file and grep it — tails truncate failing names.)

- [ ] **Step 2: Run bUnit suite**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/*/*"`
Expected: PASS.

- [ ] **Step 3: Repeat the integration suite on Memgraph (provider parity)**

Run: `SHARPMUSH_DATABASE_PROVIDER=memgraph dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RoomContentsEventTests/*"`
Then the SurrealDB provider per its env (`SHARPMUSH_DATABASE_PROVIDER=surrealdb`).
Expected: PASS on all three.

- [ ] **Step 4: Manual end-to-end (two browsers)**

Run Server + ConnectionServer + Client (`[[run-and-test-sharpmush-live]]`). Install the reference handler from `docs/softcode/room-contents-handler.md` on #9. Open `/play` as two characters in the same room; move/connect/disconnect one and confirm the **other's** sidebar "Here"/"Exits" updates live, exits are clickable, and the window resize changes `width()`/`height()`. Capture a screenshot.

- [ ] **Step 5: Commit any fixups**

```bash
git add -A
git commit -m "test: full-suite + 3-provider + manual e2e verification for WebSocket OOB foundation"
```

---

## Self-Review

**Spec coverage:**
- Bidirectional frame protocol → B1, B2 (control in), C1 (`oob` channel out); plain-text back-compat preserved in B2. ✓
- Stack consolidation → resolved as already-DRY (subclass design); new seams added to base `TerminalService` (B3, C3) inherited by `PlayTerminalService`. Noted in Discoveries. ✓
- NAWS → B1–B6 (parser, dispatch, font, metrics JS, wiring), reusing existing `NAWSUpdateConsumer`. ✓
- Transport-agnostic OOB emit → A1, A2. ✓
- `ROOM`CONTENTS` event + fan-out → A3, A4, A5; reference handler → A6. ✓
- Client OOB store + live sidebar → C1–C5. ✓
- Error handling: clamp/ignore (B1), tolerant parse (C1, C4), control frames never echoed (B3), empty package ignored (C2). ✓
- Testing across 3 providers → C6 Step 3; Podman per env. ✓
- Known limitations (wide-char wrapping, prompt frame, Nerd Font) → documented in spec; no tasks (out of scope). ✓

**Placeholder scan:** The connect-site location lookup (A5) and a few "match local building-command syntax / `Play.razor` injects" notes are explicit verification instructions, not silent TODOs — each names exactly what to confirm and where. Acceptable as plan guidance because the precise local API (e.g. how an object's location dbref is read) must be read at the call site.

**Type consistency:** `TerminalFrame(Kind, Plain, Html, Package, DataJson)` used consistently across C1/C3; `IOobChannelStore` members (`Set`/`Get`/`Packages`/`ChannelUpdated`) consistent across C2/C3/C5; `OobEntry(Dbref, Name, Cmd)` consistent across C4/C5; `SharpEvents.RoomContents` consistent across A3/A4/A5; `WebSocketControlFrame.TryParseNaws` consistent across B1/B2; `SendControlAsync` consistent across B3/B6.

## Open items to confirm during execution (read at the call site)
- A5: exact API to read a player object's room dbref (mirror `loc()`); whether `ConnectionStateEventHandler` already injects `IMediator`.
- A4/A6: local building-command verbs (`@dig`/`@tel`/`@trigger`) and `lcon(room,connected)` filter spelling.
- B1: add `SharpMUSH.ConnectionServer` project reference to `SharpMUSH.Tests` if absent.
- B4/B6: exact terminal output container element id (`_outputId`) and CSS class names in `GlobalTerminal.razor`.
- C5: the full `@inject` list in `Play.razor` to register bUnit substitutes.
