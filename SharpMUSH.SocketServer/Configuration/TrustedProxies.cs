using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SharpMUSH.SocketServer.Configuration;

/// <summary>
/// Which hops may say who a web terminal connection really comes from. Behind Caddy or cloudflared the
/// socket's peer is the proxy, so without this every <c>/ws</c> connection carries the proxy's address,
/// and a sitelock or siteban on the player's own address never matches it. The same
/// <c>ForwardedHeaders:KnownProxies</c> / <c>KnownNetworks</c> lists as <c>SharpMUSH.Server</c>'s
/// (<c>WebHostRegistration</c>), so one proxy entry in a compose file means the same on both hosts.
/// </summary>
public static class TrustedProxies
{
	public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
	{
		options.KnownIPNetworks.Clear();
		options.KnownProxies.Clear();
		foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
			if (IPAddress.TryParse(proxy, out var ip))
				options.KnownProxies.Add(ip);
		foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
			if (System.Net.IPNetwork.TryParse(network, out var ipNetwork))
				options.KnownIPNetworks.Add(ipNetwork);

		// ForwardedHeadersMiddleware reads an empty allow-list as "trust every remote", so with no proxy
		// configured the headers are not read at all: a client can never name its own address.
		options.ForwardedHeaders = options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0
			? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
			: ForwardedHeaders.None;
	}
}
