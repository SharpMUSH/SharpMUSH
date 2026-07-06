using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Tests.ConnectionServer.TestSchedulers;

namespace SharpMUSH.Tests.ConnectionServer;

public class ConnectionPumpTests
{
	private sealed class FakeTransport(params string?[] frames) : IDuplexTransport
	{
		private readonly Queue<string?> _frames = new(frames);
		public List<byte[]> Sent { get; } = [];
		public bool Closed { get; private set; }
		public string Kind => "fake";
		public string RemoteIp => "1.2.3.4";
		public string Hostname => "host";

		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
		{
			Sent.Add(data.ToArray());
			return Task.CompletedTask;
		}

		public Task<string?> ReceiveTextAsync(CancellationToken ct)
			=> Task.FromResult(_frames.Count > 0 ? _frames.Dequeue() : null);

		public Task CloseAsync()
		{
			Closed = true;
			return Task.CompletedTask;
		}
	}

	// A replay store that honours its cancellation token (like the real JetStream store, whose
	// PublishAsync throws when the token is canceled), so a canceled token in the output path is observable.
	private sealed class CtHonoringReplayStore : ITerminalReplayStore
	{
		public List<byte[]> Appended { get; } = [];
		private long _seq;

		public ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(string session, byte[] rawUtf8, CancellationToken ct = default)
		{
			ct.ThrowIfCancellationRequested();
			var seq = ++_seq;
			var wrapped = SeqEnvelope.Wrap(seq, rawUtf8);
			Appended.Add(wrapped);
			return ValueTask.FromResult((seq, wrapped));
		}

		public ValueTask<IReadOnlyList<byte[]>> AfterAsync(string session, long lastSeq, CancellationToken ct = default)
			=> ValueTask.FromResult<IReadOnlyList<byte[]>>([]);

		public ValueTask DropAsync(string session, CancellationToken ct = default) => ValueTask.CompletedTask;
	}

	private static ConnectionPump MakePump(
		IMessageBus bus,
		IConnectionServerService conn,
		IDescriptorGeneratorService desc,
		ITerminalReplayStore? replay = null,
		IResumeTokenStore? resume = null,
		SessionSinkRegistry? registry = null,
		DetachedSessionTracker? tracker = null)
		=> new(
			NullLogger<ConnectionPump>.Instance,
			conn,
			bus,
			desc,
			replay ?? new TerminalReplayStore(),
			resume ?? new ResumeTokenService(),
			registry ?? new SessionSinkRegistry(),
			tracker ?? new DetachedSessionTracker(new ManualScheduler()),
			TimeSpan.FromSeconds(120));

	[Test]
	public async Task Publishes_input_frame_then_detaches_on_close()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var tracker = new DetachedSessionTracker(new ManualScheduler());
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

	[Test]
	public async Task First_frame_that_merely_contains_hello_is_published_not_swallowed()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var pump = MakePump(bus, conn, desc);
		// A real first command whose text contains the substring "hello" (with quotes) — NOT the
		// {"hello":1} handshake. It must reach the game, not be misread as the hello frame and dropped.
		var transport = new FakeTransport("say \"hello\"", null);

		await pump.RunAsync(transport, candidateHandle: 7, CancellationToken.None);

		await bus.Received(1).Publish(
			Arg.Is<WebSocketInputMessage>(m => m.Handle == 7 && m.Input == "say \"hello\""),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Output_still_buffers_after_the_connection_token_is_canceled()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var replay = new CtHonoringReplayStore();

		Func<byte[], ValueTask>? output = null;
		_ = conn.RegisterAsync(
			Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Do<Func<byte[], ValueTask>>(o => output = o),
			Arg.Any<Func<byte[], ValueTask>>(),
			Arg.Any<Func<System.Text.Encoding>>(), Arg.Any<Action>(),
			Arg.Any<Func<string, string, ValueTask>?>(),
			Arg.Any<SharpMUSH.ConnectionServer.Models.ProtocolCapabilities?>());

		var pump = MakePump(bus, conn, desc, replay);
		var cts = new CancellationTokenSource();
		var transport = new FakeTransport("{\"hello\":1}", null);

		await pump.RunAsync(transport, candidateHandle: 7, cts.Token);

		// The socket dropped: RequestAborted (the connection token) is now canceled. Engine output during
		// the detached grace window must STILL be buffered for replay — the output path must not be
		// coupled to the dropped socket's cancellation token.
		cts.Cancel();
		await output!(System.Text.Encoding.UTF8.GetBytes("engine output"));

		await Assert.That(replay.Appended).IsNotEmpty();
	}

	[Test]
	public async Task Resume_to_dead_replays_the_tokens_session_not_the_reused_handle()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var replay = new TerminalReplayStore();
		var resume = new ResumeTokenService();

		// A now-DEAD session under handle 5 (conn.Get(5) is null → the rebind path fails, resume-to-dead
		// fires). Its buffered output lives under a per-incarnation session id, and its token carries that id.
		const string deadSession = "dead-incarnation";
		await replay.AppendAsync(deadSession, System.Text.Encoding.UTF8.GetBytes("one")); // seq 1
		await replay.AppendAsync(deadSession, System.Text.Encoding.UTF8.GetBytes("two")); // seq 2
		var deadToken = await resume.MintAsync(5, deadSession);

		var pump = MakePump(bus, conn, desc, replay, resume);
		// The client reconnects onto the SAME, now-recycled handle 5 with the dead session's token.
		var transport = new FakeTransport($"{{\"resume\":\"{deadToken}\",\"lastSeq\":1}}", null);

