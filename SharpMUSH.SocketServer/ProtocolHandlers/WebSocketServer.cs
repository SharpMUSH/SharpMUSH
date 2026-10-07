using System.Net;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

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
		// The player's address: behind a proxy listed in ForwardedHeaders:KnownProxies, UseForwardedHeaders
		// has already put the X-Forwarded-For client here (TrustedProxies). It is the host as well, the
		// way a telnet connection's is its peer address; the Host header names this server, not the
		// player, and a sitelock host rule matched against it would hit every web connection at once.
		var remoteIp = ClientAddress(context.Connection.RemoteIpAddress);
		var hostname = remoteIp;

		// Request.IsHttps, so a wss:// connection reports SSL. Behind a TLS-terminating proxy this is
		// only true once the proxy's X-Forwarded-Proto is honoured (the proxy in KnownProxies); without
		// that it under-reports rather than over-reports, which is the right way round for a security
		// claim.
		var transport = new WebSocketTransport(webSocket, remoteIp, hostname, context.Request.IsHttps);
		await pump.RunAsync(transport, handle, context.RequestAborted);
	}

	/// <summary>
	/// The address a connection is recorded under. Kestrel's dual-stack listener reports an IPv4 peer as
	/// <c>::ffff:a.b.c.d</c>, which a sitelock rule naming <c>a.b.c.d</c> would not equal.
	/// </summary>
	internal static string ClientAddress(IPAddress? address)
		=> (address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address)?.ToString() ?? "unknown";
}
