using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Service for managing WebSocket connections to SharpMUSH ConnectionServer
/// </summary>
public class WebSocketClientService : IWebSocketClientService
{
	private readonly ILogger<WebSocketClientService> _logger;
	private ClientWebSocket? _webSocket;
	private CancellationTokenSource? _cancellationTokenSource;
	private Task? _receiveTask;

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

		try
		{
			_webSocket = new ClientWebSocket();
			_cancellationTokenSource = new CancellationTokenSource();

			_logger.LogInformation("Connecting to WebSocket server: {ServerUri}", serverUri);
			await _webSocket.ConnectAsync(new Uri(serverUri), _cancellationTokenSource.Token);
			
			ConnectionStateChanged?.Invoke(this, _webSocket.State);
			_logger.LogInformation("Connected to WebSocket server");

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
	/// Send a message to the server
	/// </summary>
	public async Task SendAsync(string message)
	{
		if (_webSocket?.State != WebSocketState.Open)
		{
			throw new InvalidOperationException("WebSocket is not connected");
		}

		try
		{
			var bytes = Encoding.UTF8.GetBytes(message);
			await _webSocket.SendAsync(
				new ArraySegment<byte>(bytes),
				WebSocketMessageType.Text,
				true,
				_cancellationTokenSource?.Token ?? CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error sending message to WebSocket server");
			throw;
		}
	}

	/// <summary>
	/// Disconnect from the server
	/// </summary>
	public async Task DisconnectAsync()
	{
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
	}

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
		GC.SuppressFinalize(this);
	}
}
