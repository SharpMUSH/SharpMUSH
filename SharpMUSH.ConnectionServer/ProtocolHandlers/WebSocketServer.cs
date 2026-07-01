using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Accepts WebSocket terminal connections and runs them through the shared
/// <see cref="ConnectionPump"/> via a <see cref="WebSocketTransport"/> adapter.
/// </summary>
public class WebSocketServer(
	IDescriptorGeneratorService descriptorGenerator,
	ConnectionPump pump)
{
	public async Task HandleWebSocketAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		var webSocket = await context.WebSockets.AcceptWebSocketAsync();
		var handle = descriptorGenerator.GetNextWebSocketDescriptor();
		var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		var hostname = context.Request.Headers.Host.ToString();

		var transport = new WebSocketTransport(webSocket, remoteIp, hostname);
		await pump.RunAsync(transport, handle, context.RequestAborted);
	}
}
