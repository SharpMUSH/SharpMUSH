using System.Globalization;
using System.Net.WebSockets;
using System.Text;

namespace SharpMUSH.AnalysisProtocol;

/// <summary>Bounded UTF-8 LSP framing. The custom WS transport carries one JSON body per text message.</summary>
public static class LspFrames
{
	public const string Subprotocol = "sharpmush-lsp-v1";
	public const int DefaultMaximumBytes = 32 * 1024 * 1024;
	public const int MaximumHeaderBytes = 1024;

	public static async Task<byte[]?> ReadStdioAsync(Stream input, int maximumBytes, CancellationToken cancellationToken = default)
	{
		if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
		var header = new byte[MaximumHeaderBytes];
		var count = 0;
		var single = new byte[1];
		while (true)
		{
			var read = await input.ReadAsync(single, cancellationToken);
			if (read == 0)
			{
				if (count == 0) return null;
				throw new EndOfStreamException("Incomplete LSP header.");
			}
			if (count == header.Length || single[0] > 127) throw new InvalidDataException("Invalid or oversized LSP header.");
			header[count++] = single[0];
			if (count >= 4 && header[count - 4] == '\r' && header[count - 3] == '\n' && header[count - 2] == '\r' && header[count - 1] == '\n') break;
		}
		int? length = null;
		foreach (var line in Encoding.ASCII.GetString(header, 0, count - 4).Split("\r\n"))
		{
			var colon = line.IndexOf(':');
			if (colon <= 0) throw new InvalidDataException("Malformed LSP header.");
			if (!line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
			if (length is not null || !int.TryParse(line[(colon + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0 || value > maximumBytes)
				throw new InvalidDataException("Invalid or oversized LSP length.");
			length = value;
		}
		if (length is null) throw new InvalidDataException("Missing LSP length.");
		var message = new byte[length.Value];
		await input.ReadExactlyAsync(message, cancellationToken);
		return message;
	}

	public static async Task WriteStdioAsync(Stream output, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
	{
		await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {message.Length}\r\n\r\n"), cancellationToken);
		await output.WriteAsync(message, cancellationToken);
		await output.FlushAsync(cancellationToken);
	}

	public static async Task<byte[]?> ReadWebSocketAsync(WebSocket socket, int maximumBytes, CancellationToken cancellationToken = default,
		Func<CancellationToken, Task<IDisposable>>? continuationAdmission = null, TimeSpan? continuationTimeout = null)
	{
		if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
		using var message = new MemoryStream();
		var buffer = new byte[Math.Min(8192, maximumBytes)];
		IDisposable? admission = null;
		CancellationTokenSource? deadline = null;
		try
		{
			while (true)
			{
				// Waiting for the first small fragment is session-idle time, not message-body time.
				var frame = await socket.ReceiveAsync(buffer.AsMemory(), deadline?.Token ?? cancellationToken);
				if (frame.MessageType == WebSocketMessageType.Close) return null;
				if (frame.MessageType != WebSocketMessageType.Text || message.Length + frame.Count > maximumBytes)
					throw new InvalidDataException("Invalid or oversized LSP WebSocket message.");
				message.Write(buffer, 0, frame.Count);
				if (frame.EndOfMessage) return message.ToArray();
				if (deadline is not null) continue;
				deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				if (continuationTimeout is { } timeout) deadline.CancelAfter(timeout);
				if (continuationAdmission is not null) admission = await continuationAdmission(deadline.Token);
			}
		}
		finally { admission?.Dispose(); deadline?.Dispose(); }
	}
}
