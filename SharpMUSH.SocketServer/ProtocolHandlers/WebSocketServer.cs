using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Accepts WebSocket terminal connections and runs them through the shared
/// <see cref="ConnectionPump"/> via a <see cref="WebSocketTransport"/> adapter.
/// </summary>
public class WebSocketServer(
	IDescriptorGeneratorService descriptorGenerator,
	ConnectionPump pump,
	KeepAliveOptions keepAlive)
{
	public async Task HandleWebSocketAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		// KeepAliveTimeout makes the server abort the socket when pongs stop arriving, so a half-open
		// peer (abrupt drop) is detected in ~interval+timeout instead of lingering for minutes.
		using var webSocket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
		{
			KeepAliveInterval = keepAlive.WsInterval,
			KeepAliveTimeout = keepAlive.WsTimeout
		});
		var handle = await descriptorGenerator.GetNextWebSocketDescriptorAsync(context.RequestAborted);
		var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		var hostname = context.Request.Headers.Host.ToString();

		// Request.IsHttps, so a wss:// connection reports SSL. Behind a TLS-terminating proxy this is
		// only true once the proxy's X-Forwarded-Proto is honoured (UseForwardedHeaders with the proxy
		// in KnownProxies); without that it under-reports rather than over-reports, which is the right
		// way round for a security claim.
		var transport = new WebSocketTransport(webSocket, remoteIp, hostname, context.Request.IsHttps);
		await pump.RunAsync(transport, handle, context.RequestAborted);
	}
}
