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
		await replay.AppendAsync(9, System.Text.Encoding.UTF8.GetBytes("one")); // seq 1
		await replay.AppendAsync(9, System.Text.Encoding.UTF8.GetBytes("two")); // seq 2
		var token = await resume.MintAsync(9);

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
			.Where(b => { try { SeqEnvelope.ReadSeq(b); return true; } catch { return false; } })
			.Select(SeqEnvelope.ReadSeq)
			.ToArray();
		await Assert.That(seqs).IsEquivalentTo(new[] { 2L });

		// The resume frame is not published as a game command.
		await bus.DidNotReceive().Publish(Arg.Any<WebSocketInputMessage>(), Arg.Any<CancellationToken>());
	}
}
