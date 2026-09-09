# Movement and Automatic Behavior Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring SharpMUSH's movement, its attribute triads, and its automatic look to PennMUSH 1.8.8 parity, on a shared `did_it` primitive.

**Architecture:** A new `IDidItService` ports PennMUSH's `real_did_it`/`fail_lock` (message to actor, one evaluated o-message to the room, queued action attribute). `MoveService` is rewritten as a transcription of `move.c`'s `moveit`/`enter_room`/`safe_tel`. The ~380-line `LOOK` body moves into `SharpMUSH.Library` as `ILookService` so `enter_room` can call the automatic look synchronously. Every remaining hand-rolled triad in the codebase adopts the primitive.

**Tech Stack:** .NET 10, C# (tabs, indent 2), source-generated Mediator, TUnit tests via `ServerWebAppFactory`, FusionCache through the `ICacheable`/`ICacheInvalidating` request policy.

**Spec:** `docs/superpowers/specs/2026-09-09-movement-behavior-design.md`

## Global Constraints

- **C# formatting is gated.** Tabs, indent size 2. A build failing `FORMAT001` is fixed with
  `dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"`,
  run **twice** — the formatter needs two passes to converge.
- **`TreatWarningsAsErrors` is on** in `SharpMUSH.Library`, `SharpMUSH.Implementation`,
  `SharpMUSH.Tests`, `SharpMUSH.Tests.Infrastructure` and `SharpMUSH.Tests.Integration`. It is
  **off** in `SharpMUSH.Tests.BUnit` and `SharpMUSH.Tests.ScenePlugin`.
- **No historical comments.** This is pre-release software. Never write "previously", "used to",
  "replaced by", or any changelog-shaped note in code or docs.
- **Prefer `var`; no `this.` qualifier.** Services are non-null constructor members, never static
  and never null-forgiven.
- **`OneOf<T1,T2>` for discriminated results**, never a nullable return from a service.
- **All state access goes through the Mediator.** No handler holds an `IFusionCache`; no service
  writes through a store directly. A constructor cycle is broken with `Lazy<T>` (registered as the
  open generic `LazyService<T>`), never with a request whose handler calls a service.
- **Test command:** `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/<Class>/*"`.
  Always redirect full output to a file and grep it; `tail`/`head` truncates failing test names.
- **Podman, not Docker,** backs Testcontainers here: `DOCKER_HOST` and `RYUK_DISABLED` must be set.
- PennMUSH source for every citation below is at `/home/grave/RiderProjects/SharpMUSH/pennmush/src`.

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `SharpMUSH.Library/Services/Interfaces/IDidItService.cs` | `DidItRequest` record, `DidIt`, `FailLock` |
| `SharpMUSH.Library/Services/DidItService.cs` | The `real_did_it`/`fail_lock` port |
| `SharpMUSH.Library/Definitions/LockMessages.cs` | Penn's `lock_msgs` table as a lookup |
| `SharpMUSH.Library/Services/Interfaces/ILookService.cs` | `LookKey`, `LookRoom`, `LookAt` |
| `SharpMUSH.Library/Services/LookService.cs` | The look body moved out of `GeneralCommands` |
| `SharpMUSH.Tests/Services/DidItServiceTests.cs` | Triad semantics |
| `SharpMUSH.Tests/Commands/MovementParityTests.cs` | Movement behavior against Penn |
| `SharpMUSH.Tests/Services/LookServiceTests.cs` | Terse auto-look and description recursion |
| `SharpMUSH.Tests/Services/QueueQuotaTests.cs` | Runaway halt |

**Modified**

| File | Change |
|---|---|
| `SharpMUSH.Library/Services/MoveService.cs` | Rewritten as `MoveIt`/`EnterRoom`/`SafeTel` |
| `SharpMUSH.Library/Services/Interfaces/IMoveService.cs` | New surface |
| `SharpMUSH.Library/Services/PermissionService.cs` | Real `CanGoto`; new `IsHearer` |
| `SharpMUSH.Library/Services/CommunicationService.cs` | `InteractType` parameter |
| `SharpMUSH.Library/Services/TaskScheduler.cs` | Per-owner queue quota, runaway halt |
| `SharpMUSH.Library/ParserInterfaces/ParserState.cs` | `MoveDepth` counter |
| `SharpMUSH.Implementation/Commands/GeneralCommands.cs` | `LOOK` delegates; `GOTO`, `@teleport` rewired |
| `SharpMUSH.Implementation/Commands/MoreCommands.cs` | Triads replaced by `DidIt`; `HOME`, `ENTER`, `LEAVE` |
| `SharpMUSH.Server/Startup.cs` | Register `IDidItService`, `ILookService` |
| `SharpMUSH.Tests/Services/MoveServiceTests.cs` | Stubs replaced with real tests |

---

## Phase A — Primitives

### Task 1: `IsHearer` on the permission service

**Files:**
- Modify: `SharpMUSH.Library/Services/Interfaces/IPermissionService.cs`
- Modify: `SharpMUSH.Library/Services/PermissionService.cs`
- Test: `SharpMUSH.Tests/Services/DidItServiceTests.cs` (created here)

**Interfaces:**
- Produces: `ValueTask<bool> IPermissionService.IsHearer(AnySharpObject obj)`

