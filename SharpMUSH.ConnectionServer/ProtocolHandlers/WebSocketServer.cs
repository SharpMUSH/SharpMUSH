using System.Net.WebSockets;
using System.Text;
using SharpMUSH.Messaging.Adapters;
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
	private readonly IBus _publishEndpoint;
	private readonly IDescriptorGeneratorService _descriptorGenerator;

	public WebSocketServer(
		ILogger<WebSocketServer> logger,
		IConnectionServerService connectionService,
		IBus publishEndpoint,
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
					
					// Publish user input to MainProcess
					await _publishEndpoint.Publish(
						new WebSocketInputMessage(nextPort, message), ct);
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
}
