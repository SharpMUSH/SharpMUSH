# Detached WebSocket Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep a WebSocket play session alive across a brief drop / network switch and rebind the reconnecting socket to the *same* handle, so the character is never logged out (true pinning), for reconnects within a grace window.

**Architecture:** A per-handle mutable `SessionSink` holds the current transport; the registered output delegate routes through it (buffering to the replay store when detached). On drop the pump *detaches* (via an isolated `DetachedSessionTracker` that schedules the real disconnect after a grace window) instead of disconnecting. A mandatory `hello`/`resume` first frame lets the pump rebind a reconnect to a live detached handle. Single-instance, WebSocket-only, zero engine changes.

**Tech Stack:** .NET 10, ASP.NET Core, NATS.Net (existing replay/token stores), TUnit + NSubstitute.

## Global Constraints

- Target framework `net10.0`; tabs (indent 2) in C#; `TreatWarningsAsErrors` on every project.
- WebSocket only — do not touch `TelnetServer`.
- Zero engine changes — pinning works by *not* publishing `ConnectionClosedMessage` during grace and rebinding to the same handle number.
- Single ConnectionServer instance / sticky reconnect (detached state is in-memory).
- Reuse existing `ITerminalReplayStore` / `IResumeTokenStore` (already always-on, 24h retention).
- `Session:GraceSeconds` new config, default 120.
- Commit after every task.

---

## File Structure

- Create `Services/SessionSink.cs` — per-handle mutable current-transport holder.
- Create `Services/SessionSinkRegistry.cs` — `handle → SessionSink` map (singleton).
- Create `Services/DetachedSessionTracker.cs` — grace scheduling (detach/reattach), with an injectable scheduler for tests.
- Modify `ProtocolHandlers/ConnectionPump.cs` — output via sink; `hello`/`resume` first-frame decision (rebind vs fresh); detach-on-drop.
- Modify `Program.cs` — register the registry + tracker; read `Session:GraceSeconds`.
- Modify (client) `Services/WebSocketClientService.cs` — send `hello`/`resume` first frame; handle the `reattached` ack (skip re-login).
- Tests: `Tests/ConnectionServer/SessionSinkTests.cs`, `DetachedSessionTrackerTests.cs`, extend `ConnectionPumpTests.cs`.

---

## Task 1: SessionSink + registry

**Files:**
- Create: `SharpMUSH.ConnectionServer/Services/SessionSink.cs`
- Create: `SharpMUSH.ConnectionServer/Services/SessionSinkRegistry.cs`
- Test: `SharpMUSH.Tests/ConnectionServer/SessionSinkTests.cs`

