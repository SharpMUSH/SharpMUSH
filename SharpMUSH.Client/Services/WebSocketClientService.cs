using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Service for managing WebSocket connections to SharpMUSH ConnectionServer.
/// Includes automatic reconnection with exponential backoff, keep-alive, and
/// message buffering during disconnected periods so no output is lost when a
/// user moves between cell towers or has a brief connectivity interruption.
/// </summary>
public class WebSocketClientService : IWebSocketClientService
{

	private readonly ILogger<WebSocketClientService> _logger;
	private ClientWebSocket? _webSocket;
	private CancellationTokenSource? _cancellationTokenSource;
	private Task? _receiveTask;
	private string? _serverUri;
	private volatile bool _intentionalDisconnect;

	/// <summary>
	/// When true, this connection opts into server-side output sequencing + reconnect replay:
	/// it connects with <c>?resume=1</c>, unwraps <c>{"seq","data"}</c> envelopes (tracking the
	/// highest seq), stores the server's resume token, and re-sends <c>{"resume","lastSeq"}</c> on
	/// reconnect so missed output is replayed. Off by default; the play terminal enables it.
	/// </summary>
	protected virtual bool ResumeEnabled => false;

	private string? _resumeToken;
	private long _lastSeq;

	/// <summary>Maximum number of messages to buffer while disconnected.</summary>
	private const int MaxBufferedMessages = 500;

	/// <summary>Messages queued for sending while disconnected.</summary>
	private readonly ConcurrentQueue<string> _sendBuffer = new();