PennMUSH `Hearer` (`src/game.c:1564`): a connected player, a `Puppet`, an `Audible` object with a
`FORWARDLIST` attribute, or any object with a `LISTEN` attribute.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class DidItServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IPermissionService PermissionService => WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;

	private async Task<DBRef> Thing(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName(prefix));

	[Test]
	public async ValueTask PlainThingIsNotAHearer()
	{
		var thing = await Thing("Deaf");
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsFalse();
	}

	[Test]
	public async ValueTask ThingWithListenIsAHearer()
	{
		var thing = await Thing("Ears");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&LISTEN {thing}=*"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsTrue();
	}

	[Test]
	public async ValueTask PuppetIsAHearer()
	{
		var thing = await Thing("Puppet");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {thing}=PUPPET"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsTrue();
	}

	[Test]
	public async ValueTask AudibleWithForwardListIsAHearer()
	{
		var thing = await Thing("Loud");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {thing}=AUDIBLE"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&FORWARDLIST {thing}=#1"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsTrue();
	}

	[Test]
	public async ValueTask ForwardListWithoutAudibleIsNotAHearer()
	{
		var thing = await Thing("Quiet");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&FORWARDLIST {thing}=#1"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsFalse();
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/DidItServiceTests/*" > /tmp/t1.log 2>&1; grep -E "error|failed|Failed" /tmp/t1.log | head
```

Expected: compile error, `IPermissionService` has no `IsHearer`.

- [ ] **Step 3: Declare it on the interface**

In `IPermissionService.cs`, after the `CanInteract` overloads:

```csharp
	/// <summary>
	/// PennMUSH <c>Hearer</c> (<c>src/game.c:1564</c>): a connected player, a <c>PUPPET</c>, an
	/// <c>AUDIBLE</c> object carrying a <c>FORWARDLIST</c>, or anything with a <c>LISTEN</c>.
	/// A move by a non-hearer fires action attributes only, never messages.
	/// </summary>
	ValueTask<bool> IsHearer(AnySharpObject obj);
```

- [ ] **Step 4: Implement it**

In `PermissionService.cs`:

```csharp
	public async ValueTask<bool> IsHearer(AnySharpObject obj)
	{
		if (await obj.HasFlag("PUPPET"))
		{
			return true;
		}

		if (obj.IsPlayer && connectionService.Get(obj.Object().DBRef).Any())
		{
			return true;
		}

		var listen = await attributeService.Value.GetAttributeAsync(
			obj, obj, "LISTEN", IAttributeService.AttributeMode.Read, parent: false);

		if (listen.IsAttribute)
		{
			return true;
		}

		if (!await obj.HasFlag("AUDIBLE"))
		{
			return false;
		}

		var forwardList = await attributeService.Value.GetAttributeAsync(
			obj, obj, "FORWARDLIST", IAttributeService.AttributeMode.Read, parent: false);

		return forwardList.IsAttribute;
	}
```

`PermissionService` already holds `IConnectionService` and a `Lazy<IAttributeService>`; if either is
absent from its constructor, add it — `IAttributeService` **must** be `Lazy<T>` to avoid the
attribute/permission cycle described in `engine-data-trunk.md` §5.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/DidItServiceTests/*" > /tmp/t1.log 2>&1; grep -E "Passed|Failed" /tmp/t1.log | tail -3
```

Expected: 5 passed.

- [ ] **Step 6: Format and commit**

```bash
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Library SharpMUSH.Tests
git commit -m "Add IsHearer, PennMUSH's Hearer predicate"
```

---

### Task 2: `InteractType` on room broadcasts

**Files:**
- Modify: `SharpMUSH.Library/Services/Interfaces/ICommunicationService.cs:54-60`
- Modify: `SharpMUSH.Library/Services/CommunicationService.cs:60-95`

**Interfaces:**
- Produces: `SendToRoomAsync(..., IPermissionService.InteractType interact = IPermissionService.InteractType.Hear)`

PennMUSH's `notify_except2` takes interaction flags; the movement triads use `NA_INTER_HEAR`,
`NA_INTER_PRESENCE` and `NA_INTER_SEE` at different points (`src/move.c:105-146`). Every current
caller hard-codes hearing, so the parameter is defaulted and no existing call site changes.

- [ ] **Step 1: Write the failing test**

Append to `SharpMUSH.Tests/Services/DidItServiceTests.cs`:

```csharp
	[Test]
	public async ValueTask SendToRoomHonoursTheRequestedInteractType()
	{
		var communication = WebAppFactoryArg.Services.GetRequiredService<ICommunicationService>();
		var method = typeof(ICommunicationService).GetMethod(nameof(ICommunicationService.SendToRoomAsync))!;
		var parameterNames = method.GetParameters().Select(p => p.Name).ToArray();

		await Assert.That(communication).IsNotNull();
		await Assert.That(parameterNames).Contains("interact");
	}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/DidItServiceTests/SendToRoomHonoursTheRequestedInteractType" > /tmp/t2.log 2>&1; grep -E "Failed|error" /tmp/t2.log | head
```

Expected: FAIL — no `interact` parameter.

- [ ] **Step 3: Add the parameter to the interface**

In `ICommunicationService.cs`, replace the `SendToRoomAsync` declaration:

```csharp
	/// <param name="interact">
	/// Which interaction gate each candidate recipient must pass. PennMUSH's <c>notify_except2</c>
	/// takes this per message: movement leave/enter messages are <c>NA_INTER_PRESENCE</c>, the
	/// OX-prefixed and zone messages are <c>NA_INTER_SEE</c>, and speech is <c>NA_INTER_HEAR</c>.
	/// </param>
	ValueTask SendToRoomAsync(
		AnySharpObject executor,
		AnySharpContainer room,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		AnySharpObject? sender = null,
		IEnumerable<AnySharpObject>? excludeObjects = null,
		IPermissionService.InteractType interact = IPermissionService.InteractType.Hear);
```

- [ ] **Step 4: Thread it through the implementation**

In `CommunicationService.cs`, add the parameter to the signature and replace the hard-coded gate:

```csharp
				return await permissionService.CanInteract(executor, objWithRoom, interact);
```

- [ ] **Step 5: Run it to verify it passes, and confirm nothing else broke**

```bash
dotnet build SharpMUSH.sln > /tmp/t2b.log 2>&1; grep -E "error|Build succeeded" /tmp/t2b.log | head
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/DidItServiceTests/*" > /tmp/t2.log 2>&1; grep -E "Passed|Failed" /tmp/t2.log | tail -3
```

- [ ] **Step 6: Format and commit**

```bash
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Library SharpMUSH.Tests
git commit -m "Let room broadcasts choose their interaction gate"
```

---

### Task 3: The `did_it` primitive

**Files:**
- Create: `SharpMUSH.Library/Services/Interfaces/IDidItService.cs`
- Create: `SharpMUSH.Library/Services/DidItService.cs`
- Modify: `SharpMUSH.Server/Startup.cs`
- Test: `SharpMUSH.Tests/Services/DidItServiceTests.cs`

**Interfaces:**
- Consumes: `IPermissionService.IsHearer` (Task 1), `SendToRoomAsync(..., interact:)` (Task 2)
- Produces:
  - `record DidItRequest(AnySharpObject Player, AnySharpObject Thing, string? What = null, MString? Def = null, string? OWhat = null, string? ODef = null, string? AWhat = null, AnySharpContainer? Loc = null, DBRef? Env0 = null, DBRef? Env1 = null, IPermissionService.InteractType Interact = IPermissionService.InteractType.Hear)`
  - `ValueTask<bool> IDidItService.DidIt(IMUSHCodeParser parser, DidItRequest request)`

Port of `real_did_it` (`src/predicat.c:216`). Returns whether any attribute was actually used, which
`fail_lock` propagates.

- [ ] **Step 1: Write the failing tests**

Append to `SharpMUSH.Tests/Services/DidItServiceTests.cs`:

```csharp
	private IDidItService DidItService => WebAppFactoryArg.Services.GetRequiredService<IDidItService>();

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	[Test]
	public async ValueTask WhatGoesToThePlayerAndIsEvaluated()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItWhat");
		var thing = await Thing("WhatHolder");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&GREET {thing}=Hello [name(%#)]."));

		var messages = await MessagesWhile(actor.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(actor.DbRef),
				Thing: await Node(thing),
				What: "GREET")));

		await Assert.That(messages.Any(m => m.Contains("Hello") && m.Contains(actor.Name))).IsTrue();
	}

	[Test]
	public async ValueTask DefIsUsedWhenTheAttributeIsAbsent()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItDef");
		var thing = await Thing("NoGreet");

		var messages = await MessagesWhile(actor.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(actor.DbRef),
				Thing: await Node(thing),
				What: "GREET",
				Def: MarkupText.Plain("Nothing happens."))));

		await Assert.That(messages.Any(m => m.Contains("Nothing happens."))).IsTrue();
	}

	[Test]
	public async ValueTask ODefIsPrefixedWithTheActorsName()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItActor");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItWatcher");
		var roomDbRef = await DigAndGather(actor, watcher);
		var thing = await Thing("ODefHolder");

		var messages = await MessagesWhile(watcher.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(actor.DbRef),
				Thing: await Node(thing),
				OWhat: "OGREET",
				ODef: "waves.",
				Loc: roomDbRef)));

		await Assert.That(messages.Any(m => m == $"{actor.Name} waves.")).IsTrue();
	}

	[Test]
	public async ValueTask OWhatIsEvaluatedOncePerCallNotOncePerListener()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItOnceActor");
		var watcherA = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItOnceA");
		var watcherB = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItOnceB");
		var roomDbRef = await DigAndGather(actor, watcherA, watcherB);
		var thing = await Thing("CounterHolder");

		// The o-message increments a counter on the holder as a side effect. If the primitive
		// evaluated per listener the counter would reach 2, and both watchers would see the
		// message the OTHER listener's evaluation produced.
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&COUNT {thing}=0"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&OTICK {thing}=[set({thing},COUNT:[add(get({thing}/COUNT),1)])]ticks."));

		await DidItService.DidIt(GodParser, new DidItRequest(
			Player: await Node(actor.DbRef),
			Thing: await Node(thing),
			OWhat: "OTICK",
			Loc: roomDbRef));

		var count = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/COUNT)]"));
		await Assert.That(count!.Message!.ToPlainText().Trim()).IsEqualTo("1");
	}

	[Test]
	public async ValueTask ADarkLegalActorProducesNoOMessageButStillActs()
	{
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItDark");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItDarkWatch");
		var roomDbRef = await DigAndGather(wizard, watcher);
		var thing = await Thing("DarkHolder");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {wizard.DbRef}=DARK"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&AMARK {thing}=&MARKED me=yes"));

		var messages = await MessagesWhile(watcher.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(wizard.DbRef),
				Thing: await Node(thing),
				OWhat: "OMARK",
				ODef: "sneaks.",
				AWhat: "AMARK",
				Loc: roomDbRef)));

		await Assert.That(messages.Any(m => m.Contains("sneaks."))).IsFalse();

		await WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>()
			.DrainImmediateQueueForTests();

		var marked = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/MARKED)]"));
		await Assert.That(marked!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}
```

Add the shared helper used above:

```csharp
	/// <summary>Digs a fresh room and teleports every player into it, silently.</summary>
	private async Task<AnySharpContainer> DigAndGather(params TestIsolationHelpers.TestPlayer[] players)
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("DidItRoom");
		var dig = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomRef = dig.Message!.ToPlainText().Trim();

		foreach (var player in players)
		{
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport/silent {player.DbRef}={roomRef}"));
		}

		var parsed = DBRef.TryParse(roomRef, out var dbref)
			? dbref!.Value
			: throw new InvalidOperationException($"@dig did not return a dbref: {roomRef}");

		return (await Mediator.Send(new GetObjectNodeQuery(parsed))).Known.AsContainer;
	}
```

> **Note for the implementer:** `DrainImmediateQueueForTests` and `@teleport/silent` do not exist
> yet — they arrive in Task 5 and Task 14 respectively. Until then this file will not compile, which
> is why Task 3 Step 2 expects a compile failure and why Task 5 re-runs this suite. If you want the
> suite green at the end of Task 3, temporarily use `@teleport/quiet` and drop the drain call,
> then restore both in Task 14 and Task 5. Prefer implementing Tasks 3–5 back to back.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/DidItServiceTests/*" > /tmp/t3.log 2>&1; grep -E "error CS" /tmp/t3.log | head
```

Expected: `IDidItService` not found.

- [ ] **Step 3: Write the interface**

Create `SharpMUSH.Library/Services/Interfaces/IDidItService.cs`:

```csharp
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// One action on an object: a message to the actor, one message to everyone else present, and a
/// queued action attribute. PennMUSH <c>real_did_it</c> (<c>src/predicat.c:216</c>).
/// </summary>
/// <param name="Player">The enactor. <c>%#</c> in every evaluation, and the recipient of <paramref name="What"/>.</param>
/// <param name="Thing">Holds the attributes and is the executor that evaluates them.</param>
/// <param name="What">Attribute whose evaluated value is shown to <paramref name="Player"/>.</param>
/// <param name="Def">Shown to <paramref name="Player"/> when <paramref name="What"/> is unset.</param>
/// <param name="OWhat">Attribute evaluated once and shown to everyone else in <paramref name="Loc"/>.</param>
/// <param name="ODef">Fallback for <paramref name="OWhat"/>, rendered as "&lt;Name&gt; &lt;ODef&gt;".</param>
/// <param name="AWhat">Action attribute queued on <paramref name="Thing"/> with <paramref name="Player"/> as enactor.</param>
/// <param name="Loc">Audience for <paramref name="OWhat"/>. Defaults to the player's location.</param>
/// <param name="Env0"><c>%0</c> for every evaluation and for the queued action.</param>
/// <param name="Env1"><c>%1</c> for every evaluation and for the queued action.</param>
/// <param name="Interact">Interaction gate applied to the o-message audience.</param>
public record DidItRequest(
	AnySharpObject Player,
	AnySharpObject Thing,
	string? What = null,
	MString? Def = null,
	string? OWhat = null,
	string? ODef = null,
	string? AWhat = null,
	AnySharpContainer? Loc = null,
	DBRef? Env0 = null,
	DBRef? Env1 = null,
	IPermissionService.InteractType Interact = IPermissionService.InteractType.Hear);

public interface IDidItService
{
	/// <summary>Runs one triad. Returns true when any attribute was present and used.</summary>
	ValueTask<bool> DidIt(IMUSHCodeParser parser, DidItRequest request);

	/// <summary>
	/// Runs the failure triad for a lock that was just failed. PennMUSH <c>fail_lock</c>
	/// (<c>src/lock.c:832</c>).
	/// </summary>
	ValueTask<bool> FailLock(
		IMUSHCodeParser parser,
		AnySharpObject player,
		AnySharpObject thing,
		LockType lockType,
		MString? def = null,
		AnySharpContainer? loc = null);
}
```

- [ ] **Step 4: Write the implementation**

Create `SharpMUSH.Library/Services/DidItService.cs`:

```csharp
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class DidItService(
	IMediator mediator,
	IAttributeService attributeService,
	INotifyService notifyService,
	ICommunicationService communicationService,
	IPermissionService permissionService) : IDidItService
{
	public async ValueTask<bool> DidIt(IMUSHCodeParser parser, DidItRequest request)
	{
		var used = false;
		var loc = request.Loc ?? (request.Player.IsContent ? await request.Player.AsContent.Location() : null);

		// PennMUSH guards only the messages on a good location; the action attribute runs regardless.
		if (loc is not null)
		{
			var args = BuildArgs(request);

			if (!string.IsNullOrEmpty(request.What))
			{
				var attr = await attributeService.GetAttributeAsync(
					request.Thing, request.Thing, request.What!,
					IAttributeService.AttributeMode.Execute, parent: true);

				if (attr.IsAttribute)
				{
					used = true;
					var message = await attributeService.EvaluateAttributeFunctionAsync(
						parser, request.Thing, request.Thing, request.What!, args,
						evalParent: true, ignorePermissions: true);

					if (!string.IsNullOrEmpty(message.ToPlainText()))
					{
						await notifyService.Notify(request.Player, message, request.Thing);
					}
				}
				else if (request.Def is not null && request.Def.Length > 0)
				{
					await notifyService.Notify(request.Player, request.Def, request.Thing);
				}
			}

			// A Dark object that is legally dark produces no o-messages at all.
			if (!await request.Player.IsDarkLegal())
			{
				MString? broadcast = null;

				if (!string.IsNullOrEmpty(request.OWhat))
				{
					var oattr = await attributeService.GetAttributeAsync(
						request.Thing, request.Thing, request.OWhat!,
						IAttributeService.AttributeMode.Execute, parent: true);

					if (oattr.IsAttribute)
					{
						used = true;
						var evaluated = await attributeService.EvaluateAttributeFunctionAsync(
							parser, request.Thing, request.Thing, request.OWhat!, args,
							evalParent: true, ignorePermissions: true);

						// UFUN_NAME: the actor's name prefixes the evaluated text.
						if (!string.IsNullOrEmpty(evaluated.ToPlainText()))
						{
							broadcast = MarkupText.Concat(
								MarkupText.Plain($"{request.Player.Object().Name} "), evaluated);
						}
					}
				}

				if (broadcast is null && !string.IsNullOrEmpty(request.ODef))
				{
					broadcast = MarkupText.Plain($"{request.Player.Object().Name} {request.ODef}");
				}

				if (broadcast is not null)
				{
					var message = broadcast;
					await communicationService.SendToRoomAsync(
						request.Player,
						loc,
						_ => message,
						INotifyService.NotificationType.Emit,
						sender: request.Thing,
						excludeObjects: [request.Player, request.Thing],
						interact: request.Interact);
				}
			}
		}

		if (!string.IsNullOrEmpty(request.AWhat))
		{
			used = await QueueAction(parser, request) || used;
		}

		return used;
	}

	public async ValueTask<bool> FailLock(
		IMUSHCodeParser parser,
		AnySharpObject player,
		AnySharpObject thing,
		LockType lockType,
		MString? def = null,
		AnySharpContainer? loc = null)
	{
		var (what, owhat, awhat) = LockMessages.FailureAttributes(lockType);

		return await DidIt(parser, new DidItRequest(
			Player: player,
			Thing: thing,
			What: what,
			Def: def,
			OWhat: owhat,
			AWhat: awhat,
			Loc: loc));
	}

	private static Dictionary<string, CallState> BuildArgs(DidItRequest request)
	{
		var args = new Dictionary<string, CallState>();

		if (request.Env0 is not null)
		{
			args["0"] = new CallState(request.Env0.Value.ToString());
		}

		if (request.Env1 is not null)
		{
			args["1"] = new CallState(request.Env1.Value.ToString());
		}

		return args;
	}

	/// <summary>
	/// Queues the action attribute as its own queue entry, with <c>Thing</c> as executor and
	/// <c>Player</c> as enactor. PennMUSH <c>queue_attribute_base</c>.
	/// </summary>
	/// <remarks>
	/// The closure is evaluated when the queue drains, which may be many commands later, so it
	/// captures DBRefs and lets the parser resolve them then. Capturing a loaded object here would
	/// hand the action a snapshot taken before whatever write triggered it.
	/// </remarks>
	private async ValueTask<bool> QueueAction(IMUSHCodeParser parser, DidItRequest request)
	{
		var thing = request.Thing;
		var attr = await attributeService.GetAttributeAsync(
			thing, thing, request.AWhat!, IAttributeService.AttributeMode.Execute, parent: true);

		if (!attr.IsAttribute || attr.AsAttribute.Length == 0
				|| string.IsNullOrEmpty(attr.AsAttribute.Last().Value.ToPlainText()))
		{
			return false;
		}

		var executor = thing.Object().DBRef;
		var enactor = request.Player.Object().DBRef;
		var args = BuildArgs(request);
		var attributePath = attr.AsAttribute.Last().LongName!.Split("`");
		var baseState = parser.CurrentState;

		await mediator.Send(new QueueAttributeRequest(
			() => ValueTask.FromResult(baseState with
			{
				Executor = executor,
				Enactor = enactor,
				Caller = enactor,
				Arguments = args,
				EnvironmentRegisters = args
			}),
			new DbRefAttribute(executor, attributePath)));

		return true;
	}
}
```

- [ ] **Step 5: Write the lock-message table**

Create `SharpMUSH.Library/Definitions/LockMessages.cs`:

```csharp
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Which attributes a failed lock triggers. PennMUSH's <c>lock_msgs</c> table
/// (<c>src/lock.c:102</c>) names four locks explicitly; every other lock type derives its
/// attributes as <c>&lt;LOCK&gt;_LOCK`FAILURE</c> and siblings.
/// </summary>
public static class LockMessages
{
	private static readonly Dictionary<LockType, string> Named = new()
	{
		[LockType.Basic] = "FAILURE",
		[LockType.Enter] = "EFAIL",
		[LockType.Use] = "UFAIL",
		[LockType.Leave] = "LFAIL"
	};

	public static (string What, string OWhat, string AWhat) FailureAttributes(LockType lockType)
		=> Named.TryGetValue(lockType, out var failBase)
			? (failBase, $"O{failBase}", $"A{failBase}")
			: ($"{lockType.ToString().ToUpperInvariant()}_LOCK`FAILURE",
				 $"{lockType.ToString().ToUpperInvariant()}_LOCK`OFAILURE",
				 $"{lockType.ToString().ToUpperInvariant()}_LOCK`AFAILURE");
}
```

- [ ] **Step 6: Register the service**

In `SharpMUSH.Server/Startup.cs`, beside the other `IMoveService`/`IPermissionService`
registrations, add:

```csharp
		services.AddSingleton<IDidItService, DidItService>();
```

- [ ] **Step 7: Run the tests**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/DidItServiceTests/*" > /tmp/t3.log 2>&1; grep -E "Passed|Failed" /tmp/t3.log | tail -3
```

Expected: all pass, except the two depending on Tasks 5 and 14 if you have not done them yet.

- [ ] **Step 8: Format and commit**

```bash
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Library SharpMUSH.Server SharpMUSH.Tests
git commit -m "Add the did_it primitive and the lock failure table"
```

---

## Phase B — Queue safety

### Task 4: Move-depth counter on parser state

**Files:**
- Modify: `SharpMUSH.Library/ParserInterfaces/ParserState.cs:238-285`

**Interfaces:**
- Produces: `ParserState.MoveDepth` of type `InvocationCounter?`

PennMUSH bounds `enter_room` recursion with `static int deep` capped at 15 (`src/move.c:232`). That
counter is process-global and correct only because Penn's queue is single-threaded; SharpMUSH runs
moves concurrently, so the depth belongs to the evaluation, beside `CallDepth`.

- [ ] **Step 1: Add the parameter**

In the `ParserState` record, after `LimitExceeded`:

```csharp
	InvocationCounter? MoveDepth = null,
```

and its doc comment beside the others:

```csharp
/// <param name="MoveDepth">
/// Shared counter bounding recursive movement — <c>enter_room</c> reached through
/// <c>safe_tel</c>'s HOME case or through a container's drop-to. PennMUSH caps the equivalent at 15
/// (<c>src/move.c:232</c>) with a process-global counter, which is only safe under its
/// single-threaded queue; here it is per evaluation, like <see cref="CallDepth"/>.
/// </param>
```

- [ ] **Step 2: Initialise it everywhere the other counters are initialised**

Add `MoveDepth: new InvocationCounter(),` at each of these sites, matching the surrounding style:

- `SharpMUSH.Library/ParserInterfaces/ParserState.cs:375`
- `SharpMUSH.Implementation/MUSHCodeParser.cs:438`
- `SharpMUSH.Implementation/MUSHCodeParser.cs:625`
- `SharpMUSH.Implementation/Services/LockEvaluationServices.cs:62`

and, following the propagate-or-create pattern already used for `CallDepth` at those two sites:

- `SharpMUSH.Library/Services/EventService.cs:139`
- `SharpMUSH.Library/Services/ConnectionAnnounceService.cs:354`

```csharp
				MoveDepth: isEmpty ? new InvocationCounter() : parser.CurrentState.MoveDepth ?? new InvocationCounter(),
```

- [ ] **Step 3: Build**

```bash
dotnet build SharpMUSH.sln > /tmp/t4.log 2>&1; grep -E "error|Build succeeded" /tmp/t4.log | head
```

Expected: build succeeded.

- [ ] **Step 4: Format and commit**

```bash
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Implementation --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Implementation --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Library SharpMUSH.Implementation
git commit -m "Track movement recursion depth per evaluation"
```

---

### Task 5: Queue quota and the runaway halt

**Files:**
- Modify: `SharpMUSH.Library/Services/TaskScheduler.cs`
- Modify: `SharpMUSH.Library/Services/Interfaces/ITaskScheduler.cs`
- Create: `SharpMUSH.Tests/Services/QueueQuotaTests.cs`

**Interfaces:**
- Produces: `ValueTask ITaskScheduler.DrainImmediateQueueForTests()`

`LimitOptions.PlayerQueueLimit` exists, defaults to 100, is documented in `sharptop.md:1313`, and is
read by nothing. `TaskScheduler` has a single process-wide 10,000-entry channel written with
`TryWrite`, so a runaway loop fills it and every subsequent enqueue — including other players'
commands — is dropped behind a `LogWarning` that blames the queue being completed. PennMUSH bounds
this per owner with `queue_limit` and halts the offender (`src/cque.c:303`). Queueing action
attributes (Task 3) makes it reachable from far more places, so it is fixed here.

Per `sharptop.md:1313`, wizards and holders of the `Queue` power get `player_queue_limit` plus the
current database size.

- [ ] **Step 1: Write the failing tests**

Create `SharpMUSH.Tests/Services/QueueQuotaTests.cs`:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A runaway object is halted and told about it, and it does not consume the queue that every
/// other player shares. PennMUSH <c>queue_limit</c> / <c>src/cque.c:303</c>.
/// </summary>
public class QueueQuotaTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask MutualLookActionsHaltTheOffenderRatherThanRunningForever()
	{
		var a = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("RunawayA"));
		var b = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("RunawayB"));

		// Each object's @adescribe looks at the other. Penn bounds this with the queue quota.
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ADESCRIBE {a}=look {b}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ADESCRIBE {b}=look {a}"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"look {a}"));
		await Scheduler.DrainImmediateQueueForTests();

		var aHalted = await GodParser.FunctionParse(MarkupText.Plain($"[hasflag({a},HALT)]"));
		var bHalted = await GodParser.FunctionParse(MarkupText.Plain($"[hasflag({b},HALT)]"));

		await Assert.That(
			aHalted!.Message!.ToPlainText().Trim() == "1" || bHalted!.Message!.ToPlainText().Trim() == "1")
			.IsTrue();
	}

	[Test]
	public async ValueTask AnUnrelatedCommandStillRunsAfterARunaway()
	{
		var runaway = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("Hog"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ADESCRIBE {runaway}=look {runaway}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"look {runaway}"));
		await Scheduler.DrainImmediateQueueForTests();

		var witness = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("Witness"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&MARK {witness}=set"));
		await Scheduler.DrainImmediateQueueForTests();

		var mark = await GodParser.FunctionParse(MarkupText.Plain($"[get({witness}/MARK)]"));
		await Assert.That(mark!.Message!.ToPlainText().Trim()).IsEqualTo("set");
	}
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/QueueQuotaTests/*" > /tmp/t5.log 2>&1; grep -E "error CS|Failed" /tmp/t5.log | head
```

Expected: `DrainImmediateQueueForTests` not found.

- [ ] **Step 3: Add the drain helper to the scheduler interface**

In `ITaskScheduler.cs`:

```csharp
	/// <summary>
	/// Runs the immediate queue to empty. Tests need a deterministic point at which queued action
	/// attributes have run; production never calls this.
	/// </summary>
	ValueTask DrainImmediateQueueForTests();
```

- [ ] **Step 4: Implement the quota, the halt and the drain**

In `TaskScheduler.cs`, add a per-owner pending count and the quota check. Take
`IOptionsMonitor<SharpMUSHOptions>` (named `options` here to match the codebase) and
`IPermissionService` in the constructor if they are not already present.

```csharp
	private readonly ConcurrentDictionary<int, int> _pendingByOwner = new();

	/// <summary>
	/// PennMUSH <c>queue_limit</c> (<c>src/cque.c:226</c>): how many entries this owner may hold.
	/// Wizards and holders of the <c>Queue</c> power get the limit plus the database size, as
	/// <c>help @queue</c> documents.
	/// </summary>
	private async ValueTask<int> QuotaFor(DBRef owner)
	{
		var limit = (int) options.CurrentValue.Limit.PlayerQueueLimit;
		var node = await mediator.Send(new GetObjectNodeQuery(owner));

		if (node.IsNone)
		{
			return limit;
		}

		var obj = node.Known;

		if (await obj.IsWizard() || await obj.HasPower("QUEUE"))
		{
			return limit + await mediator.Send(new GetObjectCountQuery());
		}

		return limit;
	}

	/// <summary>
	/// Refuses one entry, tells the owner, logs it, and halts the offender — PennMUSH's
	/// "Runaway object" path.
	/// </summary>
	private async ValueTask HaltRunaway(DBRef offender, DBRef owner)
	{
		var node = await mediator.Send(new GetObjectNodeQuery(offender));
		var name = node.IsNone ? offender.ToString() : node.Known.Object().Name;

		await notifyService.NotifyLocalized(owner,
			nameof(ErrorMessages.Notifications.RunawayObject), name, offender.ToString());

		logger.LogWarning("Runaway object {Name} ({DbRef}) exceeded its queue quota; commands halted",
			name, offender);

		// Same shape as @halt's flag path (GeneralCommands.cs:2055): the flag is looked up by name,
		// and a database missing it is a seeding problem, not something to invent a flag for.
		var haltFlag = await mediator.Send(new GetObjectFlagQuery("HALT"));

		if (haltFlag is not null && !node.IsNone)
		{
			await mediator.Send(new SetObjectFlagCommand(node.Known, haltFlag));
		}
	}
```

Gate every enqueue path (`EnqueueWork`, the user-command path, the command-list path and
`WriteAsyncAttribute`) on the quota. Each currently ends in `TryWrite`; replace that shape with:

```csharp
		var owner = await OwnerOf(state.Executor!.Value);

		if (_pendingByOwner.GetValueOrDefault(owner.Number) >= await QuotaFor(owner))
		{
			await HaltRunaway(state.Executor.Value, owner);
			return;
		}

		_pendingByOwner.AddOrUpdate(owner.Number, 1, (_, current) => current + 1);
		_pendingEntries[pid] = entry;

		if (!_immediateQueue.Writer.TryWrite(entry))
		{
			_pendingEntries.TryRemove(pid, out _);
			_pendingByOwner.AddOrUpdate(owner.Number, 0, (_, current) => Math.Max(0, current - 1));
			entry.Cts.Dispose();
			logger.LogWarning(
				"Immediate queue is full at {Capacity} entries; dropped PID {Pid} in group {Group}",
				ImmediateQueueCapacity, pid, group);
		}
```

and decrement `_pendingByOwner` in the `finally` of `ProcessQueueAsync`'s per-entry block, beside
the existing `_pendingEntries.TryRemove`.

The drain:

```csharp
	public async ValueTask DrainImmediateQueueForTests()
	{
		EnsureConsumerStarted();

		// The consumer enqueues while it drains, so poll until both the channel and the pending
		// set are empty rather than reading the count once.
		for (var attempt = 0; attempt < 200; attempt++)
		{
			if (_immediateQueue.Reader.Count == 0 && _pendingEntries.IsEmpty)
			{
				return;
			}

			await Task.Delay(25);
		}
	}
```

- [ ] **Step 5: Add the notification string**

In `SharpMUSH.Library/Definitions/ErrorMessages.cs`, inside `Notifications`:

```csharp
		public const string RunawayObject = "Runaway object: {0}({1}). Commands halted.";
```

and the matching entry in `SharpMUSH.Library/Resources/Notifications.resx` with the same name and
value. Leave `Notifications.fr.resx` alone; untranslated keys fall back to the invariant resource.

- [ ] **Step 6: Run the tests**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/QueueQuotaTests/*" > /tmp/t5.log 2>&1; grep -E "Passed|Failed" /tmp/t5.log | tail -3
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/DidItServiceTests/*" > /tmp/t5b.log 2>&1; grep -E "Passed|Failed" /tmp/t5b.log | tail -3
```

Expected: both suites pass.

- [ ] **Step 7: Format and commit**

```bash
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Library SharpMUSH.Tests
git commit -m "Enforce the per-owner queue quota and halt runaway objects"
```


---

## Phase C — The automatic look

### Task 6: Move the shared look helpers into `SharpMUSH.Library`

**Files:**
- Move: `SharpMUSH.Implementation/Common/AttributeHelpers.cs` → `SharpMUSH.Library/Common/AttributeHelpers.cs`
- Move: `SharpMUSH.Implementation/Common/MessageHelpers.cs` → `SharpMUSH.Library/Common/MessageHelpers.cs`
- Modify: `SharpMUSH.Implementation/Commands/MoreCommands.cs`, `Commands/GeneralCommands.cs`,
  `Functions/AttributeFunctions.cs`, `Handlers/ChannelMessageRequestHandler.cs`,
  `Substitutions/Substitutions.cs` — namespace on the `using`

**Interfaces:**
- Produces: `SharpMUSH.Library.Common.AttributeHelpers`, `SharpMUSH.Library.Common.MessageHelpers`

`ILookService` lives in `SharpMUSH.Library` and the look body calls
`AttributeHelpers.EvaluateFormatAttribute` and `MessageHelpers.FormatMStringsWithOxfordComma`.
Both classes already import nothing above `SharpMUSH.Library`, so this is a namespace move.

- [ ] **Step 1: Move both files and change their namespace**

```bash
mkdir -p SharpMUSH.Library/Common
git mv SharpMUSH.Implementation/Common/AttributeHelpers.cs SharpMUSH.Library/Common/AttributeHelpers.cs
git mv SharpMUSH.Implementation/Common/MessageHelpers.cs SharpMUSH.Library/Common/MessageHelpers.cs
sed -i 's/^namespace SharpMUSH\.Implementation\.Common;/namespace SharpMUSH.Library.Common;/' \
  SharpMUSH.Library/Common/AttributeHelpers.cs SharpMUSH.Library/Common/MessageHelpers.cs
```

- [ ] **Step 2: Build to find every broken reference**

```bash
dotnet build SharpMUSH.sln > /tmp/t6.log 2>&1; grep -E "error CS0246|error CS0103" /tmp/t6.log | sort -u | head -20
```

- [ ] **Step 3: Fix the usings**

In each of the five consumer files, replace `using SharpMUSH.Implementation.Common;` with
`using SharpMUSH.Library.Common;`. Where a file needs both (it still uses another type from
`SharpMUSH.Implementation.Common`), keep both usings.

- [ ] **Step 4: Build clean**

```bash
dotnet build SharpMUSH.sln > /tmp/t6.log 2>&1; grep -E "error|Build succeeded" /tmp/t6.log | head
```

Expected: build succeeded.

- [ ] **Step 5: Run the full suite to prove the move changed no behavior**

```bash
dotnet run --project SharpMUSH.Tests > /tmp/t6full.log 2>&1; grep -E "^\s*(Passed|Failed|Skipped)" /tmp/t6full.log | tail -5
```

Expected: same counts as before the move; no new failures.

- [ ] **Step 6: Format and commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Implementation; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Move the attribute and message helpers into SharpMUSH.Library"
```

---

### Task 7: Extract `ILookService`

**Files:**
- Create: `SharpMUSH.Library/Services/Interfaces/ILookService.cs`
- Create: `SharpMUSH.Library/Services/LookService.cs`
- Modify: `SharpMUSH.Implementation/Commands/GeneralCommands.cs:580-961` (the `LOOK` body)
- Modify: `SharpMUSH.Server/Startup.cs`

**Interfaces:**
- Consumes: `IDidItService` (Task 3), `SharpMUSH.Library.Common.*` (Task 6)
- Produces:
  - `enum LookKey { Normal = 0, Auto = 1, Trans = 2, CloudyTrans = 4, NoContents = 8 }`
  - `ValueTask<CallState> ILookService.LookRoom(IMUSHCodeParser parser, AnySharpObject looker, AnyOptionalSharpObject viewing, LookKey key, bool lookOutside = false, bool forceOpaque = false)`

PennMUSH `look_room` (`src/look.c:452`). Two rules the extraction must honour:

- **It takes the caller's parser and threads its state.** `ParserState.FunctionRecursionDepths` is
  what stops `@describe` of `u(%#/describe)` from recursing forever, and it is keyed by attribute
  name, so it also catches two objects whose descriptions evaluate each other. Building a fresh
  `ParserState` here would reset that counter on every hop and turn a bounded recursion into stack
  exhaustion.
- **`LookKey.Auto` obeys `TERSE`**, per `look.c:492-517`: a terse looker gets no description, and
  gets only `@osuccess`/`@asuccess` (or `@ofailure`/`@afailure`) rather than the full triad.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Services/LookServiceTests.cs`:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class LookServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ILookService LookService => WebAppFactoryArg.Services.GetRequiredService<ILookService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	[Test]
	public async ValueTask ATerseLookerSkipsTheDescriptionOnAnAutomaticLook()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TerseLooker");
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("TerseRoom")}"));
		var roomRef = dig.Message!.ToPlainText().Trim();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {player.DbRef}={roomRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@describe {roomRef}=A distinctive description."));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=TERSE"));

		DBRef.TryParse(roomRef, out var roomDbRef);
		var room = (await Mediator.Send(new GetObjectNodeQuery(roomDbRef!.Value))).Known;
		var looker = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;

		var terse = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(GodParser, looker, room.WithNoneOption(), LookKey.Auto));

		await Assert.That(terse.Any(m => m.Contains("A distinctive description."))).IsFalse();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=!TERSE"));

		var full = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(GodParser, looker, room.WithNoneOption(), LookKey.Auto));

		await Assert.That(full.Any(m => m.Contains("A distinctive description."))).IsTrue();
	}

	[Test]
	public async ValueTask ASelfReferentialDescriptionTerminatesWithARecursionError()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RecursiveDesc");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {player.DbRef}=[u(%#/describe)]"));

		var messages = await MessagesWhile(player.DbRef, async () =>
			await GodParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look me")));

		await Assert.That(messages.Any(m => m.Contains("recursion", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}

	[Test]
	public async ValueTask TwoDescriptionsThatEvaluateEachOtherTerminate()
	{
		var a = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MutualDescA");
		var b = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MutualDescB");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {a.DbRef}=[u({b.DbRef}/describe)]"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {b.DbRef}=[u({a.DbRef}/describe)]"));

		var messages = await MessagesWhile(a.DbRef, async () =>
			await GodParser.CommandParse(a.Handle, ConnectionService, MarkupText.Plain($"look {b.DbRef}")));

		await Assert.That(messages.Any(m => m.Contains("recursion", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LookServiceTests/*" > /tmp/t7.log 2>&1; grep -E "error CS" /tmp/t7.log | head
```

Expected: `ILookService` not found.

- [ ] **Step 3: Write the interface**

Create `SharpMUSH.Library/Services/Interfaces/ILookService.cs`:

```csharp
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>How a room is being looked at. PennMUSH's <c>LOOK_*</c> keys, <c>src/look.c</c>.</summary>
[Flags]
public enum LookKey
{
	Normal = 0,

	/// <summary>The look a mover gets on arrival. Obeys <c>TERSE</c>.</summary>
	Auto = 1,

	/// <summary>Looking through a transparent exit.</summary>
	Trans = 2,

	/// <summary>Looking through a cloudy transparent exit — description only, no name line.</summary>
	CloudyTrans = 4,

	/// <summary>Skip the contents listing.</summary>
	NoContents = 8
}

public interface ILookService
{
	/// <summary>
	/// PennMUSH <c>look_room</c> (<c>src/look.c:452</c>).
	/// </summary>
	/// <remarks>
	/// Takes the caller's parser and threads its state. <see cref="ParserState.FunctionRecursionDepths"/>
	/// is what bounds a <c>@describe</c> that evaluates itself, or two that evaluate each other;
	/// building a fresh state here would reset that counter on every hop.
	/// </remarks>
	ValueTask<CallState> LookRoom(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnyOptionalSharpObject viewing,
		LookKey key,
		bool lookOutside = false,
		bool forceOpaque = false);
}
```

- [ ] **Step 4: Move the body**

Create `SharpMUSH.Library/Services/LookService.cs` with a class

```csharp
public class LookService(
	IMediator mediator,
	IAttributeService attributeService,
	ILocateService locateService,
	INotifyService notifyService,
	IPermissionService permissionService,
	IDidItService didItService,
	IOptionsMonitor<SharpMUSHOptions> configuration,
	Lazy<IMoveService> moveService) : ILookService
```

and move the body of `Commands.Look` (`GeneralCommands.cs:580-961`) into `LookRoom` verbatim, with
these mechanical substitutions:

- `executor` → the `looker` parameter; drop the `KnownExecutorObject` lookup and the switch/argument
  parsing, which stay in the command.
- `MoveService.RescueFromVoidAsync` → `moveService.Value.RescueFromVoidAsync`. The `Lazy<T>` is
  required: `LookService` needs `IMoveService` and `MoveService` needs `ILookService`, and
  `engine-data-trunk.md` §5 resolves that with the open generic `LazyService<T>`, not through the
  Mediator.
- The `viewing` resolution block moves to the command; `LookRoom` receives the resolved target.

Then add the two `LookKey` behaviors that do not exist today, at the point where the description is
emitted (`GeneralCommands.cs:750-756` in the original):

```csharp
		var terse = key.HasFlag(LookKey.Auto) && await looker.HasFlag("TERSE");

		if (!terse)
		{
			await notifyService.Notify(looker, formattedName, looker);

			if (formattedDesc.Length > 0)
			{
				await notifyService.Notify(looker, formattedDesc, looker);
			}
		}
		else
		{
			await notifyService.Notify(looker, formattedName, looker);
		}
```

and, for a room, the success/failure triad Penn runs at `look.c:511-525`:

```csharp
		if (realViewing.IsRoom && !key.HasFlag(LookKey.CloudyTrans))
		{
			var passes = await permissionService.PassesLock(looker, realViewing, LockType.Basic);

			if (terse)
			{
				await didItService.DidIt(parser, new DidItRequest(
					Player: looker,
					Thing: realViewing,
					OWhat: passes ? "OSUCCESS" : "OFAILURE",
					AWhat: passes ? "ASUCCESS" : "AFAILURE"));
			}
			else if (passes)
			{
				await didItService.DidIt(parser, new DidItRequest(
					Player: looker,
					Thing: realViewing,
					What: "SUCCESS",
					OWhat: "OSUCCESS",
					AWhat: "ASUCCESS"));
			}
			else
			{
				await didItService.FailLock(parser, looker, realViewing, LockType.Basic);
			}
		}
```

Replace the existing hand-rolled `ODESCRIBE`/`ADESCRIBE` and `OIDESCRIBE`/`AIDESCRIBE` blocks with
`DidIt` calls carrying only `OWhat` and `AWhat`, matching `look.c:496` and `look.c:507`.

- [ ] **Step 5: Reduce the `LOOK` command to a caller**

In `GeneralCommands.cs`, `Look` keeps the void rescue, the switch handling and the `viewing`
resolution, then:

```csharp
		return await LookService.LookRoom(parser, executor, viewing, LookKey.Normal, lookOutside, forceOpaque);
```

- [ ] **Step 6: Register the service**

In `Startup.cs`:

```csharp
		services.AddSingleton<ILookService, LookService>();
```

Confirm `Lazy<>` is already registered as the open generic `LazyService<>`; if not, add
`services.AddTransient(typeof(Lazy<>), typeof(LazyService<>));`.

- [ ] **Step 7: Run the look tests and the existing look suite**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LookServiceTests/*" > /tmp/t7.log 2>&1; grep -E "Passed|Failed" /tmp/t7.log | tail -3
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Look*/*" > /tmp/t7b.log 2>&1; grep -E "Passed|Failed" /tmp/t7b.log | tail -3
```

- [ ] **Step 8: Format and commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Implementation SharpMUSH.Server SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Extract ILookService so movement can look synchronously"
```

---

## Phase D — The movement pipeline

### Task 8: Bounded absolute-room walk

**Files:**
- Modify: `SharpMUSH.Library/Services/MoveService.cs`
- Modify: `SharpMUSH.Library/Services/Interfaces/IMoveService.cs`
- Test: `SharpMUSH.Tests/Services/MoveServiceTests.cs`

**Interfaces:**
- Produces: `ValueTask<AnySharpContainer?> IMoveService.AbsoluteRoom(AnySharpObject obj)` — the
  outermost room containing `obj`, or null when the chain exceeds `Limit.MaxDepth` or dead-ends.

PennMUSH `absolute_room` (`src/db.c`). Zone messages compare the mover's absolute room before and
after the move, so this is computed exactly twice per move and handed to the triads, never re-walked
per triad. `WouldCreateLoop` currently walks the same chain without a bound and adopts this helper.

- [ ] **Step 1: Replace the stub tests with real ones**

Replace the whole body of `SharpMUSH.Tests/Services/MoveServiceTests.cs` below the field
declarations with:

```csharp
	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;

	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<DBRef> Dig(string prefix)
	{
		var result = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		DBRef.TryParse(result.Message!.ToPlainText().Trim(), out var dbref);
		return dbref!.Value;
	}

	[Test]
	public async ValueTask AbsoluteRoomOfSomethingInARoomIsThatRoom()
	{
		var room = await Dig("AbsRoom");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("AbsThing"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={room}"));

		var absolute = await MoveService.AbsoluteRoom(await Node(thing));

		await Assert.That(absolute).IsNotNull();
		await Assert.That(absolute!.Object().DBRef).IsEqualTo(room);
	}

	[Test]
	public async ValueTask AbsoluteRoomWalksOutThroughContainers()
	{
		var room = await Dig("NestedRoom");
		var box = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("Box"));
		var coin = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("Coin"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {coin}={box}"));

		var absolute = await MoveService.AbsoluteRoom(await Node(coin));

		await Assert.That(absolute!.Object().DBRef).IsEqualTo(room);
	}

	[Test]
	public async ValueTask AbsoluteRoomOfARoomIsItself()
	{
		var room = await Dig("SelfRoom");
		var absolute = await MoveService.AbsoluteRoom(await Node(room));
		await Assert.That(absolute!.Object().DBRef).IsEqualTo(room);
	}
```

Delete the seven `[Skip]`-ed stubs and the `NeedsSetup` category with them.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MoveServiceTests/*" > /tmp/t8.log 2>&1; grep -E "error CS" /tmp/t8.log | head
```

- [ ] **Step 3: Implement it**

In `IMoveService.cs`:

```csharp
	/// <summary>
	/// The outermost room containing <paramref name="obj"/>, walking out through containers.
	/// PennMUSH <c>absolute_room</c>. Null when the chain exceeds <c>Limit.MaxDepth</c> or ends
	/// somewhere that is not a room — Penn's "You're in too many containers" and void cases.
	/// </summary>
	ValueTask<AnySharpContainer?> AbsoluteRoom(AnySharpObject obj);
```

In `MoveService.cs`:

```csharp
	public async ValueTask<AnySharpContainer?> AbsoluteRoom(AnySharpObject obj)
	{
		if (obj.IsRoom)
		{
			return obj.AsRoom;
		}

		if (!obj.IsContent)
		{
			return null;
		}

		var current = await obj.AsContent.Location();
		var maxDepth = configuration.CurrentValue.Limit.MaxDepth;

		for (var depth = 0; depth < maxDepth; depth++)
		{
			if (current.IsRoom)
			{
				return current;
			}

			var next = await current.WithExitOption().AsContent.Location();

			if (next.Object().DBRef.Equals(current.Object().DBRef))
			{
				return null;
			}

			current = next;
		}

		return null;
	}
```

Add `IOptionsMonitor<SharpMUSHOptions> configuration` to the `MoveService` constructor.

- [ ] **Step 4: Point `WouldCreateLoop` at the same bound**

Replace `WouldCreateLoop`'s unbounded `while (true)` with a `for` capped at
`configuration.CurrentValue.Limit.MaxDepth`, returning `false` when the cap is reached — an object
chain deeper than the configured maximum is already broken, and a loop check that never terminates
is worse than one that gives up.

- [ ] **Step 5: Run the tests**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MoveServiceTests/*" > /tmp/t8.log 2>&1; grep -E "Passed|Failed" /tmp/t8.log | tail -3
```

- [ ] **Step 6: Format and commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Add a bounded absolute-room walk and bound the loop check with it"
```

---

### Task 9: `MoveIt` — the full triad sequence

**Files:**
- Modify: `SharpMUSH.Library/Services/MoveService.cs`
- Modify: `SharpMUSH.Library/Services/Interfaces/IMoveService.cs`
- Create: `SharpMUSH.Tests/Commands/MovementParityTests.cs`

**Interfaces:**
- Consumes: `IDidItService.DidIt` (Task 3), `IPermissionService.IsHearer` (Task 1),
  `IMoveService.AbsoluteRoom` (Task 8)
- Produces: `ValueTask MoveIt(IMUSHCodeParser parser, AnySharpContent what, AnySharpContainer where, bool noMoveMsgs, DBRef enactor, string cause)`

PennMUSH `moveit` (`src/move.c:66`). The order below is exact and observable; do not reorder.

- [ ] **Step 1: Write the failing tests**

Create `SharpMUSH.Tests/Commands/MovementParityTests.cs`:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Movement produces the messages PennMUSH produces, in PennMUSH's order.
/// Reference: <c>src/move.c</c> <c>moveit</c> / <c>enter_room</c>.
/// </summary>
public class MovementParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private async Task<string> Dig(string prefix)
	{
		var result = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		return result.Message!.ToPlainText().Trim();
	}

	/// <summary>
	/// Two rooms joined by an exit named <c>out</c>, and a mover standing in the first.
	/// <c>@open</c> sources the exit from the executor's own location, so God is moved into the
	/// origin room first — the same shape <c>ObjectDestructionTests.cs:153</c> uses — and its
	/// CallState carries the new exit's dbref.
	/// </summary>
	private async Task<(TestIsolationHelpers.TestPlayer Mover, string From, string To, string Exit)> Corridor(string prefix)
	{
		var from = await Dig($"{prefix}From");
		var to = await Dig($"{prefix}To");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent me={from}"));
		var open = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@open out={to}"));
		var exit = open.Message!.ToPlainText().Trim();

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Mover");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={from}"));

		return (mover, from, to, exit);
	}

	[Test]
	public async ValueTask WalkingThroughAnExitAnnouncesDepartureAndArrival()
	{
		var (mover, from, to, _) = await Corridor("Walk");
		var watcherHere = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkStay");
		var watcherThere = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkGreet");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcherHere.DbRef}={from}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcherThere.DbRef}={to}"));

		var departure = await MessagesWhile(watcherHere.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(departure.Any(m => m == $"{mover.Name} has left.")).IsTrue();
	}

	[Test]
	public async ValueTask WalkingThroughAnExitAnnouncesArrivalInTheDestination()
	{
		var (mover, from, to, _) = await Corridor("Arrive");
		var greeter = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ArriveGreet");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {greeter.DbRef}={to}"));

		var arrival = await MessagesWhile(greeter.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(arrival.Any(m => m == $"{mover.Name} has arrived.")).IsTrue();
	}

	[Test]
	public async ValueTask ADarkWizardArrivesSilently()
	{
		var (mover, from, to, _) = await Corridor("Sneak");
		var greeter = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SneakGreet");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {greeter.DbRef}={to}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {mover.DbRef}=WIZARD"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {mover.DbRef}=DARK"));

		var arrival = await MessagesWhile(greeter.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(arrival.Any(m => m.Contains("has arrived."))).IsFalse();
	}

	[Test]
	public async ValueTask ANonHearingThingFiresItsActionAttributeButSendsNoMessage()
	{
		var room = await Dig("SilentThingRoom");
		var destination = await Dig("SilentThingDest");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentWatch");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={destination}"));

		var thing = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("SilentThing"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AENTER {destination}=&ARRIVED me=yes"));

		var arrival = await MessagesWhile(watcher.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {thing}={destination}")));

		await Assert.That(arrival.Any(m => m.Contains("has arrived."))).IsFalse();

		await Scheduler.DrainImmediateQueueForTests();
		var arrived = await GodParser.FunctionParse(MarkupText.Plain($"[get({destination}/ARRIVED)]"));
		await Assert.That(arrived!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}

	[Test]
	public async ValueTask OxEnterIsShownInTheOriginRoomAndOxLeaveInTheDestination()
	{
		var origin = await Dig("OxOrigin");
		var destinationRoom = await Dig("OxDest");

		// OXENTER/OXLEAVE only fire for non-room containers (move.c:122-127), so the mover moves
		// between two vehicles rather than between two rooms.
		var fromVehicle = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("FromCar"));
		var toVehicle = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("ToCar"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {fromVehicle}={origin}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {toVehicle}={destinationRoom}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&OXLEAVE {fromVehicle}=climbs out of the car."));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OxMover");
		var destinationWatcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OxDestWatch");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={fromVehicle}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {destinationWatcher.DbRef}={toVehicle}"));

		// OXLEAVE is set on the container being LEFT and is shown to the people where the mover ARRIVES.
		var seen = await MessagesWhile(destinationWatcher.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport {mover.DbRef}={toVehicle}")));

		await Assert.That(seen.Any(m => m == $"{mover.Name} climbs out of the car.")).IsTrue();
	}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t9.log 2>&1; grep -E "Failed|error CS" /tmp/t9.log | head
```

- [ ] **Step 3: Write `MoveIt`**

Replace `ExecuteMoveAsync`, `TriggerLeaveHooksAsync`, `TriggerEnterHooksAsync`,
`TriggerTeleportHooksAsync`, `NotifyContentsOfMoveAsync` and the `MoveAttributes` class with:

```csharp
	public async ValueTask MoveIt(
		IMUSHCodeParser parser,
		AnySharpContent what,
		AnySharpContainer where,
		bool noMoveMsgs,
		DBRef enactor,
		string cause)
	{
		var mover = what.WithRoomOption();
		var oldContainer = await what.Location();
		var old = oldContainer.Object().DBRef;
		var destination = where.Object().DBRef;

		// Both absolute rooms are resolved exactly twice per move, before and after, and handed to
		// the zone triads. Walking per triad would multiply the location queries.
		var absOld = await AbsoluteRoom(mover);

		await mediator.Send(new MoveObjectCommand(
			what, where, enactor, noMoveMsgs, cause, OldContainer: old));

		var absNew = await AbsoluteRoom(mover);
		var destinationObject = where.WithExitOption();
		var oldObject = oldContainer.WithExitOption();

		var wizardSuppressed = configuration.CurrentValue.Command.WizNoAEnter
			&& await mover.IsWizard() && await mover.IsDarkLegal();

		if (!wizardSuppressed && !old.Equals(destination))
		{
			await didItService.DidIt(parser, new DidItRequest(
				Player: mover, Thing: mover, OWhat: "OXMOVE",
				Loc: oldContainer, Env0: old, Env1: destination,
				Interact: IPermissionService.InteractType.Hear));

			if (await permissionService.IsHearer(mover))
			{
				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: oldObject,
					What: "LEAVE", OWhat: "OLEAVE", ODef: ErrorMessages.Notifications.DefaultOLeave,
					AWhat: "ALEAVE", Loc: oldContainer, Env0: old, Env1: destination,
					Interact: IPermissionService.InteractType.Presence));

				await ZoneTriad(parser, mover, absOld, absNew, leaving: true, loc: oldContainer);

				if (!oldObject.IsRoom)
				{
					// OXLEAVE lives on the container being left and is shown where the mover arrives.
					await didItService.DidIt(parser, new DidItRequest(
						Player: mover, Thing: oldObject, OWhat: "OXLEAVE",
						Loc: where, Env0: destination,
						Interact: IPermissionService.InteractType.See));
				}

				if (!destinationObject.IsRoom)
				{
					await didItService.DidIt(parser, new DidItRequest(
						Player: mover, Thing: destinationObject, OWhat: "OXENTER",
						Loc: oldContainer, Env0: old,
						Interact: IPermissionService.InteractType.See));
				}

				await ZoneTriad(parser, mover, absOld, absNew, leaving: false, loc: where);

				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: destinationObject,
					What: "ENTER", OWhat: "OENTER", ODef: ErrorMessages.Notifications.DefaultOEnter,
					AWhat: "AENTER", Loc: where, Env0: destination, Env1: old,
					Interact: IPermissionService.InteractType.Presence));
			}
			else
			{
				// A non-hearer triggers the actions and none of the messages.
				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: oldObject, AWhat: "ALEAVE", Loc: oldContainer, Env0: old));
				await ZoneTriad(parser, mover, absOld, absNew, leaving: true, loc: oldContainer, actionsOnly: true);
				await ZoneTriad(parser, mover, absOld, absNew, leaving: false, loc: where, actionsOnly: true);
				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: destinationObject, AWhat: "AENTER", Loc: where, Env0: destination));
			}
		}

		if (!noMoveMsgs)
		{
			await didItService.DidIt(parser, new DidItRequest(
				Player: mover, Thing: mover,
				What: "MOVE", OWhat: "OMOVE", AWhat: "AMOVE",
				Loc: where, Env0: destination, Env1: old,
				Interact: IPermissionService.InteractType.See));
		}
	}

	/// <summary>
	/// The zone triad, fired only when the absolute room's zone actually changed.
	/// PennMUSH <c>move.c:114-133</c>.
	/// </summary>
	private async ValueTask ZoneTriad(
		IMUSHCodeParser parser,
		AnySharpObject mover,
		AnySharpContainer? absOld,
		AnySharpContainer? absNew,
		bool leaving,
		AnySharpContainer loc,
		bool actionsOnly = false)
	{
		var oldZone = absOld is null ? null : await ZoneOf(absOld);
		var newZone = absNew is null ? null : await ZoneOf(absNew);

		if (oldZone?.Object().DBRef.Equals(newZone?.Object().DBRef ?? new DBRef(-1)) == true)
		{
			return;
		}

		var zone = leaving ? oldZone : newZone;

		if (zone is null)
		{
			return;
		}

		await didItService.DidIt(parser, new DidItRequest(
			Player: mover,
			Thing: zone,
			What: actionsOnly ? null : leaving ? "ZLEAVE" : "ZENTER",
			OWhat: actionsOnly ? null : leaving ? "OZLEAVE" : "OZENTER",
			AWhat: leaving ? "AZLEAVE" : "AZENTER",
			Loc: loc,
			Env0: loc.Object().DBRef,
			Interact: IPermissionService.InteractType.See));
	}

	private async ValueTask<AnySharpObject?> ZoneOf(AnySharpContainer container)
	{
		var zone = await container.WithExitOption().Object().Zone.WithCancellation(CancellationToken.None);
		return zone.IsNone ? null : zone.Known;
	}
```

Add `IDidItService didItService` to the constructor.

- [ ] **Step 4: Confirm `WizNoAEnter` is exposed**

`CommandOptions.cs:53` declares `wiz_noaenter`. Check the property name on `CommandOptions` and use
it verbatim; if the option is declared but has no property, add one following the neighbouring
options' shape.

- [ ] **Step 5: Run the tests**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t9.log 2>&1; grep -E "Passed|Failed" /tmp/t9.log | tail -3
```

Tests that need `EnterRoom` (arrival through an exit) still fail here — `GOTO` is rewired in
Task 12. Departure/arrival by `@teleport` should pass.

- [ ] **Step 6: Format and commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Rewrite the move triads as a transcription of moveit"
```

---

### Task 10: `EnterRoom` — depth guard, drop-to and the automatic look

**Files:**
- Modify: `SharpMUSH.Library/Services/MoveService.cs`
- Modify: `SharpMUSH.Library/Services/Interfaces/IMoveService.cs`
- Test: `SharpMUSH.Tests/Commands/MovementParityTests.cs`

**Interfaces:**
- Consumes: `MoveIt` (Task 9), `ILookService.LookRoom` (Task 7), `ParserState.MoveDepth` (Task 4)
- Produces: `ValueTask<OneOf<Success, Error<string>>> EnterRoom(IMUSHCodeParser parser, AnySharpContent what, AnySharpContainer where, bool noMoveMsgs, DBRef enactor, string cause)`

PennMUSH `enter_room` (`src/move.c:227`). The automatic look at the end is **unconditional** — it is
not gated on `noMoveMsgs`; only `TERSE` shortens it.

- [ ] **Step 1: Write the failing test**

Append to `MovementParityTests.cs`:

```csharp
	[Test]
	public async ValueTask ArrivingSomewhereLooksAtIt()
	{
		var (mover, _, to, _) = await Corridor("AutoLook");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {to}=An unmistakable arrival room."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(seen.Any(m => m.Contains("An unmistakable arrival room."))).IsTrue();
	}

	[Test]
	public async ValueTask ASilentTeleportStillLooks()
	{
		var destination = await Dig("SilentLook");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {destination}=Silently arrived."));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentLookMover");

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport/silent {mover.DbRef}={destination}")));

		await Assert.That(seen.Any(m => m.Contains("Silently arrived."))).IsTrue();
	}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/A*Look*" > /tmp/t10.log 2>&1; grep -E "Failed" /tmp/t10.log | head
```

- [ ] **Step 3: Implement `EnterRoom`**

```csharp
	private const int MaxMoveDepth = 15;

	public async ValueTask<OneOf<Success, Error<string>>> EnterRoom(
		IMUSHCodeParser parser,
		AnySharpContent what,
		AnySharpContainer where,
		bool noMoveMsgs,
		DBRef enactor,
		string cause)
	{
		var depth = parser.CurrentState.MoveDepth;

		if (depth is not null && depth.Count >= MaxMoveDepth)
		{
			return new Error<string>(ErrorMessages.Notifications.TooManyContainers);
		}

		depth?.Increment();

		try
		{
			var mover = what.WithRoomOption();

			if (where.IsExit())
			{
				return new Error<string>(ErrorMessages.Notifications.CantGoThatWay);
			}

			if (where.Object().DBRef.Equals(what.Object().DBRef))
			{
				return new Error<string>(ErrorMessages.Notifications.CantGoThatWay);
			}

			if (await WouldCreateLoop(what, where))
			{
				return new Error<string>(ErrorMessages.Notifications.CantGoThatWayContainmentLoop);
			}

			var oldContainer = await what.Location();

			await MoveIt(parser, what, where, noMoveMsgs, enactor, cause);

			// A STICKY room the mover just emptied sends its contents through its drop-to.
			if (!oldContainer.Object().DBRef.Equals(where.Object().DBRef) && oldContainer.IsRoom)
			{
				await MaybeDropTo(parser, oldContainer, enactor);
			}

			// The automatic look. Unconditional in PennMUSH (move.c:279) — nomovemsgs does not
			// reach it, and only TERSE shortens it.
			await lookService.Value.LookRoom(
				parser, mover, where.WithExitOption().WithNoneOption(), LookKey.Auto);

			return new Success();
		}
		finally
		{
			depth?.Decrement();
		}
	}

	/// <summary>PennMUSH <c>maybe_dropto</c> (<c>src/move.c:203</c>).</summary>
	private async ValueTask MaybeDropTo(IMUSHCodeParser parser, AnySharpContainer room, DBRef enactor)
	{
		if (!await room.WithExitOption().HasFlag("STICKY"))
		{
			return;
		}

		var dropTo = await room.WithExitOption().Object().DropTo.WithCancellation(CancellationToken.None);

		if (dropTo.IsNone || dropTo.Known.Object().DBRef.Equals(room.Object().DBRef))
		{
			return;
		}

		// A room still holding anything that can hear and has a connected owner keeps its contents.
		await foreach (var content in room.Content(mediator))
		{
			var candidate = content.WithRoomOption();

			if (await permissionService.IsHearer(candidate) && await OwnerConnected(candidate))
			{
				return;
			}
		}

		await foreach (var content in room.Content(mediator).ToListAsync())
		{
			await EnterRoom(parser, content, dropTo.Known.AsContainer, noMoveMsgs: false, enactor, "dropto");
		}
	}
```

Add `Lazy<ILookService> lookService` to the constructor — the cycle break required by
`engine-data-trunk.md` §5.

Add `TooManyContainers` to `ErrorMessages.Notifications` and `Notifications.resx`:

```csharp
		public const string TooManyContainers = "You're in too many containers.";
```

`OwnerConnected` is a small private helper: resolve `Object().Owner` and ask
`connectionService.Get(ownerDbRef).Any()`.

- [ ] **Step 4: Run the tests**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t10.log 2>&1; grep -E "Passed|Failed" /tmp/t10.log | tail -3
```

- [ ] **Step 5: Format and commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Add enter_room with its depth guard, drop-to and automatic look"
```

---

### Task 11: `SafeTel`

**Files:**
- Modify: `SharpMUSH.Library/Services/MoveService.cs`, `Interfaces/IMoveService.cs`
- Test: `SharpMUSH.Tests/Commands/MovementParityTests.cs`

**Interfaces:**
- Consumes: `EnterRoom` (Task 10)
- Produces: `ValueTask<OneOf<Success, Error<string>>> SafeTel(IMUSHCodeParser parser, AnySharpContent what, AnySharpContainer where, bool noMoveMsgs, DBRef enactor, string cause)`

PennMUSH `safe_tel` (`src/move.c:286`): when the destination's owner differs from the current
location's owner, anything the mover carries that is `STICKY` and not homed to the mover goes home
instead of travelling.

Check what `SharpObject.Home` resolves to before writing the call — `RescueFromVoidAsync` reads it
as something with `.Object().DBRef`, and `EnterRoom` needs an `AnySharpContainer`. If it is an
`AnyOptionalSharpContainer`, unwrap it with `.WithoutNone()` and treat the none case as the item
having no home, which sends it nowhere rather than to dbref -1.

- [ ] **Step 1: Write the failing test**

```csharp
	[Test]
	public async ValueTask TeleportingAcrossOwnersSendsStickyCarriedObjectsHome()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StickyOwner");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StickyMover");

		var home = await Dig("StickyHome");
		var start = await Dig("StickyStart");
		var elsewhere = await Dig("StickyElsewhere");

		var sticky = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("StickyItem"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {sticky}={owner.DbRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {sticky}={home}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sticky}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {start}={owner.DbRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={start}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {sticky}={mover.DbRef}"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {mover.DbRef}={elsewhere}"));

		var where = await GodParser.FunctionParse(MarkupText.Plain($"[loc({sticky})]"));
		await Assert.That(where!.Message!.ToPlainText().Trim()).IsEqualTo(home);
	}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/TeleportingAcrossOwnersSendsStickyCarriedObjectsHome" > /tmp/t11.log 2>&1; grep -E "Failed" /tmp/t11.log | head
```

- [ ] **Step 3: Implement**

```csharp
	public async ValueTask<OneOf<Success, Error<string>>> SafeTel(
		IMUSHCodeParser parser,
		AnySharpContent what,
		AnySharpContainer where,
		bool noMoveMsgs,
		DBRef enactor,
		string cause)
	{
		var mover = what.WithRoomOption();
		var currentLocation = await what.Location();
		var currentOwner = currentLocation.WithExitOption().Object().Owner;
		var destinationOwner = where.WithExitOption().Object().Owner;

		var sameOwner = (await currentOwner.WithCancellation(CancellationToken.None)).Object.DBRef
			.Equals((await destinationOwner.WithCancellation(CancellationToken.None)).Object.DBRef);

		if (sameOwner)
		{
			return await EnterRoom(parser, what, where, noMoveMsgs, enactor, cause);
		}

		var carried = await mover.AsContainer.Content(mediator).ToListAsync();

		foreach (var item in carried)
		{
			var itemObject = item.WithRoomOption();

			if (await permissionService.Controls(mover, itemObject))
			{
				continue;
			}

			if (!await itemObject.HasFlag("STICKY"))
			{
				continue;
			}

			var itemHome = await itemObject.Object().Home.WithCancellation(CancellationToken.None);

			if (itemHome.Object().DBRef.Equals(mover.Object().DBRef))
			{
				continue;
			}

			await EnterRoom(parser, item, itemHome, noMoveMsgs, enactor, cause);
		}

		return await EnterRoom(parser, what, where, noMoveMsgs, enactor, cause);
	}
```

- [ ] **Step 4: Run and commit**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t11.log 2>&1; grep -E "Passed|Failed" /tmp/t11.log | tail -3
for d in SharpMUSH.Library SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Add safe_tel, which strips sticky carried objects across owners"
```


---

### Task 12: A real `CanGoto`, and `GOTO` on the pipeline

**Files:**
- Modify: `SharpMUSH.Library/Services/PermissionService.cs:421-427`
- Modify: `SharpMUSH.Implementation/Commands/GeneralCommands.cs:1579-1645` (`GoTo`), `1395-1437` (`FailToGoThatWay`)
- Test: `SharpMUSH.Tests/Commands/MovementParityTests.cs`

**Interfaces:**
- Consumes: `EnterRoom`, `SafeTel` (Tasks 10–11), `IDidItService.FailLock` (Task 3)
- Produces: `ValueTask<bool> IPermissionService.CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destination)` — actually evaluating the exit's basic lock

`CanGoto` currently returns `true` unconditionally, so exit basic locks are unenforced. `GoTo` ends
at a bare `MoveObjectCommand`, so it fires no triads, no automatic look, and passes no
`OldContainer` — which drops `MoveObjectCommand` onto its global `CacheTags.ObjectContents` fallback
and wipes every container's contents list on every step. PennMUSH `do_move` (`src/move.c:378`).

- [ ] **Step 1: Write the failing tests**

```csharp
	[Test]
	public async ValueTask AnExitWhoseBasicLockFailsRunsTheFailureTriad()
	{
		var (mover, from, to, exit) = await Corridor("Locked");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock {exit}=#0"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&FAILURE {exit}=The door is [switch(1,1,stuck)]."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		// Evaluated, not echoed raw.
		await Assert.That(seen.Any(m => m == "The door is stuck.")).IsTrue();
		var where = await GodParser.FunctionParse(MarkupText.Plain($"[loc({mover.DbRef})]"));
		await Assert.That(where!.Message!.ToPlainText().Trim()).IsEqualTo(from);
	}

	[Test]
	public async ValueTask AFailedLeaveLockRunsTheLeaveFailureTriad()
	{
		var (mover, from, _, _) = await Corridor("LeaveLocked");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock/leave {from}=#0"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&LFAIL {from}=The walls hold you."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(seen.Any(m => m == "The walls hold you.")).IsTrue();
	}

	[Test]
	public async ValueTask AnExitFiresItsSuccessTriadAndItsDropTriadInTheDestination()
	{
		var (mover, _, to, exit) = await Corridor("Triads");
		var greeter = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TriadGreet");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {greeter.DbRef}={to}"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&SUCCESS {exit}=You slip through."));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ODROP {exit}=slips in."));

		var moverSaw = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));
		await Assert.That(moverSaw.Any(m => m == "You slip through.")).IsTrue();

		// @odrop on an exit is shown where the mover arrives (move.c:483, loc = var_dest).
		var greeterSaw = WebAppFactoryArg.Notifications.For(greeter.DbRef).ToList();
		await Assert.That(greeterSaw.Any(m => m == $"{mover.Name} slips in.")).IsTrue();
	}

	[Test]
	public async ValueTask WalkingThroughAnExitDoesNotWipeUnrelatedContentsCaches()
	{
		var bystanderRoom = await Dig("CacheBystander");
		var bystander = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("CacheThing"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {bystander}={bystanderRoom}"));

		// Warm the bystander room's contents entry.
		var before = await GodParser.FunctionParse(MarkupText.Plain($"[lcon({bystanderRoom})]"));

		var (mover, _, _, _) = await Corridor("CacheWalk");
		await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out"));

		var after = await GodParser.FunctionParse(MarkupText.Plain($"[lcon({bystanderRoom})]"));
		await Assert.That(after!.Message!.ToPlainText()).IsEqualTo(before!.Message!.ToPlainText());
	}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t12.log 2>&1; grep -E "Failed" /tmp/t12.log | head
```

- [ ] **Step 3: Give `CanGoto` a body**

```csharp
	/// <summary>
	/// PennMUSH <c>could_doit</c> (<c>src/predicat.c:75</c>) as <c>do_move</c> uses it: the exit's
	/// basic lock, evaluated against the mover.
	/// </summary>
	public async ValueTask<bool> CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destination)
		=> await PassesLock(who, new AnySharpObject(exit), LockType.Basic);
```

- [ ] **Step 4: Rewrite `GoTo`'s tail**

Replace everything in `GoTo` from the `PermissionService.CanGoto` check onward:

```csharp
		// The leave lock on the room the mover is standing in comes first (move.c:441).
		var currentLocation = await executor.Where();

		if (!await PermissionService.PassesLock(executor, currentLocation.WithExitOption(), LockType.Leave))
		{
			await DidItService.FailLock(parser, executor, currentLocation.WithExitOption(), LockType.Leave,
				MarkupText.Plain(ErrorMessages.Notifications.CantGoThatWay));
			return CallState.Empty;
		}

		if (!await PermissionService.CanGoto(executor, exitObj, destination))
		{
			await DidItService.FailLock(parser, executor, exitObject, LockType.Basic,
				MarkupText.Plain(ErrorMessages.Notifications.CantGoThatWay));
			return CallState.Empty;
		}

		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: exitObject,
			What: "SUCCESS", OWhat: "OSUCCESS", AWhat: "ASUCCESS",
			Loc: currentLocation));

		// @drop / @odrop / @adrop on an exit are shown where the mover ARRIVES.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: exitObject,
			What: "DROP", OWhat: "ODROP", AWhat: "ADROP",
			Loc: destination));

		var destinationObject = destination.WithExitOption();

		var result = destinationObject.IsRoom
			? await MoveService.EnterRoom(parser, executor.AsContent, destination,
				noMoveMsgs: false, executor.Object().DBRef, "move")
			: await MoveService.SafeTel(parser, executor.AsContent, destination,
				noMoveMsgs: false, executor.Object().DBRef, "move");

		if (result.IsT1)
		{
			await NotifyService.Notify(executor, result.AsT1.Value, executor);
			return CallState.Empty;
		}

		// Followers trail the leader only if the leader actually went somewhere.
		var newLocation = await executor.Where();

		if (!newLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await FollowerCommand(parser, executor, currentLocation, "GOTO", exitObj.Object.DBRef);
		}

		return new CallState(destination.ToString());
```

Delete `FailToGoThatWay` entirely — `FailLock` replaces it, and unlike the old helper it evaluates
the attribute instead of echoing its stored text. Its two remaining callers (the unlinked-exit path
at `GeneralCommands.cs:1624`) become:

```csharp
			return resolved.AsT1 == ExitDestinationFailure.Unlinked
				? await FailBasicLock(parser, executor, exitObject)
				: CallState.Empty;
```

with

```csharp
	private async ValueTask<Option<CallState>> FailBasicLock(
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject exitObject)
	{
		await DidItService.FailLock(parser, executor, exitObject, LockType.Basic,
			MarkupText.Plain(ErrorMessages.Notifications.CantGoThatWay));
		return CallState.Empty;
	}
```

- [ ] **Step 5: Port `follower_command`**

In `MoreCommands.cs`, beside the existing `FOLLOWING` helpers:

```csharp
	/// <summary>
	/// Re-issues <paramref name="command"/> for every object following <paramref name="leader"/>
	/// that was standing with them. PennMUSH <c>follower_command</c> (<c>src/move.c:1300</c>).
	/// </summary>
	/// <remarks>
	/// Each follower's command is queued as that follower with the leader as enactor, not run
	/// inline: a chain of followers would otherwise recurse on the stack.
	/// </remarks>
	internal async ValueTask FollowerCommand(
		IMUSHCodeParser parser,
		AnySharpObject leader,
		AnySharpContainer from,
		string command,
		DBRef? toward)
	{
		var followers = await AttributeService.GetAttributeAsync(
			leader, leader, "FOLLOWERS", IAttributeService.AttributeMode.Read, parent: false);

		if (!followers.IsAttribute || followers.AsAttribute.Length == 0)
		{
			return;
		}

		var line = toward is null ? command : $"{command} {toward}";
		var leaderIsHidden = await leader.IsDarkLegal();

		foreach (var token in followers.AsAttribute.Last().Value.ToPlainText()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!DBRef.TryParse(token, out var followerRef))
			{
				continue;
			}

			var node = await Mediator.Send(new GetObjectNodeQuery(followerRef!.Value));

			if (node.IsNone)
			{
				continue;
			}

			var follower = node.Known;

			if (!follower.IsContent)
			{
				continue;
			}

			var followerLocation = await follower.AsContent.Location();

			if (!followerLocation.Object().DBRef.Equals(from.Object().DBRef))
			{
				continue;
			}

			if (leaderIsHidden && !await follower.HasPower("See_All"))
			{
				continue;
			}

			await NotifyService.NotifyLocalized(follower.Object().DBRef,
				nameof(ErrorMessages.Notifications.YouFollowFormat), leader.Object().Name);

			await Mediator.Send(new QueueCommandListRequest(
				MarkupText.Plain(line),
				parser.CurrentState with
				{
					Executor = follower.Object().DBRef,
					Enactor = leader.Object().DBRef,
					Caller = leader.Object().DBRef
				},
				new DbRefAttribute(follower.Object().DBRef, DefaultSemaphoreAttributeArray),
				-1));
		}
	}
```

Add to `ErrorMessages.Notifications` and `Notifications.resx`:

```csharp
		public const string YouFollowFormat = "You follow {0}.";
```

- [ ] **Step 6: Run the tests**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t12.log 2>&1; grep -E "Passed|Failed" /tmp/t12.log | tail -3
```

- [ ] **Step 7: Format and commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Implementation SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Put GOTO on the movement pipeline and enforce exit locks"
```

---

### Task 13: `@teleport` — `/SILENT`, the `TPORT` attributes, no hand-queued look

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/GeneralCommands.cs:1646-1830`
- Modify: `SharpMUSH.Tests/Commands/AttributeTreePatternVisibilityTests.cs`,
  `ChannelPermissionTests.cs`, `ChannelMatchRecallTests.cs`,
  `SharpMUSH.Tests/Functions/HiddenWhoVisibilityTests.cs`
- Modify: `SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpcmd.md`

PennMUSH's switch is `SILENT` (`src/command.c:312`), and its attributes are
`TPORT`/`OTPORT`/`ATPORT`/`OXTPORT` (`src/wiz.c:490-494`). `OTELEPORT`/`OXTELEPORT` have no PennMUSH
counterpart and go. `silent` suppresses the `TPORT` triad and the `MOVE` triad; it does not suppress
`ENTER`/`LEAVE`, and it does not suppress the automatic look.

- [ ] **Step 1: Write the failing test**

```csharp
	[Test]
	public async ValueTask SilentTeleportSuppressesTheMoveTriadButNotTheEnterTriad()
	{
		var destination = await Dig("SilentSplit");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentSplitMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AMOVE {mover.DbRef}=&MOVED me=yes"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AENTER {destination}=&ENTERED me=yes"));

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@teleport/silent {mover.DbRef}={destination}"));
		await Scheduler.DrainImmediateQueueForTests();

		var moved = await GodParser.FunctionParse(MarkupText.Plain($"[get({mover.DbRef}/MOVED)]"));
		var entered = await GodParser.FunctionParse(MarkupText.Plain($"[get({destination}/ENTERED)]"));

		await Assert.That(moved!.Message!.ToPlainText().Trim()).IsEmpty();
		await Assert.That(entered!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}

	[Test]
	public async ValueTask TeleportFiresTheTportTriad()
	{
		var destination = await Dig("TportDest");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TportMover");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TPORT {mover.DbRef}=The world folds."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport {mover.DbRef}={destination}")));

		await Assert.That(seen.Any(m => m == "The world folds.")).IsTrue();
	}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*Tport*" > /tmp/t13.log 2>&1; grep -E "Failed" /tmp/t13.log | head
```

- [ ] **Step 3: Rename the switch**

In the `[SharpCommand]` attribute on `Teleport`, `Switches = ["LIST", "INSIDE", "QUIET"]` becomes
`Switches = ["LIST", "INSIDE", "SILENT"]`, and `parser.CurrentState.Switches.Contains("QUIET")`
becomes `Contains("SILENT")`.

- [ ] **Step 4: Replace the move call and the hand-queued look**

```csharp
			var isSilent = parser.CurrentState.Switches.Contains("SILENT");
			var currentLocation = await target.AsContent.Location();

			if (!isSilent && !currentLocation.Object().DBRef.Equals(destinationContainer.Object().DBRef))
			{
				await DidItService.DidIt(parser, new DidItRequest(
					Player: target, Thing: target, OWhat: "OXTPORT",
					Loc: currentLocation, Env0: executor.Object().DBRef));
			}

			var moveResult = await MoveService.SafeTel(
				parser, targetContent, destinationContainer, isSilent, executor.Object().DBRef, "teleport");

			if (moveResult.IsT1)
			{
				await NotifyService.Notify(executor, moveResult.AsT1.Value, executor);
				continue;
			}

			if (!isSilent && !currentLocation.Object().DBRef.Equals(destinationContainer.Object().DBRef))
			{
				await DidItService.DidIt(parser, new DidItRequest(
					Player: target, Thing: target,
					What: "TPORT", OWhat: "OTPORT", AWhat: "ATPORT",
					Loc: destinationContainer,
					Env0: executor.Object().DBRef, Env1: currentLocation.Object().DBRef));
			}
```

Delete the whole `if (target.IsPlayer && !isSilent)` block that follows — its
`TeleportedPlayerNotified` notify and its `QueueCommandListRequest` for `look` are both replaced by
`EnterRoom`'s automatic look. Remove `TeleportedPlayerNotified` from `ErrorMessages.Notifications`
and both resx files if nothing else uses it.

- [ ] **Step 5: Update the four test files that used the old switch**

```bash
grep -rln "teleport/quiet" --include="*.cs" . | grep -v obj/ | xargs sed -i 's#teleport/quiet#teleport/silent#g'
```

Then, in `ChannelPermissionTests.cs`, replace the comment block above the teleport in `CreateMortal`
(the one explaining that `/QUIET` avoids a racing queued look) with:

```csharp
		// /SILENT so the arrival produces no movement messages inside another test's assertion
		// window. The automatic look still runs, but it runs inline, so it cannot land later.
```

Apply the same trim to the equivalent comment in `AttributeTreePatternVisibilityTests.cs`.

- [ ] **Step 6: Update the helpfile**

In `SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpcmd.md`, change the documented `@teleport`
switch from `/quiet` to `/silent` and describe what it suppresses: the `@move`/`@omove`/`@amove` and
`@tport`/`@otport`/`@atport` triads, not arrival and departure messages and not the automatic look.

- [ ] **Step 7: Run the affected suites**

```bash
for c in MovementParityTests ChannelPermissionTests AttributeTreePatternVisibilityTests ChannelMatchRecallTests HiddenWhoVisibilityTests; do
  dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/$c/*" > /tmp/t13-$c.log 2>&1
  echo "$c: $(grep -cE '^\s*Failed' /tmp/t13-$c.log) failed"
done
```

- [ ] **Step 8: Format and commit**

```bash
for d in SharpMUSH.Implementation SharpMUSH.Tests SharpMUSH.Library; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Rename @teleport/quiet to /silent and use PennMUSH's TPORT attributes"
```

---

### Task 14: `ENTER`, `LEAVE` and `HOME` on the pipeline

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/MoreCommands.cs:1542-1704` (`ENTER`),
  `2199-2340` (`LEAVE`), `2115-2198` (`HOME`)
- Test: `SharpMUSH.Tests/Commands/MovementParityTests.cs`

`ENTER` currently calls `ExecuteMoveAsync` — which fires the enter triad — and then fires
`ENTER`/`OENTER`/`OXENTER`/`AENTER` again inline, so every one of them lands twice. `LEAVE`
duplicates the same way. `HOME` lacks PennMUSH's messages (`src/move.c:392-406`).

- [ ] **Step 1: Write the failing tests**

```csharp
	[Test]
	public async ValueTask EnteringAContainerFiresItsEnterTriadExactlyOnce()
	{
		var room = await Dig("EnterOnce");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EnterOnceMover");
		var box = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("EnterBox"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {box}=ENTER_OK"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ENTER {box}=You squeeze inside."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain($"enter {box}")));

		await Assert.That(seen.Count(m => m == "You squeeze inside.")).IsEqualTo(1);
	}

	[Test]
	public async ValueTask GoingHomeSaysSoThreeTimesAndTellsTheRoom()
	{
		var room = await Dig("HomeStart");
		var home = await Dig("HomeTarget");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HomeMover");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HomeWatch");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {mover.DbRef}={home}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={room}"));

		var moverSaw = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("home")));

		await Assert.That(moverSaw.Count(m => m == "There's no place like home...")).IsEqualTo(3);

		var watcherSaw = WebAppFactoryArg.Notifications.For(watcher.DbRef).ToList();
		await Assert.That(watcherSaw.Any(m => m == $"{mover.Name} goes home.")).IsTrue();
	}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t14.log 2>&1; grep -E "Failed" /tmp/t14.log | head
```

- [ ] **Step 3: Strip the duplicate triads from `ENTER`**

Delete, from the success path of the `ENTER` command, the `enterAttr`, `oenterAttr`, `oxenterAttr`
and `aenterAttr` blocks and the trailing `"You enter {name}."` notify. `MoveService.EnterRoom`
already fires the whole triad, and PennMUSH has no such trailing message. The call becomes:

```csharp
		var moveResult = await MoveService.EnterRoom(
			parser, executor.AsContent, objectToEnter.AsContainer,
			noMoveMsgs: false, executor.Object().DBRef, "enter");

		if (moveResult.IsT1)
		{
			await NotifyService.Notify(executor, moveResult.AsT1.Value, executor);
		}

		return CallState.Empty;
```

Keep the enter-lock failure path above it, but route it through `FailLock`:

```csharp
			await DidItService.FailLock(parser, executor, objectToEnter, LockType.Enter);
			return CallState.Empty;
```

deleting the hand-rolled `EFAIL`/`OEFAIL`/`AEFAIL` blocks it replaces.

- [ ] **Step 4: Do the same for `LEAVE`**

Replace `LEAVE`'s move and its inline `LEAVE`/`OLEAVE`/`OXLEAVE`/`ALEAVE` blocks with a single
`EnterRoom` into the container's own location, and route its leave-lock failure through
`FailLock(..., LockType.Leave)`.

- [ ] **Step 5: Give `HOME` PennMUSH's messages**

```csharp
		var location = await executor.Where();
		var home = await executor.Object().Home.WithCancellation(CancellationToken.None);

		if (!await executor.IsDark() && !await location.WithExitOption().IsDark())
		{
			await CommunicationService.SendToRoomAsync(
				executor,
				location,
				_ => MarkupText.Plain(string.Format(ErrorMessages.Notifications.GoesHomeFormat, executor.Object().Name)),
				INotifyService.NotificationType.Emit,
				excludeObjects: [executor],
				interact: IPermissionService.InteractType.See);
		}

		// PennMUSH sends all three (move.c:403-405).
		for (var i = 0; i < 3; i++)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoPlaceLikeHome), executor);
		}

		await MoveService.SafeTel(parser, executor.AsContent, home, noMoveMsgs: false,
			executor.Object().DBRef, "home");
```

Add to `ErrorMessages.Notifications` and `Notifications.resx`:

```csharp
		public const string NoPlaceLikeHome = "There's no place like home...";
		public const string GoesHomeFormat = "{0} goes home.";
```

- [ ] **Step 6: Run and commit**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t14.log 2>&1; grep -E "Passed|Failed" /tmp/t14.log | tail -3
for d in SharpMUSH.Implementation SharpMUSH.Library SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Route ENTER, LEAVE and HOME through the movement pipeline"
```

---

### Task 15: Remove the invented messages and the dead surface

**Files:**
- Modify: `SharpMUSH.Library/Services/MoveService.cs`, `Interfaces/IMoveService.cs`
- Modify: `SharpMUSH.Library/Definitions/ErrorMessages.cs`,
  `SharpMUSH.Library/Resources/Notifications.resx`, `Notifications.fr.resx`
- Modify: `SharpMUSH.Tests/Services/CachingBehaviorTests.cs:569`

None of the following exist in PennMUSH; this is pre-release software, so they are deleted rather
than kept behind an option.

- [ ] **Step 1: Delete `ExecuteMoveAsync`, `CanMoveAsync` and `CalculateMoveCostAsync`**

All three go from `IMoveService` and `MoveService`. `ExecuteMoveAsync` is superseded by
`EnterRoom`/`SafeTel`. `CalculateMoveCostAsync` always returned zero and has no caller.

`CanMoveAsync` required `Controls(who, target)` for **every** move, which PennMUSH never does — it
gates exit traversal on the exit's basic lock plus the room's leave lock (`src/move.c:441-456`),
both of which now live in `GoTo` (Task 12), and it gates a container on its enter lock, which lives
in `ENTER` (Task 14). Keeping a control check here would forbid an object moving anything it does
not own, which is not the game's rule.

Confirm nothing still calls it before deleting:

```bash
grep -rn "CanMoveAsync\|CalculateMoveCostAsync\|ExecuteMoveAsync" --include="*.cs" . | grep -v obj/
```

Update the remaining external callers:

- `SharpMUSH.Library/Services/ObjectDestructionService.cs:262` → `EnterRoom(...)` with
  `cause: "container destroyed"`.
- `SharpMUSH.Library/Services/DatabaseConversion/PennMUSHDatabaseConverter.cs:574` →
  `EnterRoom(...)` with `noMoveMsgs: true` — an import must not narrate itself.
- `SharpMUSH.Tests/Services/CachingBehaviorTests.cs:569` → `EnterRoom(..., noMoveMsgs: true, ...)`.

- [ ] **Step 2: Delete the invented strings**

Remove from `ErrorMessages.Notifications` and both resx files anything now unreferenced. Verify
before deleting each:

```bash
for key in TeleportedPlayerNotified; do
  echo "$key: $(grep -rn "$key" --include='*.cs' . | grep -v obj/ | grep -v ErrorMessages.cs | wc -l) references"
done
grep -rn "You sense that you have moved\|You enter " --include="*.cs" . | grep -v obj/ | head
```

Delete the `"You sense that you have moved from X to Y."` string with
`NotifyContentsOfMoveAsync` (already removed in Task 9) and the `"You enter {name}."` literal
(removed in Task 14). Confirm neither survives.

- [ ] **Step 3: Build clean and run everything**

```bash
dotnet build SharpMUSH.sln > /tmp/t15.log 2>&1; grep -E "error|Build succeeded" /tmp/t15.log | head
dotnet run --project SharpMUSH.Tests > /tmp/t15full.log 2>&1; grep -E "^\s*(Passed|Failed|Skipped)" /tmp/t15full.log | tail -5
```

- [ ] **Step 4: Format and commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Remove the movement messages PennMUSH does not have"
```

---

## Phase E — The sweep

Each task here replaces hand-rolled triads with `DidIt`/`FailLock`. The behavior change common to
all of them is that action attributes are now **queued** rather than run inline, which is PennMUSH's
ordering (`queue_attribute_base`); tests asserting on inline ordering are updated to the
PennMUSH-correct order rather than the primitive being bent to match them.

### Task 16: `GET`, `DROP` and `GIVE`

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/MoreCommands.cs:1185-1280` (`DROP`),
  `1756-1908` (`GET`), `1909-2114` (`GIVE`)
- Test: `SharpMUSH.Tests/Commands/` — whichever suites currently cover these

- [ ] **Step 1: Find the tests that will move**

```bash
grep -rln "SUCCESS\|ODROP\|OGIVE\|ORECEIVE" --include="*.cs" SharpMUSH.Tests/ | sort -u
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Command*/*" > /tmp/t16base.log 2>&1
grep -cE "^\s*Failed" /tmp/t16base.log
```

Record the baseline failure count; it must not rise.

- [ ] **Step 2: Replace `DROP`'s triad**

The success path's `DROP`/`ODROP`/`ADROP` blocks become one call:

```csharp
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: objectToDrop,
			What: "DROP", OWhat: "ODROP", AWhat: "ADROP",
			Loc: await executor.Where()));
