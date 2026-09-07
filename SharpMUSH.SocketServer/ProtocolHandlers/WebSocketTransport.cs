using System.Net.WebSockets;
using System.Text;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>Adapts a <see cref="WebSocket"/> to <see cref="IDuplexTransport"/> using native text framing.</summary>
public sealed class WebSocketTransport(WebSocket socket, string remoteIp, string hostname, bool isSecure = false)
	: IDuplexTransport
{
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
		var buffer = new byte[1024 * 4];
		using var messageBuffer = new MemoryStream();

		// A single text message can arrive fragmented across several ReceiveAsync calls.
		// Accumulate until EndOfMessage and decode the complete UTF-8 payload once, so a
		// fragmented control frame (e.g. NAWS JSON) is never partially parsed and then
		// misrouted as a command, and multi-byte characters are never split mid-frame.
		WebSocketReceiveResult result;
		do
		{
			result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

			if (result.MessageType == WebSocketMessageType.Close)
			{
				await CloseOutputAsync(ct);
				return null;
			}

			messageBuffer.Write(buffer, 0, result.Count);
		}
		while (!result.EndOfMessage);

		return messageBuffer.Length > 0 ? Encoding.UTF8.GetString(messageBuffer.ToArray()) : string.Empty;
	}

	public Task CloseAsync() => CloseOutputAsync(CancellationToken.None);

	private async Task CloseOutputAsync(CancellationToken ct)
	{
		await _sendLock.WaitAsync(ct);
		try
		{
			// The receive loop owns incoming frames, including the peer's close response.
			if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", ct);
		}
		finally
		{
			_sendLock.Release();
		}
	}
}
