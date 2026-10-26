using System.Net.WebSockets;
using SharpMUSH.SocketServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

public class WebSocketTransportMessageLimitTests
{
	[Test]
	public async Task AMessageAtTheLimitIsDelivered()
	{
		using var socket = new FragmentingWebSocket(WebSocketTransport.MaxMessageBytes);
		var transport = new WebSocketTransport(socket, "127.0.0.1", "localhost");

		var message = await transport.ReceiveTextAsync(CancellationToken.None);

		await Assert.That(message!.Length).IsEqualTo(WebSocketTransport.MaxMessageBytes);
		await Assert.That(socket.SentCloseStatus).IsNull();
	}

	[Test]
	public async Task AMessageOverTheLimitClosesWithMessageTooBig()
	{
		using var socket = new FragmentingWebSocket(WebSocketTransport.MaxMessageBytes + 1);
		var transport = new WebSocketTransport(socket, "127.0.0.1", "localhost");

		var message = await transport.ReceiveTextAsync(CancellationToken.None);

		await Assert.That(message).IsNull();
		await Assert.That(socket.SentCloseStatus).IsEqualTo(WebSocketCloseStatus.MessageTooBig);
	}

	[Test]
	public async Task AMessageThatNeverEndsStopsAtTheLimit()
	{
		using var socket = new FragmentingWebSocket(long.MaxValue);
		var transport = new WebSocketTransport(socket, "127.0.0.1", "localhost");

		var message = await transport.ReceiveTextAsync(CancellationToken.None);

		await Assert.That(message).IsNull();
		await Assert.That(socket.SentCloseStatus).IsEqualTo(WebSocketCloseStatus.MessageTooBig);
		await Assert.That(socket.Delivered).IsLessThanOrEqualTo(WebSocketTransport.MaxMessageBytes + 4096L);
	}

	/// <summary>Delivers one text message of <c>length</c> bytes of 'a', as many fragments as the reader's buffer takes.</summary>
	private sealed class FragmentingWebSocket(long length) : WebSocket
	{
		private WebSocketState _state = WebSocketState.Open;
		public long Delivered { get; private set; }
		public WebSocketCloseStatus? SentCloseStatus { get; private set; }
		public override WebSocketState State => _state;
		public override WebSocketCloseStatus? CloseStatus => null;
		public override string? CloseStatusDescription => null;
		public override string? SubProtocol => null;
		public override void Abort() => _state = WebSocketState.Aborted;
		public override void Dispose() { }

		public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("CloseAsync would compete with the receive loop.");

		public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
		{
			SentCloseStatus = closeStatus;
			_state = WebSocketState.CloseSent;
			return Task.CompletedTask;
		}

		public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
		{
			var count = (int)Math.Min(buffer.Count, length - Delivered);
			buffer.AsSpan(0, count).Fill((byte)'a');
			Delivered += count;
			return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, Delivered == length));
		}

		public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
			=> Task.CompletedTask;
	}
}