```

and the drop-lock failure becomes `FailLock(parser, executor, objectToDrop, LockType.Drop)`.

- [ ] **Step 3: Replace `GET`'s triad**

```csharp
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: objectToGet,
			What: "SUCCESS", OWhat: "OSUCCESS", AWhat: "ASUCCESS",
			Loc: await executor.Where()));
```

with the take-lock failure as `FailLock(parser, executor, objectToGet, LockType.Take)`.

- [ ] **Step 4: Replace `GIVE`'s two triads**

`GIVE` fires both a give triad on the giver's item and a receive triad on the recipient:

```csharp
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: given,
			What: "GIVE", OWhat: "OGIVE", AWhat: "AGIVE", Loc: location));

		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: recipient,
			What: "RECEIVE", OWhat: "ORECEIVE", AWhat: "ARECEIVE", Loc: location));
```

`GIVE`'s existing `/SILENT` switch keeps its meaning: it suppresses both triads.

- [ ] **Step 5: Run and compare against the baseline**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Command*/*" > /tmp/t16.log 2>&1
grep -cE "^\s*Failed" /tmp/t16.log
grep -E "^\s*Failed" /tmp/t16.log | head -20
```

For each newly failing test, decide whether it asserted inline action-attribute ordering. If so,
insert `await Scheduler.DrainImmediateQueueForTests();` before the assertion and keep the assertion.
If it asserted something else, the change is a regression — fix the code, not the test.

