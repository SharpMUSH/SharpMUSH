using System.Net.WebSockets;
using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

public class WebSocketTransportSerializationTests
{
	[Test]
	public async Task SendsWaitForTheUnderlyingWriteToComplete()
	{
		using var socket = new ControlledWebSocket();
		var transport = new WebSocketTransport(socket, "127.0.0.1", "localhost");
		var first = transport.SendAsync(new byte[] { 1 }, CancellationToken.None);
		await socket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var second = transport.SendAsync(new byte[] { 2 }, CancellationToken.None);
		await Assert.That(socket.SendCount).IsEqualTo(1);
		await Assert.That(second.IsCompleted).IsFalse();

		socket.AllowSend.TrySetResult();
		await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(socket.SendCount).IsEqualTo(2);
		await Assert.That(socket.MaximumConcurrentWrites).IsEqualTo(1);
	}

	[Test]
	public async Task CloseWaitsForSendAndItsOwnUnderlyingWrite()
	{
		using var socket = new ControlledWebSocket();
		var transport = new WebSocketTransport(socket, "127.0.0.1", "localhost");
		var send = transport.SendAsync(new byte[] { 1 }, CancellationToken.None);
		await socket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var close = transport.CloseAsync();
		await Assert.That(socket.CloseStarted.Task.IsCompleted).IsFalse();
		socket.AllowSend.TrySetResult();
		await socket.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(close.IsCompleted).IsFalse();
		socket.AllowClose.TrySetResult();
		await Task.WhenAll(send, close).WaitAsync(TimeSpan.FromSeconds(5));
		await transport.SendAsync(new byte[] { 2 }, CancellationToken.None);
		await Assert.That(socket.SendCount).IsEqualTo(1);
		await Assert.That(socket.MaximumConcurrentWrites).IsEqualTo(1);
	}

	[Test]
	public async Task PeerCloseAcknowledgementIsSerializedWithAnActiveSend()
	{
		using var socket = new ControlledWebSocket();
		var transport = new WebSocketTransport(socket, "127.0.0.1", "localhost");
		var send = transport.SendAsync(new byte[] { 1 }, CancellationToken.None);
		await socket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var receive = transport.ReceiveTextAsync(CancellationToken.None);
		await Assert.That(socket.CloseStarted.Task.IsCompleted).IsFalse();
		socket.AllowSend.TrySetResult();
		await socket.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		socket.AllowClose.TrySetResult();
		await send.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(await receive.WaitAsync(TimeSpan.FromSeconds(5))).IsNull();
		await Assert.That(socket.MaximumConcurrentWrites).IsEqualTo(1);
		await Assert.That(socket.State).IsEqualTo(WebSocketState.Closed);
	}

	private sealed class ControlledWebSocket : WebSocket
	{
		public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource AllowSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource AllowClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private WebSocketState _state = WebSocketState.Open;
		private int _activeWrites;
		public int SendCount { get; private set; }
		public int MaximumConcurrentWrites { get; private set; }
		public override WebSocketState State => _state;
		public override WebSocketCloseStatus? CloseStatus => null;
		public override string? CloseStatusDescription => null;
		public override string? SubProtocol => null;
		public override void Abort() => _state = WebSocketState.Aborted;
		public override void Dispose() { }
		public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("CloseAsync would compete with the receive loop.");

		public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
		{
			MaximumConcurrentWrites = Math.Max(MaximumConcurrentWrites, Interlocked.Increment(ref _activeWrites));
			CloseStarted.TrySetResult();
			try
			{
				await AllowClose.Task.WaitAsync(cancellationToken);
				_state = _state == WebSocketState.CloseReceived ? WebSocketState.Closed : WebSocketState.CloseSent;
			}
			finally { Interlocked.Decrement(ref _activeWrites); }
		}

		public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
		{
			_state = WebSocketState.CloseReceived;
			return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
		}

		public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
		{
			MaximumConcurrentWrites = Math.Max(MaximumConcurrentWrites, Interlocked.Increment(ref _activeWrites));
			SendCount++;
			SendStarted.TrySetResult();
			try { await AllowSend.Task.WaitAsync(cancellationToken); }
			finally { Interlocked.Decrement(ref _activeWrites); }
		}
	}
}
