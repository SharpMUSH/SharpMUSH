using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SharpMUSH.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Handles WebSocket protocol connections and publishes messages to the message queue
/// </summary>
public class WebSocketServer
{
	private readonly ILogger<WebSocketServer> _logger;
	private readonly IConnectionServerService _connectionService;
	private readonly IMessageBus _publishEndpoint;
	private readonly IDescriptorGeneratorService _descriptorGenerator;

	public WebSocketServer(
		ILogger<WebSocketServer> logger,
		IConnectionServerService connectionService,
		IMessageBus publishEndpoint,
		IDescriptorGeneratorService descriptorGenerator)
	{
		_logger = logger;
		_connectionService = connectionService;
		_publishEndpoint = publishEndpoint;
		_descriptorGenerator = descriptorGenerator;
	}

	public async Task HandleWebSocketAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		var webSocket = await context.WebSockets.AcceptWebSocketAsync();
		var nextPort = _descriptorGenerator.GetNextWebSocketDescriptor();
		var ct = context.RequestAborted;

		var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		var hostname = context.Request.Headers.Host.ToString();

		// Register connection in ConnectionService
		await _connectionService.RegisterAsync(
			nextPort,
			remoteIp,
			hostname,
			"websocket",
			async data =>
			{
				if (webSocket.State == WebSocketState.Open)
				{
					await webSocket.SendAsync(
						new ArraySegment<byte>(data),
						WebSocketMessageType.Text,
						true,
						ct);
				}
			},
			async data =>
			{
				if (webSocket.State == WebSocketState.Open)
				{
					await webSocket.SendAsync(
						new ArraySegment<byte>(data),
						WebSocketMessageType.Text,
						true,
						ct);
				}
			},
			() => Encoding.UTF8,
			() =>
			{
				if (webSocket.State == WebSocketState.Open)
				{
					_ = webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
				}
			},
			async (module, message) =>
			{
				// Send GMCP message via WebSocket as JSON
				if (webSocket.State == WebSocketState.Open)
				{
					var gmcpMessage = new
					{
						type = "gmcp",
						package = module,
						data = message
					};
					var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gmcpMessage));
					await webSocket.SendAsync(
						new ArraySegment<byte>(jsonBytes),
						WebSocketMessageType.Text,
						true,
						ct);
				}
			});

		try
		{
			var buffer = new byte[1024 * 4];
			var receiveBuffer = new ArraySegment<byte>(buffer);

			while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
			{
				var result = await webSocket.ReceiveAsync(receiveBuffer, ct);

				if (result.MessageType == WebSocketMessageType.Close)
				{
					await webSocket.CloseAsync(
						WebSocketCloseStatus.NormalClosure,
						"Connection closed",
						ct);
					break;
				}

				if (result.MessageType == WebSocketMessageType.Text)
				{
					var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
					
					// Try to parse as GMCP JSON message
					if (TryParseGMCPMessage(message, out var package, out var data))
					{
						_logger.LogDebug("Received GMCP message from WebSocket {Handle}: {Package}", nextPort, package);
						
						// Publish GMCP signal to MainProcess
						await _publishEndpoint.Publish(
							new GMCPSignalMessage(nextPort, package, data), ct);
					}
					else
					{
						// Regular text input - publish to MainProcess
						await _publishEndpoint.Publish(
							new WebSocketInputMessage(nextPort, message), ct);
					}
				}
			}
		}
		catch (WebSocketException ex)
		{
			_logger.LogDebug(ex, "WebSocket connection {Handle} disconnected", nextPort);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error handling WebSocket connection {Handle}", nextPort);
		}
		finally
		{
			// Disconnect and notify MainProcess
			await _connectionService.DisconnectAsync(nextPort);
		}
	}

	/// <summary>
	/// Tries to parse a WebSocket message as a GMCP JSON message
	/// </summary>
	/// <param name="message">The raw message string</param>
	/// <param name="package">The GMCP package name</param>
	/// <param name="data">The GMCP data as JSON string</param>
	/// <returns>True if the message is a valid GMCP message</returns>
	private bool TryParseGMCPMessage(string message, out string package, out string data)
	{
		package = string.Empty;
		data = string.Empty;

		try
		{
			using var doc = JsonDocument.Parse(message);
			var root = doc.RootElement;

			// Check if it's a GMCP message
			if (!root.TryGetProperty("type", out var typeElement) || 
			    typeElement.GetString() != "gmcp")
			{
				return false;
			}

			// Get package name
			if (!root.TryGetProperty("package", out var packageElement))
			{
				return false;
			}
			package = packageElement.GetString() ?? string.Empty;

			// Get data (optional)
			if (root.TryGetProperty("data", out var dataElement))
			{
				data = dataElement.GetRawText();
			}

			return !string.IsNullOrEmpty(package);
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