- [ ] **Step 6: Format and commit**

```bash
for d in SharpMUSH.Implementation SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Put GET, DROP and GIVE on the did_it primitive"
```

---

### Task 17: `USE`, `BUY` and `PAGE`

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/MoreCommands.cs:844-1004` (`BUY`),
  `2823-2900` (`USE`), `2480-2560` (`PAGE`'s lock failure)
- Modify: `SharpMUSH.Implementation/Commands/BuildingCommands.cs` (the `USE` reference there)

- [ ] **Step 1: Replace `USE`'s triad and its use-lock failure**

```csharp
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: target,
			What: "USE", OWhat: "OUSE", AWhat: "AUSE", Loc: await executor.Where()));
```

and, on failure, `FailLock(parser, executor, target, LockType.Use)` — which resolves to
`UFAIL`/`OUFAIL`/`AUFAIL` through the table from Task 3, replacing the hand-rolled blocks.

- [ ] **Step 2: Replace `BUY`'s triad**

```csharp
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: vendor,
			What: "BUY", OWhat: "OBUY", AWhat: "ABUY", Loc: await executor.Where()));
```

- [ ] **Step 3: Replace `PAGE`'s hand-built lock attribute names**

`MoreCommands.cs:2504` builds `"PAGE_LOCK\`AFAILURE"` by hand. That is exactly what
`LockMessages.FailureAttributes(LockType.Page)` returns, so the whole block becomes:

```csharp
			await DidItService.FailLock(parser, executor, recipient, LockType.Page);
