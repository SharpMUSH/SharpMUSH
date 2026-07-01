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

	[Test]
	public async Task Publishes_input_frame_then_disconnects_on_close()
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, conn, bus, desc);
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
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, conn, bus, desc);
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
}
