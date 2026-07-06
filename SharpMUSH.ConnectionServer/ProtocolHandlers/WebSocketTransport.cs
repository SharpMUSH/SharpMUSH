using System.Net.WebSockets;
using System.Text;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>Adapts a <see cref="WebSocket"/> to <see cref="IDuplexTransport"/> using native text framing.</summary>
public sealed class WebSocketTransport(WebSocket socket, string remoteIp, string hostname) : IDuplexTransport
{
	public string Kind => "websocket";
	public string RemoteIp => remoteIp;
	public string Hostname => hostname;

	public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
	{
		if (socket.State == WebSocketState.Open)
			await socket.SendAsync(data, WebSocketMessageType.Text, true, ct);
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
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", ct);
				return null;
			}

			messageBuffer.Write(buffer, 0, result.Count);
		}
		while (!result.EndOfMessage);

		return messageBuffer.Length > 0 ? Encoding.UTF8.GetString(messageBuffer.ToArray()) : string.Empty;
	}

	public Task CloseAsync()
	{
		if (socket.State == WebSocketState.Open)
			_ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
		return Task.CompletedTask;
	}
}