```

- [ ] **Step 4: Run the affected suites and commit**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Command*/*" > /tmp/t17.log 2>&1
grep -cE "^\s*Failed" /tmp/t17.log
for d in SharpMUSH.Implementation; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Put USE, BUY and PAGE on the did_it primitive"
```

---

### Task 18: `@name` and the connect announcements

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/WizardCommands.cs` (the `@name` triad)
- Modify: `SharpMUSH.Library/Services/ConnectionAnnounceService.cs:354` region

PennMUSH `do_name` fires `ONAME`/`ANAME` through `real_did_it` (`src/set.c:158`), and the
connect/disconnect announcements fire `ACONNECT`/`ADISCONNECT` the same way.

- [ ] **Step 1: Replace `@name`'s triad**

```csharp
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: target,
			OWhat: "ONAME", AWhat: "ANAME", Loc: await target.Where()));
```

- [ ] **Step 2: Replace the connect and disconnect triads**

In `ConnectionAnnounceService`, replace the hand-rolled `ACONNECT`/`ADISCONNECT` execution with
`DidIt` carrying only `AWhat`. Keep the existing state-propagation shape at line 354 — it already
threads the counters correctly — and add `MoveDepth` to it per Task 4.

- [ ] **Step 3: Run the connection suites and commit**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Connect*/*" > /tmp/t18.log 2>&1
grep -E "Passed|Failed" /tmp/t18.log | tail -3
for d in SharpMUSH.Implementation SharpMUSH.Library; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Put @name and the connect announcements on the did_it primitive"
```

