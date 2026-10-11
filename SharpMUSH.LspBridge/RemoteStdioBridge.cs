using System.Net.WebSockets;
using System.Text.Json;
using SharpMUSH.AnalysisProtocol;

namespace SharpMUSH.LspBridge;

/// <summary>Converts normal LSP byte framing to the versioned remote WS transport.</summary>
public static class RemoteStdioBridge
{
	public static Uri ValidateEndpoint(string value)
	{
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || string.IsNullOrEmpty(uri.Host) ||
			uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
			throw new ArgumentException("A WSS endpoint without userinfo, query or fragment is required.", nameof(value));
		return uri;
	}

	public static async Task SendAsync(WebSocket socket, Stream input, CancellationToken cancellationToken = default)
	{
		while (await LspFrames.ReadStdioAsync(input, LspFrames.DefaultMaximumBytes, cancellationToken) is { } bytes)
		{
			ValidateMessage(bytes);
			await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
		}
	}

	public static async Task<bool> ReceiveOneAsync(WebSocket socket, Stream output, CancellationToken cancellationToken = default)
	{
		var bytes = await LspFrames.ReadWebSocketAsync(socket, LspFrames.DefaultMaximumBytes, cancellationToken);
		if (bytes is null) return false;
		ValidateMessage(bytes);
		await LspFrames.WriteStdioAsync(output, bytes, cancellationToken);
		return true;
	}

	public static async Task RunAsync(WebSocket socket, Stream input, Stream output, CancellationToken cancellationToken = default)
	{
		using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var send = SendAsync(socket, input, stopping.Token);
		async Task Receive() { while (await ReceiveOneAsync(socket, output, stopping.Token)) { } }
		var receive = Receive();
		var completed = await Task.WhenAny(send, receive);
		try { await completed; }
		finally
		{
			stopping.Cancel();
			socket.Abort();
			// Console stdin can retain an uncancellable native read. Process shutdown must not wait for it.
			// Observe both pumps if they finish later, without retaining shutdown or exposing exceptions.
			_ = Task.WhenAll(send, receive).ContinueWith(task => { _ = task.Exception; },
				CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		}
	}

	private static void ValidateMessage(byte[] bytes)
	{
		using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
		if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("One JSON-RPC object per message is required.");
	}
}
