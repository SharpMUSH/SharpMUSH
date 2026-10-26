using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using TelnetNegotiationCore.Interpreters;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

/// <summary>Adapts a <see cref="WebSocket"/> to <see cref="IDuplexTransport"/> using native text framing.</summary>
public sealed class WebSocketTransport(WebSocket socket, string remoteIp, string hostname, bool isSecure = false)
	: IDuplexTransport
{
	/// <summary>
	/// The longest message accepted: the same as a telnet line, so a command typed in the browser may be
	/// as long as one typed in a MUD client. A longer message closes the connection with 1009.
	/// </summary>
	public const int MaxMessageBytes = TelnetInterpreter.DefaultMaxBufferSize;

	private readonly SemaphoreSlim _sendLock = new(1, 1);

	public string Kind => "websocket";
	public string RemoteIp => remoteIp;
	public string Hostname => hostname;
	public bool IsSecure => isSecure;

	public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
	{
		await _sendLock.WaitAsync(ct);
		try
		{
			if (socket.State == WebSocketState.Open)
				await socket.SendAsync(data, WebSocketMessageType.Text, true, ct);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	public async Task<string?> ReceiveTextAsync(CancellationToken ct)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(1024 * 4);

		try
		{
			// A single text message can arrive fragmented across several ReceiveAsync calls.
			// A message that fits one fragment — every command line does — is decoded in place;
			// anything longer is accumulated until EndOfMessage and decoded once, so a fragmented
			// control frame (e.g. NAWS JSON) is never partially parsed and then misrouted as a
			// command, and multi-byte characters are never split mid-frame.
			var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				await CloseAsync(ct);
				return null;
			}

			if (result.EndOfMessage)
			{
				return Encoding.UTF8.GetString(buffer, 0, result.Count);
			}

			using var messageBuffer = new MemoryStream();
			messageBuffer.Write(buffer, 0, result.Count);

			while (!result.EndOfMessage)
			{
				result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					await CloseAsync(ct);
					return null;
				}

				if (messageBuffer.Length + result.Count > MaxMessageBytes)
				{
					await CloseAsync(WebSocketCloseStatus.MessageTooBig, $"A message may be at most {MaxMessageBytes} bytes.", ct);
					return null;
				}

				messageBuffer.Write(buffer, 0, result.Count);
			}

			return Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public Task CloseAsync(CancellationToken ct = default)
		=> CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", ct);

	private async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken ct)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(TimeSpan.FromSeconds(5));
		try { await CloseOutputAsync(status, description, deadline.Token); }
		catch (OperationCanceledException) { socket.Abort(); throw; }
	}

	private async Task CloseOutputAsync(WebSocketCloseStatus status, string description, CancellationToken ct)
	{
		await _sendLock.WaitAsync(ct);
		try
		{
			// The receive loop owns incoming frames, including the peer's close response.
			if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
				await socket.CloseOutputAsync(status, description, ct);
		}
		finally
		{
			_sendLock.Release();
		}
	}
}
