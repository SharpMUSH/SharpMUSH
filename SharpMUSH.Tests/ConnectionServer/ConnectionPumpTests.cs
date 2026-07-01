using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

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
		IResumeTokenStore? resume = null)
		=> new(
			NullLogger<ConnectionPump>.Instance,
			conn,
			bus,
			desc,
			replay ?? new TerminalReplayStore(),
			resume ?? new ResumeTokenService());

	[Test]
	public async Task Publishes_input_frame_then_disconnects_on_close()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var pump = MakePump(bus, conn, desc);
		var transport = new FakeTransport("look", null); // one command, then peer close

		await pump.RunAsync(transport, handle: 42, CancellationToken.None);

		await bus.Received(1).Publish(
			Arg.Is<WebSocketInputMessage>(m => m.Handle == 42 && m.Input == "look"),
			Arg.Any<CancellationToken>());
		await conn.Received(1).DisconnectAsync(42);
		desc.Received(1).ReleaseWebSocketDescriptor(42);
	}

	[Test]
	public async Task Registers_connection_with_transport_kind_and_endpoint()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var pump = MakePump(bus, conn, desc);
		var transport = new FakeTransport((string?)null); // immediate close

		await pump.RunAsync(transport, handle: 7, CancellationToken.None);

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
	public async Task Sequenced_resume_replays_missed_frames_and_does_not_treat_resume_as_command()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var replay = new TerminalReplayStore();
		var resume = new ResumeTokenService();

		// A prior connection (handle 9) produced three output frames.
		await replay.AppendAsync(9, System.Text.Encoding.UTF8.GetBytes("one"));   // seq 1
		await replay.AppendAsync(9, System.Text.Encoding.UTF8.GetBytes("two"));   // seq 2
		await replay.AppendAsync(9, System.Text.Encoding.UTF8.GetBytes("three")); // seq 3
		var oldToken = await resume.MintAsync(9);

		var pump = MakePump(bus, conn, desc, replay: replay, resume: resume);
		// New connection (handle 99) opens with a resume frame acking seq 1, then closes.
		var transport = new FakeTransport($"{{\"resume\":\"{oldToken}\",\"lastSeq\":1}}", null);

		await pump.RunAsync(transport, handle: 99, CancellationToken.None);

		// Sent = [ resumeToken control frame for 99, replayed seq 2, replayed seq 3 ]
		var replayed = transport.Sent
			.Where(b =>
			{
				try { SeqEnvelope.ReadSeq(b); return true; } catch { return false; }
			})
			.Select(SeqEnvelope.ReadSeq)
			.ToArray();
		await Assert.That(replayed).IsEquivalentTo(new[] { 2L, 3L });

		// The resume frame must not be published as a game command.
		await bus.DidNotReceive().Publish(Arg.Any<WebSocketInputMessage>(), Arg.Any<CancellationToken>());
	}
}