		await pump.RunAsync(transport, candidateHandle: 5, CancellationToken.None);

		// The dead incarnation's post-ack frame is replayed (keyed by the token's session, not the handle).
		var seqs = transport.Sent
			.Where(b => SeqEnvelope.TryReadSeq(b, out _))
			.Select(SeqEnvelope.ReadSeq)
			.ToArray();
		await Assert.That(seqs).IsEquivalentTo(new[] { 2L });
	}

	[Test]
	public async Task Registers_fresh_connection_on_hello()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var pump = MakePump(bus, conn, desc);
		var transport = new FakeTransport("{\"hello\":1}", null);

		await pump.RunAsync(transport, candidateHandle: 7, CancellationToken.None);

		await conn.Received(1).RegisterAsync(
			7, "1.2.3.4", "host", "fake",
			Arg.Any<Func<byte[], ValueTask>>(),
			Arg.Any<Func<byte[], ValueTask>>(),
			Arg.Any<Func<System.Text.Encoding>>(),
			Arg.Any<Action>(),
			Arg.Any<Func<string, string, ValueTask>?>(),
			Arg.Any<SharpMUSH.ConnectionServer.Models.ProtocolCapabilities?>());
	}

	[Test]
	public async Task Reconnect_within_grace_rebinds_to_the_same_handle()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var replay = new TerminalReplayStore();
		var resume = new ResumeTokenService();
		var registry = new SessionSinkRegistry();
		var tracker = new DetachedSessionTracker(new ManualScheduler());

		// Model a live handle 9 that produced output and is currently detached (socket dropped).
		conn.Get(9).Returns(new ConnectionServerService.ConnectionData(
			9, null, ConnectionServerService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => System.Text.Encoding.UTF8, () => { }, null,
			new SharpMUSH.ConnectionServer.Models.ProtocolCapabilities(), null, "websocket"));
		var sink9 = registry.GetOrCreate(9);
		sink9.Detach();
		const string session9 = "live-incarnation-9";
		await replay.AppendAsync(session9, System.Text.Encoding.UTF8.GetBytes("one")); // seq 1
		await replay.AppendAsync(session9, System.Text.Encoding.UTF8.GetBytes("two")); // seq 2
		var token = await resume.MintAsync(9, session9);

		var pump = MakePump(bus, conn, desc, replay, resume, registry, tracker);
		var transport = new FakeTransport($"{{\"resume\":\"{token}\",\"lastSeq\":1}}", null);

		await pump.RunAsync(transport, candidateHandle: 99, CancellationToken.None);

		// Rebound: a fresh handle 99 was NOT registered, and its descriptor was released.
		await conn.DidNotReceive().RegisterAsync(
			99, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<Func<byte[], ValueTask>>(), Arg.Any<Func<byte[], ValueTask>>(),
			Arg.Any<Func<System.Text.Encoding>>(), Arg.Any<Action>(),
			Arg.Any<Func<string, string, ValueTask>?>(),
			Arg.Any<SharpMUSH.ConnectionServer.Models.ProtocolCapabilities?>());
		desc.Received(1).ReleaseWebSocketDescriptor(99);

		// The reconnecting transport received the reattach ack (proves it was attached to session 9
		// before the subsequent drop re-detached it in the finally).
		await Assert.That(transport.Sent.Any(b => System.Text.Encoding.UTF8.GetString(b).Contains("reattached"))).IsTrue();

		// Sent = [ {"reattached":true}, replayed seq 2 ]
		var seqs = transport.Sent
			.Where(b => SeqEnvelope.TryReadSeq(b, out _))
			.Select(SeqEnvelope.ReadSeq)
			.ToArray();
		await Assert.That(seqs).IsEquivalentTo(new[] { 2L });

		// The resume frame is not published as a game command.
		await bus.DidNotReceive().Publish(Arg.Any<WebSocketInputMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Reattach_rotates_the_resume_token()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var replay = new TerminalReplayStore();
		var resume = new ResumeTokenService();
		var registry = new SessionSinkRegistry();

		conn.Get(9).Returns(new ConnectionServerService.ConnectionData(
			9, null, ConnectionServerService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => System.Text.Encoding.UTF8, () => { }, null,
			new SharpMUSH.ConnectionServer.Models.ProtocolCapabilities(), null, "websocket"));
		registry.GetOrCreate(9).Detach();
		var oldToken = await resume.MintAsync(9, "live-incarnation-9");

		var pump = MakePump(bus, conn, desc, replay, resume, registry);
		var transport = new FakeTransport($"{{\"resume\":\"{oldToken}\",\"lastSeq\":0}}", null);

		await pump.RunAsync(transport, candidateHandle: 99, CancellationToken.None);

		// The used token is now spent ...
		var (oldStillValid, _, _) = await resume.TryResolveAsync(oldToken);
		await Assert.That(oldStillValid).IsFalse();

		// ... and a fresh token was issued that resolves to the same handle.
		var newTokenFrame = transport.Sent
			.Select(b => System.Text.Encoding.UTF8.GetString(b))
			.First(s => s.Contains("resumeToken"));
		var newToken = System.Text.Json.JsonDocument.Parse(newTokenFrame).RootElement.GetProperty("resumeToken").GetString()!;
		var (newValid, newHandle, newSession) = await resume.TryResolveAsync(newToken);
		await Assert.That(newValid).IsTrue();
		await Assert.That(newHandle).IsEqualTo(9L);
		// The rotated token stays bound to the SAME incarnation, so a further drop still replays this session.
		await Assert.That(newSession).IsEqualTo("live-incarnation-9");
	}
}