	/// <summary>Initial delay between reconnection attempts.</summary>
	private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);

	/// <summary>Maximum delay between reconnection attempts.</summary>
	private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

	public event EventHandler<string>? MessageReceived;
	public event EventHandler<WebSocketState>? ConnectionStateChanged;

	public bool IsConnected => _webSocket?.State == WebSocketState.Open;

	public WebSocketClientService(ILogger<WebSocketClientService> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Connect to the WebSocket server
	/// </summary>
	/// <param name="serverUri">WebSocket server URI (e.g., ws://localhost:4202/ws)</param>
	public async Task ConnectAsync(string serverUri)
	{
		if (_webSocket?.State == WebSocketState.Open)
		{
			_logger.LogWarning("Already connected to WebSocket server");
			return;
		}

		_serverUri = serverUri;
		_intentionalDisconnect = false;

		await ConnectInternalAsync();
	}

	private async Task ConnectInternalAsync()
	{
		if (_serverUri is null) return;

		try
		{
			_webSocket?.Dispose();
			_webSocket = new ClientWebSocket();

			// KeepAliveInterval is not supported in the browser (Blazor WASM)
			if (!OperatingSystem.IsBrowser())
			{
				_webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
			}

			_cancellationTokenSource = new CancellationTokenSource();

			var connectUri = ResumeEnabled
				? (_serverUri.Contains('?') ? $"{_serverUri}&resume=1" : $"{_serverUri}?resume=1")
				: _serverUri;
			_logger.LogInformation("Connecting to WebSocket server: {ServerUri}", connectUri);
			await _webSocket.ConnectAsync(new Uri(connectUri), _cancellationTokenSource.Token);

			ConnectionStateChanged?.Invoke(this, _webSocket.State);
			_logger.LogInformation("Connected to WebSocket server");

			// On a reconnect (we already hold a resume token) ask the server to replay output we missed.
			if (ResumeEnabled && _resumeToken is not null)
			{
				var resumeFrame = Encoding.UTF8.GetBytes(
					$"{{\"resume\":\"{_resumeToken}\",\"lastSeq\":{_lastSeq}}}");
				await _webSocket.SendAsync(
					new ArraySegment<byte>(resumeFrame),
					WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
			}

			await FlushSendBufferAsync();

			_receiveTask = ReceiveMessagesAsync(_cancellationTokenSource.Token);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error connecting to WebSocket server");
			throw;
		}
	}

	/// <summary>
	/// Send a message to the server, buffering it if currently disconnected.
	/// </summary>
	public async Task SendAsync(string message)
	{
		if (_webSocket?.State == WebSocketState.Open)
		{
			try
			{
				var bytes = Encoding.UTF8.GetBytes(message);
				await _webSocket.SendAsync(
					new ArraySegment<byte>(bytes),
					WebSocketMessageType.Text,
					true,
					_cancellationTokenSource?.Token ?? CancellationToken.None);
				return;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (WebSocketException ex)
			{
				_logger.LogWarning(ex, "Failed to send message, buffering for retry");
			}
		}

		if (_sendBuffer.Count < MaxBufferedMessages)
		{
			_sendBuffer.Enqueue(message);
		}
		else
		{
			_logger.LogWarning("Send buffer full ({Max} messages), dropping message", MaxBufferedMessages);
		}
	}

	/// <summary>
	/// Disconnect from the server
	/// </summary>
	public async Task DisconnectAsync()
	{
		_intentionalDisconnect = true;

		if (_webSocket?.State == WebSocketState.Open)
		{
			try
			{
				_cancellationTokenSource?.Cancel();
				await _webSocket.CloseAsync(
					WebSocketCloseStatus.NormalClosure,
					"Client disconnecting",
					CancellationToken.None);

				ConnectionStateChanged?.Invoke(this, _webSocket.State);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error disconnecting from WebSocket server");
			}
		}

		_webSocket?.Dispose();
		_cancellationTokenSource?.Dispose();
		_webSocket = null;
		_cancellationTokenSource = null;
		ClearSendBuffer();
	}

	/// <inheritdoc/>
	public void ClearSendBuffer()
	{
		var count = _sendBuffer.Count;
		_sendBuffer.Clear();
		if (count > 0)
			_logger.LogDebug("Send buffer cleared ({Count} stale messages discarded)", count);
	}

	/// <summary>
	/// Raises <see cref="MessageReceived"/>, first intercepting resume/seq control frames when
	/// <see cref="ResumeEnabled"/>. A resume-token frame is consumed silently; a seq envelope has its
	/// sequence tracked and its inner payload surfaced. Anything else is passed through unchanged, so a
	/// resume-capable client talking to a non-sequencing server still works.
	/// </summary>
	private void SurfaceMessage(string message)
	{
		if (ResumeEnabled && TryHandleControlFrame(message, out var payload))
		{
			if (payload is not null)
				MessageReceived?.Invoke(this, payload);
			return;
		}

		MessageReceived?.Invoke(this, message);
	}

	private bool TryHandleControlFrame(string message, out string? payload)
	{
		payload = null;

		if (ResumeFrameParser.TryReadResumeToken(message, out var token))
		{
			_resumeToken = token;
			return true; // control frame consumed; nothing to surface
		}

		if (ResumeFrameParser.TryReadSeq(message, out var seq, out var data))
		{
			if (seq > _lastSeq)
				_lastSeq = seq;
			payload = data;
			return true;
		}

		return false;
	}

	private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[1024 * 4];
		using var messageBuffer = new MemoryStream();

		try
		{
			while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
			{
				// A single server message (e.g. a serialized markup envelope) can exceed the receive
				// buffer and arrive as several fragments. Accumulate until EndOfMessage and decode the
				// complete UTF-8 payload once, so callers never see a partial (invalid JSON) frame or a
				// multi-byte character split across a chunk boundary.
				WebSocketReceiveResult result;
				messageBuffer.SetLength(0);

				do
				{
					result = await _webSocket.ReceiveAsync(
						new ArraySegment<byte>(buffer),
						cancellationToken);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						_logger.LogInformation("WebSocket server closed the connection");
						ConnectionStateChanged?.Invoke(this, WebSocketState.Closed);
						break;
					}

					messageBuffer.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);

				// Server-initiated close: leave the receive loop so reconnection can run.
				if (result.MessageType == WebSocketMessageType.Close)
					break;

				if (result.MessageType == WebSocketMessageType.Text && messageBuffer.Length > 0)
				{
					var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
					SurfaceMessage(message);
				}
			}
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("WebSocket receive cancelled");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error receiving WebSocket messages");
			ConnectionStateChanged?.Invoke(this, WebSocketState.Aborted);
		}

		// Attempt automatic reconnection if the disconnect was not intentional
		if (!_intentionalDisconnect && _serverUri is not null)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					await ReconnectAsync();
				}
				catch (OperationCanceledException)
				{
					// Expected during intentional disconnect
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Unhandled exception during WebSocket reconnection");
				}
			});
		}
	}

	private async Task ReconnectAsync()
	{
		var delay = InitialReconnectDelay;
		var attempt = 0;

		while (!_intentionalDisconnect)
		{
			attempt++;
			_logger.LogInformation("Attempting reconnection (attempt {Attempt}, delay {Delay}s)...",
				attempt, delay.TotalSeconds);

			await Task.Delay(delay);

			try
			{
				await ConnectInternalAsync();

				if (_webSocket?.State == WebSocketState.Open)
				{
					_logger.LogInformation("Reconnected successfully after {Attempt} attempt(s).", attempt);
					return;
				}
			}
			catch (OperationCanceledException)
			{
				_logger.LogDebug("Reconnection attempt {Attempt} cancelled", attempt);
			}
			catch (WebSocketException ex)
			{
				_logger.LogWarning(ex, "Reconnection attempt {Attempt} failed", attempt);
			}

			delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
		}

		_logger.LogInformation("Reconnection stopped after {Attempts} attempt(s) due to intentional disconnect.", attempt);
	}

	private async Task FlushSendBufferAsync()
	{
		while (_sendBuffer.TryDequeue(out var message))
		{
			if (_webSocket?.State != WebSocketState.Open) break;

			try
			{
				var bytes = Encoding.UTF8.GetBytes(message);
				await _webSocket.SendAsync(
					new ArraySegment<byte>(bytes),
					WebSocketMessageType.Text,
					true,
					_cancellationTokenSource?.Token ?? CancellationToken.None);
			}
			catch (OperationCanceledException)
			{
				if (_sendBuffer.Count < MaxBufferedMessages)
				{
					_sendBuffer.Enqueue(message);
				}
				else
				{
					_logger.LogWarning("Send buffer full, dropping message during flush");
				}
				break;
			}
			catch (WebSocketException ex)
			{
				_logger.LogWarning(ex, "Failed to flush buffered message");
				if (_sendBuffer.Count < MaxBufferedMessages)
				{
					_sendBuffer.Enqueue(message);
				}
				else
				{
					_logger.LogWarning("Send buffer full, dropping message during flush");
				}
				break;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
		GC.SuppressFinalize(this);
	}
}