---

## Phase F — Verification and documentation

### Task 19: Audit every `MoveObjectCommand` for `OldContainer`

**Files:**
- Modify: any caller found below
- Test: `SharpMUSH.Tests/Commands/MovementParityTests.cs`

`MoveObjectCommand.CacheTags` falls back to the global `CacheTags.ObjectContents` when
`OldContainer` is null, wiping every container's contents list. Every caller must supply it.

- [ ] **Step 1: Find the callers that omit it**

```bash
grep -rn "new MoveObjectCommand(" --include="*.cs" . | grep -v obj/
```

For each hit, confirm the call passes `OldContainer:`. `MoveService.MoveIt` does (Task 9); check
`ObjectDestructionService`, `PennMUSHDatabaseConverter`, `RescueFromVoidAsync` and any test helper.

- [ ] **Step 2: Add a guard test**

```csharp
	[Test]
	public async ValueTask EveryMoveCommandCarriesItsOriginContainer()
	{
		// A move that omits OldContainer falls back to the global contents tag, which wipes the
		// contents entry of every container in the game. Cheaper to assert here than to notice
		// it as a cache-miss storm in production.
		var bystanderRoom = await Dig("GuardRoom");
		var bystander = await TestIsolationHelpers.CreateTestThingAsync(
			WebAppFactoryArg.Services, Mediator, TestIsolationHelpers.GenerateUniqueName("GuardThing"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {bystander}={bystanderRoom}"));

		var warm = await GodParser.FunctionParse(MarkupText.Plain($"[lcon({bystanderRoom})]"));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GuardMover");
		var elsewhere = await Dig("GuardElsewhere");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {mover.DbRef}={elsewhere}"));

		var after = await GodParser.FunctionParse(MarkupText.Plain($"[lcon({bystanderRoom})]"));
		await Assert.That(after!.Message!.ToPlainText()).IsEqualTo(warm!.Message!.ToPlainText());
	}
```

