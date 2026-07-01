using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.ClientState;

public class TransportNegotiatorTests
{
	private sealed class StubClient(string kind, bool failConnect) : ITransportClient
	{
		public string Kind => kind;
		public bool IsConnected { get; private set; }
		public event EventHandler<string>? MessageReceived;

		public Task ConnectAsync(string uri)
		{
			_ = MessageReceived; // silence unused-event warning
			if (failConnect) throw new NotSupportedException("no WebTransport");
			IsConnected = true;
			return Task.CompletedTask;
		}

		public Task SendAsync(string message) => Task.CompletedTask;
		public Task DisconnectAsync() => Task.CompletedTask;
		public void ClearSendBuffer() { }
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	[Test]
	public async Task Falls_back_to_WebSocket_when_WebTransport_connect_throws()
	{
		var wt = new StubClient("webtransport", failConnect: true);
		var ws = new StubClient("websocket", failConnect: false);
		var negotiator = new TransportNegotiator(NullLogger<TransportNegotiator>.Instance, () => wt, () => ws);

		var chosen = await negotiator.SelectAsync("wss://h/ws", "https://h:4203/wt");

		await Assert.That(chosen.Kind).IsEqualTo("websocket");
		await Assert.That(chosen.IsConnected).IsTrue();
	}

	[Test]
	public async Task Uses_WebTransport_when_connect_succeeds()
	{
		var wt = new StubClient("webtransport", failConnect: false);
		var ws = new StubClient("websocket", failConnect: false);
		var negotiator = new TransportNegotiator(NullLogger<TransportNegotiator>.Instance, () => wt, () => ws);

		var chosen = await negotiator.SelectAsync("wss://h/ws", "https://h:4203/wt");

		await Assert.That(chosen.Kind).IsEqualTo("webtransport");
	}

	[Test]
	public async Task Skips_WebTransport_when_uri_is_null()
	{
		var wt = new StubClient("webtransport", failConnect: false);
		var ws = new StubClient("websocket", failConnect: false);
		var negotiator = new TransportNegotiator(NullLogger<TransportNegotiator>.Instance, () => wt, () => ws);

		var chosen = await negotiator.SelectAsync("wss://h/ws", wtUri: null);

		await Assert.That(chosen.Kind).IsEqualTo("websocket");
	}
}
