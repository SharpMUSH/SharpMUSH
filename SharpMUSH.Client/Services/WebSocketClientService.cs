using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

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

			_logger.LogInformation("Connecting to WebSocket server: {ServerUri}", _serverUri);
			await _webSocket.ConnectAsync(new Uri(_serverUri), _cancellationTokenSource.Token);

			ConnectionStateChanged?.Invoke(this, _webSocket.State);
			_logger.LogInformation("Connected to WebSocket server");

			// Flush any messages that were queued while disconnected
			await FlushSendBufferAsync();

			// Start receiving messages
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

		// Buffer the message for later delivery
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
	}

	private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[1024 * 4];

		try
		{
			while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
			{
				var result = await _webSocket.ReceiveAsync(
					new ArraySegment<byte>(buffer),
					cancellationToken);

				if (result.MessageType == WebSocketMessageType.Close)
				{
					_logger.LogInformation("WebSocket server closed the connection");
					ConnectionStateChanged?.Invoke(this, WebSocketState.Closed);
					break;
				}

				if (result.MessageType == WebSocketMessageType.Text)
				{
					var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
					MessageReceived?.Invoke(this, message);
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

			// Exponential backoff capped at MaxReconnectDelay
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