**Interfaces:**
- Produces:
  - `sealed class SessionSink { IDuplexTransport? Current {get;} void Attach(IDuplexTransport); void Detach(); }`
  - `sealed class SessionSinkRegistry { SessionSink GetOrCreate(long handle); SessionSink? Get(long handle); void Remove(long handle); }`

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/ConnectionServer/SessionSinkTests.cs
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class SessionSinkTests
{
	private sealed class DummyTransport : IDuplexTransport
	{
		public string Kind => "fake";
		public string RemoteIp => "ip";
		public string Hostname => "host";
		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => Task.CompletedTask;
		public Task<string?> ReceiveTextAsync(CancellationToken ct) => Task.FromResult<string?>(null);
		public Task CloseAsync() => Task.CompletedTask;
	}

	[Test]
	public async Task Attach_then_Detach_updates_Current()
	{
		var sink = new SessionSink();
		await Assert.That(sink.Current).IsNull();
		var t = new DummyTransport();
		sink.Attach(t);
		await Assert.That(sink.Current).IsSameReferenceAs(t);
		sink.Detach();
		await Assert.That(sink.Current).IsNull();
	}

	[Test]
	public async Task Registry_GetOrCreate_is_stable_and_Remove_clears()
	{
		var reg = new SessionSinkRegistry();
		var a = reg.GetOrCreate(5);
		var b = reg.GetOrCreate(5);
		await Assert.That(a).IsSameReferenceAs(b);
		reg.Remove(5);
		await Assert.That(reg.Get(5)).IsNull();
	}
}
```

- [ ] **Step 2: Run — expect FAIL (types missing)**

Run: `dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj -c Debug`
Expected: compile error — `SessionSink` / `SessionSinkRegistry` undefined.

- [ ] **Step 3: Implement**

```csharp
// SharpMUSH.ConnectionServer/Services/SessionSink.cs
using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Per-handle holder of the currently-attached transport. The registered output delegate routes
/// through this, so a session's output can be rebound to a new socket on reconnect, or buffered
/// (replay only) while detached.
/// </summary>
public sealed class SessionSink
{
	private volatile IDuplexTransport? _current;
	public IDuplexTransport? Current => _current;
	public void Attach(IDuplexTransport transport) => _current = transport;
	public void Detach() => _current = null;
}
```

```csharp
// SharpMUSH.ConnectionServer/Services/SessionSinkRegistry.cs
using System.Collections.Concurrent;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Maps a handle to its <see cref="SessionSink"/> so a reconnecting pump can rebind it.</summary>
public sealed class SessionSinkRegistry
{
	private readonly ConcurrentDictionary<long, SessionSink> _sinks = new();
	public SessionSink GetOrCreate(long handle) => _sinks.GetOrAdd(handle, _ => new SessionSink());
	public SessionSink? Get(long handle) => _sinks.TryGetValue(handle, out var s) ? s : null;
	public void Remove(long handle) => _sinks.TryRemove(handle, out _);
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet run --project SharpMUSH.Tests -c Debug -- --treenode-filter "/*/*/SessionSinkTests/*"`
Expected: `succeeded: 2`.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.ConnectionServer/Services/SessionSink.cs SharpMUSH.ConnectionServer/Services/SessionSinkRegistry.cs SharpMUSH.Tests/ConnectionServer/SessionSinkTests.cs
git commit -m "feat: SessionSink + registry for rebindable session output"
```

---

## Task 2: DetachedSessionTracker (grace scheduling)

**Files:**
- Create: `SharpMUSH.ConnectionServer/Services/DetachedSessionTracker.cs`
- Test: `SharpMUSH.Tests/ConnectionServer/DetachedSessionTrackerTests.cs`

**Interfaces:**
- Produces:
  - `interface IGraceScheduler { IDisposable Schedule(TimeSpan delay, Func<Task> action); }`
  - `sealed class TimerGraceScheduler : IGraceScheduler` (production; `System.Threading.Timer`).
  - `sealed class DetachedSessionTracker(IGraceScheduler scheduler)` with:
    - `void Detach(long handle, Func<Task> onGraceExpired, TimeSpan grace)`
    - `bool Reattach(long handle)` — true if it was detached (cancels the timer).
    - `bool IsDetached(long handle)`

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/ConnectionServer/DetachedSessionTrackerTests.cs
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class DetachedSessionTrackerTests
{
	// Deterministic scheduler: captures the action so the test fires it manually.
	private sealed class ManualScheduler : IGraceScheduler
	{
		public Func<Task>? Captured { get; private set; }
		public bool Disposed { get; private set; }
		private sealed class Handle(ManualScheduler s) : IDisposable
		{
			public void Dispose() => s.Disposed = true;
		}
		public IDisposable Schedule(TimeSpan delay, Func<Task> action)
		{
			Captured = action;
			return new Handle(this);
		}
	}

	[Test]
	public async Task Detach_then_grace_expiry_fires_onGraceExpired_once()
	{
		var sched = new ManualScheduler();
		var tracker = new DetachedSessionTracker(sched);
		var fired = 0;
		tracker.Detach(7, () => { fired++; return Task.CompletedTask; }, TimeSpan.FromSeconds(120));

		await Assert.That(tracker.IsDetached(7)).IsTrue();
		await sched.Captured!(); // simulate grace expiry
		await Assert.That(fired).IsEqualTo(1);
	}

	[Test]
	public async Task Reattach_cancels_the_grace_timer()
	{
		var sched = new ManualScheduler();
		var tracker = new DetachedSessionTracker(sched);
		tracker.Detach(7, () => Task.CompletedTask, TimeSpan.FromSeconds(120));

		var was = tracker.Reattach(7);

		await Assert.That(was).IsTrue();
		await Assert.That(sched.Disposed).IsTrue();       // timer cancelled
		await Assert.That(tracker.IsDetached(7)).IsFalse();
	}

	[Test]
	public async Task Reattach_of_unknown_handle_returns_false()
	{
		var tracker = new DetachedSessionTracker(new ManualScheduler());
		await Assert.That(tracker.Reattach(99)).IsFalse();
	}
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj -c Debug`
Expected: compile error — `DetachedSessionTracker` / `IGraceScheduler` undefined.

- [ ] **Step 3: Implement**

```csharp
// SharpMUSH.ConnectionServer/Services/DetachedSessionTracker.cs
using System.Collections.Concurrent;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Schedules a delayed action; abstracted so grace expiry is deterministic in tests.</summary>
public interface IGraceScheduler
{
	IDisposable Schedule(TimeSpan delay, Func<Task> action);
}

/// <summary>Production scheduler backed by a one-shot <see cref="Timer"/>.</summary>
public sealed class TimerGraceScheduler : IGraceScheduler
{
	public IDisposable Schedule(TimeSpan delay, Func<Task> action)
	{
		var timer = new Timer(_ => _ = action(), null, delay, Timeout.InfiniteTimeSpan);
		return timer;
	}
}

/// <summary>
/// Tracks handles whose socket dropped but whose session is held open for a grace window. On
/// expiry the scheduled <c>onGraceExpired</c> (the real disconnect) runs; a reconnect within the
/// window cancels it via <see cref="Reattach"/>.
/// </summary>
public sealed class DetachedSessionTracker(IGraceScheduler scheduler)
{
	private readonly ConcurrentDictionary<long, IDisposable> _pending = new();

	public void Detach(long handle, Func<Task> onGraceExpired, TimeSpan grace)
	{
		var registration = scheduler.Schedule(grace, async () =>
		{
			if (_pending.TryRemove(handle, out _))
				await onGraceExpired();
		});

		// Replace any prior pending timer for this handle.
		if (_pending.TryRemove(handle, out var previous))
			previous.Dispose();
		_pending[handle] = registration;
	}

	public bool Reattach(long handle)
	{
		if (!_pending.TryRemove(handle, out var registration))
			return false;
		registration.Dispose();
		return true;
	}

	public bool IsDetached(long handle) => _pending.ContainsKey(handle);
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet run --project SharpMUSH.Tests -c Debug -- --treenode-filter "/*/*/DetachedSessionTrackerTests/*"`
Expected: `succeeded: 3`.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.ConnectionServer/Services/DetachedSessionTracker.cs SharpMUSH.Tests/ConnectionServer/DetachedSessionTrackerTests.cs
git commit -m "feat: DetachedSessionTracker with injectable grace scheduler"
```

---

## Task 3: ConnectionPump — output via sink, detach-on-drop, first-frame rebind

**Files:**
- Modify: `SharpMUSH.ConnectionServer/ProtocolHandlers/ConnectionPump.cs`
- Modify: `SharpMUSH.Tests/ConnectionServer/ConnectionPumpTests.cs`

**Interfaces:**
- Consumes: `SessionSinkRegistry`, `DetachedSessionTracker`, `ITerminalReplayStore`, `IResumeTokenStore`, `IConnectionServerService`, `IMessageBus`, `IDescriptorGeneratorService`, `SeqEnvelope`, `IDuplexTransport`.
- Produces: `ConnectionPump(logger, connectionService, publishEndpoint, descriptorGenerator, replayStore, resumeTokens, sinkRegistry, detachedTracker, TimeSpan grace)` with `Task RunAsync(IDuplexTransport transport, long candidateHandle, CancellationToken ct)`.

The full rewritten file:

- [ ] **Step 1: Rewrite ConnectionPump.cs**

```csharp
// SharpMUSH.ConnectionServer/ProtocolHandlers/ConnectionPump.cs
using System.Text;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Owns the shared connection lifecycle. Output is sequence-wrapped and buffered for replay, routed
/// through a per-handle <see cref="SessionSink"/> so it can be rebound to a new socket on reconnect.
/// On drop the session is DETACHED (held for a grace window) instead of disconnected, so a quick
/// reconnect rebinds to the same handle and the engine never logs the character out.
/// </summary>
public sealed class ConnectionPump(
	ILogger<ConnectionPump> logger,
	IConnectionServerService connectionService,
	IMessageBus publishEndpoint,
	IDescriptorGeneratorService descriptorGenerator,
	ITerminalReplayStore replayStore,
	IResumeTokenStore resumeTokens,
	SessionSinkRegistry sinkRegistry,
	DetachedSessionTracker detachedTracker,
	TimeSpan grace)
{
	public async Task RunAsync(IDuplexTransport transport, long candidateHandle, CancellationToken ct)
	{
		long handle;

		var firstFrame = await transport.ReceiveTextAsync(ct);
		if (firstFrame is null)
		{
			descriptorGenerator.ReleaseWebSocketDescriptor(candidateHandle);
			return; // peer closed before saying anything
		}

		if (SeqEnvelope.TryReadResume(firstFrame, out var token, out var lastSeq)
			&& await TryRebindAsync(transport, token, lastSeq, ct) is { } rebound)
		{
			descriptorGenerator.ReleaseWebSocketDescriptor(candidateHandle);
			handle = rebound;
		}
		else
		{
			handle = candidateHandle;
			await RegisterFreshAsync(transport, handle, ct);

			// resume-to-dead: still replay the old handle's durable buffer, then continue fresh.
			if (SeqEnvelope.TryReadResume(firstFrame, out var deadToken, out var deadLastSeq))
			{
				var (found, oldHandle) = await resumeTokens.TryResolveAsync(deadToken, ct);
				if (found)
					foreach (var f in await replayStore.AfterAsync(oldHandle, deadLastSeq, ct))
						await transport.SendAsync(f, ct);
			}
			else if (!IsHello(firstFrame))
			{
				// Not hello and not resume — a real command arrived first; don't drop it.
				await PublishInputAsync(handle, firstFrame, ct);
			}
		}

		try
		{
			while (!ct.IsCancellationRequested)
			{
				var message = await transport.ReceiveTextAsync(ct);
				if (message is null) break;
				if (message.Length == 0) continue;
				await PublishInputAsync(handle, message, ct);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			logger.LogError(ex, "Error pumping connection {Handle}", handle);
		}
		finally
		{
			// Detach (hold the session) instead of disconnecting; the grace timer does the real
			// disconnect if the client does not come back.
			sinkRegistry.Get(handle)?.Detach();
			detachedTracker.Detach(handle, async () =>
			{
				await connectionService.DisconnectAsync(handle);
				descriptorGenerator.ReleaseWebSocketDescriptor(handle);
				sinkRegistry.Remove(handle);
			}, grace);
		}
	}

	/// <summary>Rebind to a live (detached or attached) handle; returns the handle, or null to fall back to fresh.</summary>
	private async Task<long?> TryRebindAsync(IDuplexTransport transport, string token, long lastSeq, CancellationToken ct)
	{
		var (found, oldHandle) = await resumeTokens.TryResolveAsync(token, ct);
		if (!found) return null;

		var sink = sinkRegistry.Get(oldHandle);
		if (sink is null || connectionService.Get(oldHandle) is null)
			return null; // session no longer alive → fresh path (will still durably replay)

		detachedTracker.Reattach(oldHandle);           // cancel any grace timer
		var previous = sink.Current;
		sink.Attach(transport);
		if (previous is not null)
			await previous.CloseAsync();               // connection-steal: last write wins

		await transport.SendAsync(Encoding.UTF8.GetBytes("{\"reattached\":true}"), ct);
		foreach (var f in await replayStore.AfterAsync(oldHandle, lastSeq, ct))
			await transport.SendAsync(f, ct);

		logger.LogInformation("Reattached to session {Handle}", oldHandle);
		return oldHandle;
	}

	private async Task RegisterFreshAsync(IDuplexTransport transport, long handle, CancellationToken ct)
	{
		var sink = sinkRegistry.GetOrCreate(handle);
		sink.Attach(transport);

		Func<byte[], ValueTask> output = async data =>
		{
			var (_, wrapped) = await replayStore.AppendAsync(handle, data, ct);
			var current = sink.Current;
			if (current is not null)
				await current.SendAsync(wrapped, ct);
		};

		await connectionService.RegisterAsync(
			handle, transport.RemoteIp, transport.Hostname, transport.Kind,
			output, output, () => Encoding.UTF8, () => sink.Current?.CloseAsync());

		var token = await resumeTokens.MintAsync(handle, ct);
		await transport.SendAsync(Encoding.UTF8.GetBytes($"{{\"resumeToken\":\"{token}\"}}"), ct);
	}

	private async Task PublishInputAsync(long handle, string message, CancellationToken ct)
	{
		if (WebSocketControlFrame.TryParseNaws(message, out var cols, out var rows))
			await publishEndpoint.Publish(new NAWSUpdateMessage(handle, rows, cols), ct);
		else
			await publishEndpoint.Publish(new WebSocketInputMessage(handle, message), ct);
	}

	private static bool IsHello(string frame) => frame.Contains("\"hello\"");
}
```

- [ ] **Step 2: Update ConnectionPumpTests to the new ctor + behavior**

Replace the `MakePump` helper and the resume test:

```csharp
	private static ConnectionPump MakePump(
		IMessageBus bus,
		IConnectionServerService conn,
		IDescriptorGeneratorService desc,
		ITerminalReplayStore? replay = null,
		IResumeTokenStore? resume = null,
		SessionSinkRegistry? registry = null,
		DetachedSessionTracker? tracker = null)
		=> new(
			NullLogger<ConnectionPump>.Instance, conn, bus, desc,
			replay ?? new TerminalReplayStore(),
			resume ?? new ResumeTokenService(),
			registry ?? new SessionSinkRegistry(),
			tracker ?? new DetachedSessionTracker(new SharpMUSH.Tests.ConnectionServer.TestSchedulers.ManualScheduler()),
			TimeSpan.FromSeconds(120));
```

Add a shared manual scheduler helper (so tests can construct trackers):

```csharp
// SharpMUSH.Tests/ConnectionServer/TestSchedulers.cs
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer.TestSchedulers;

public sealed class ManualScheduler : IGraceScheduler
{
	public Func<Task>? Captured { get; private set; }
	public IDisposable Schedule(TimeSpan delay, Func<Task> action)
	{
		Captured = action;
		return new Noop();
	}
	private sealed class Noop : IDisposable { public void Dispose() { } }
}
```

Update the two basic tests to feed `hello` first (they were sending a command/nothing as the first
frame; the pump now expects a first frame). `Publishes_input_frame_then_disconnects_on_close`:

```csharp
	[Test]
	public async Task Publishes_input_frame_then_detaches_on_close()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		conn.Get(Arg.Any<long>()).Returns((ConnectionServerService.ConnectionData?)null);
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var tracker = new DetachedSessionTracker(new SharpMUSH.Tests.ConnectionServer.TestSchedulers.ManualScheduler());
		var pump = MakePump(bus, conn, desc, tracker: tracker);
		var transport = new FakeTransport("{\"hello\":1}", "look", null);

		await pump.RunAsync(transport, candidateHandle: 42, CancellationToken.None);

		await bus.Received(1).Publish(
			Arg.Is<WebSocketInputMessage>(m => m.Handle == 42 && m.Input == "look"),
			Arg.Any<CancellationToken>());
		// Drop now DETACHES rather than disconnecting immediately.
		await conn.DidNotReceive().DisconnectAsync(42);
		await Assert.That(tracker.IsDetached(42)).IsTrue();
	}
```

Update the resume test to construct a live handle first (register via a fresh connect), drop it,
then reconnect with the token and assert reattach (same handle, replayed frames). Replace the old
`Sequenced_resume_...` test body:

```csharp
	[Test]
	public async Task Reconnect_within_grace_rebinds_to_the_same_handle()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var replay = new TerminalReplayStore();
		var resume = new ResumeTokenService();
		var registry = new SessionSinkRegistry();
		var tracker = new DetachedSessionTracker(new SharpMUSH.Tests.ConnectionServer.TestSchedulers.ManualScheduler());

		// Model a live handle 9 that produced output and is currently detached (socket dropped).
		conn.Get(9).Returns(new ConnectionServerService.ConnectionData(
			9, null, ConnectionServerService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => System.Text.Encoding.UTF8, () => { }, null,
			new SharpMUSH.ConnectionServer.Models.ProtocolCapabilities(), null, "websocket"));
		var sink9 = registry.GetOrCreate(9);
		sink9.Detach();
		await replay.AppendAsync(9, System.Text.Encoding.UTF8.GetBytes("one"));   // seq 1
		await replay.AppendAsync(9, System.Text.Encoding.UTF8.GetBytes("two"));   // seq 2
		var token = await resume.MintAsync(9);

		var pump = MakePump(bus, conn, desc, replay, resume, registry, tracker);
		var transport = new FakeTransport($"{{\"resume\":\"{token}\",\"lastSeq\":1}}", null);

		await pump.RunAsync(transport, candidateHandle: 99, CancellationToken.None);

		// Rebound: sink for 9 now points at the reconnecting transport; a fresh 99 was NOT registered.
		await conn.DidNotReceive().RegisterAsync(
			99, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<Func<byte[], ValueTask>>(), Arg.Any<Func<byte[], ValueTask>>(),
			Arg.Any<Func<System.Text.Encoding>>(), Arg.Any<Action>(),
			Arg.Any<Func<string, string, ValueTask>?>(),
			Arg.Any<SharpMUSH.ConnectionServer.Models.ProtocolCapabilities?>());
		desc.Received(1).ReleaseWebSocketDescriptor(99);
		// Sent = [ {"reattached":true}, replayed seq 2 ]
		var seqs = transport.Sent
			.Where(b => { try { SeqEnvelope.ReadSeq(b); return true; } catch { return false; } })
			.Select(SeqEnvelope.ReadSeq).ToArray();
		await Assert.That(seqs).IsEquivalentTo(new[] { 2L });
	}
```

- [ ] **Step 3: Update DI in Program.cs**

Register the registry, tracker, scheduler, grace, and pass grace into the pump. Replace the
`AddSingleton<ConnectionPump>()` registration:

```csharp
		var graceSeconds = builder.Configuration.GetValue("Session:GraceSeconds", 120.0);
		builder.Services.AddSingleton<SessionSinkRegistry>();
		builder.Services.AddSingleton<IGraceScheduler, TimerGraceScheduler>();
		builder.Services.AddSingleton<DetachedSessionTracker>();
		builder.Services.AddSingleton(sp => new ConnectionPump(
			sp.GetRequiredService<ILogger<ConnectionPump>>(),
			sp.GetRequiredService<IConnectionServerService>(),
			sp.GetRequiredService<IMessageBus>(),
			sp.GetRequiredService<IDescriptorGeneratorService>(),
			sp.GetRequiredService<ITerminalReplayStore>(),
			sp.GetRequiredService<IResumeTokenStore>(),
			sp.GetRequiredService<SessionSinkRegistry>(),
			sp.GetRequiredService<DetachedSessionTracker>(),
			TimeSpan.FromSeconds(graceSeconds)));
```

Add `using SharpMUSH.Messaging.Abstractions;` if `IMessageBus` is not already in scope in Program.cs.

- [ ] **Step 4: Build + run the pump tests**

Run: `dotnet build SharpMUSH.sln -c Debug` → Build succeeded.
Run: `dotnet run --project SharpMUSH.Tests -c Debug -- --treenode-filter "/*/*/ConnectionPumpTests/*"`
Expected: all pump tests pass.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.ConnectionServer/ProtocolHandlers/ConnectionPump.cs SharpMUSH.ConnectionServer/Program.cs SharpMUSH.Tests/ConnectionServer/
git commit -m "feat: detach-on-drop + first-frame rebind in ConnectionPump"
```

---

## Task 4: Client — hello/resume first frame + reattached ack

**Files:**
- Modify: `SharpMUSH.Client/Services/WebSocketClientService.cs`

**Interfaces:**
- Consumes: existing `_resumeToken`, `_lastSeq`, `ResumeFrameParser`.
- Produces: on connect the client sends `{"hello":1}` (fresh) or `{"resume",…}` (reconnect) as the first
  frame; on receiving `{"reattached":true}` it raises a `Reattached` event (so the terminal skips login).

- [ ] **Step 1: Send the mandatory first frame on connect**

In `ConnectInternalAsync`, replace the existing "on reconnect send resume frame" block with a first
frame that is always sent:

```csharp
			// Mandatory first frame: resume on reconnect (we hold a token), else hello.
			var firstFrame = _resumeToken is not null
				? $"{{\"resume\":\"{_resumeToken}\",\"lastSeq\":{_lastSeq}}}"
				: "{\"hello\":1}";
			var firstBytes = Encoding.UTF8.GetBytes(firstFrame);
			await _webSocket.SendAsync(
				new ArraySegment<byte>(firstBytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
```

- [ ] **Step 2: Handle the reattached ack**

Add a public event and handle the frame in `TryHandleControlFrame`:

```csharp
	/// <summary>Raised when the server confirms a rebind to an existing session (skip re-login).</summary>
	public event EventHandler? Reattached;
```

In `TryHandleControlFrame`, before the resume-token check:

```csharp
		if (message.Contains("\"reattached\""))
		{
			Reattached?.Invoke(this, EventArgs.Empty);
			return true; // consumed
		}
```

- [ ] **Step 3: Build**

Run: `dotnet build SharpMUSH.Client/SharpMUSH.Client.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.Client/Services/WebSocketClientService.cs
git commit -m "feat: client hello/resume first frame + reattached ack"
```

---

## Self-Review Notes

- **Spec coverage:** §Components → T1 (sink/registry), T2 (tracker); §Protocol first-frame + rebind →
  T3; §reattached ack + client first frame → T4; §three tiers → T3 (`TryRebindAsync` live vs
  fresh+durable-replay); §grace config → T3 Step 3. Token re-mint is spec'd as a follow-up (not a task).
- **Placeholder scan:** none — all steps carry complete code.
- **Type consistency:** `SessionSink.Current/Attach/Detach`, `SessionSinkRegistry.GetOrCreate/Get/Remove`,
  `DetachedSessionTracker.Detach/Reattach/IsDetached`, `IGraceScheduler.Schedule`, and the new
  `ConnectionPump` ctor signature are used identically across T1–T4. `ConnectionData` positional
  constructor matches `SharpMUSH.ConnectionServer.Services.ConnectionServerService.ConnectionData`.
- **Note for implementer:** the play terminal skipping re-login on `Reattached` is a wiring detail in
  `PlayTerminalService`/the login flow; if the login flow isn't cleanly interceptable, leave the event
  wired and unconsumed (the reattach still works; the client would just re-run a harmless login that the
  server can no-op). Do not block the task on it.