- [ ] **Step 3: Run the caching suite together with the movement suite**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/CachingBehaviorTests/*" > /tmp/t19a.log 2>&1
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MovementParityTests/*" > /tmp/t19b.log 2>&1
grep -E "Passed|Failed" /tmp/t19a.log /tmp/t19b.log | tail -6
```

- [ ] **Step 4: Commit**

```bash
for d in SharpMUSH.Library SharpMUSH.Implementation SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**"
done
git add -A
git commit -m "Make every move name its origin container"
```

---

### Task 20: Documentation and the full-suite gate

**Files:**
- Modify: `SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpcmd.md`, `sharptop.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-09-09-movement-behavior-design.md`

- [ ] **Step 1: Document the movement attributes**

In `sharpcmd.md`, add a movement section listing the attributes in the order they fire, matching
Task 9's implementation: `@oxmove`, then `@leave`/`@oleave`/`@aleave`, the zone attributes,
`@oxleave`, `@oxenter`, `@enter`/`@oenter`/`@aenter`, then `@move`/`@omove`/`@amove`. State which
are suppressed by a silent move (only the `@move` triad and, for `@teleport`, the `@tport` triad)
and that the automatic look always runs, obeying `TERSE`.

- [ ] **Step 2: Document the queue quota**

`sharptop.md:1313` already describes `player_queue_limit`; add that exceeding it halts the object
and notifies its owner, matching the message added in Task 5.

- [ ] **Step 3: Note the new services in `CLAUDE.md`**

Under **Server-Side: Commands & Functions**, add a short paragraph: attribute triads go through
`IDidItService` (`DidIt` for the message/o-message/action triad, `FailLock` for a failed lock),
never hand-rolled; the automatic look is `ILookService.LookRoom(..., LookKey.Auto)`; movement is
`IMoveService.EnterRoom`/`SafeTel`, and no command sends `MoveObjectCommand` directly.

- [ ] **Step 4: Mark the spec implemented**

Change the spec's `Status:` line to `Status: implemented`, and delete §10's "Out of scope" entry for
the queue restructure only if Task 5 actually restructured it (it should not have — it added a quota
to the existing structure).

- [ ] **Step 5: Run the whole suite and both bUnit suites**

```bash
dotnet run --project SharpMUSH.Tests > /tmp/final.log 2>&1
grep -E "^\s*(Passed|Failed|Skipped)" /tmp/final.log | tail -5
grep -E "^\s*Failed" /tmp/final.log | head -30
dotnet run --project SharpMUSH.Tests.BUnit > /tmp/final-bunit.log 2>&1
grep -E "^\s*(Passed|Failed)" /tmp/final-bunit.log | tail -3
```

Expected: zero failures. Do not proceed past a failure by adjusting an assertion unless you can
state, in the commit message, which PennMUSH behavior the old assertion contradicted.

- [ ] **Step 6: Verify formatting across every touched project**

```bash
for d in SharpMUSH.Library SharpMUSH.Implementation SharpMUSH.Server SharpMUSH.Tests; do
  dotnet format whitespace --folder $d --exclude "**/bin/**" --exclude "**/obj/**" --verify-no-changes || echo "NEEDS FORMAT: $d"
done
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Document the movement attribute order and the queue quota"
```
