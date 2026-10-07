using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Common;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// A web terminal connection is recorded under the player's own address, so a sitelock or siteban on
/// it holds whether they come in by telnet or the website. Behind a proxy that address comes from the
/// proxy's <c>X-Forwarded-For</c>, which only a listed proxy may set.
/// </summary>
public class WebSocketClientAddressTests
{
	private const string Proxy = "172.29.0.10";
	private const string Player = "203.0.113.7";

	private static async Task<HttpContext> RunAsync(string remote, string? forwardedFor, params (string Key, string Value)[] settings)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
			.Build();
		var options = new ForwardedHeadersOptions();
		TrustedProxies.Configure(options, configuration);

		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
		if (forwardedFor is not null)
		{
			context.Request.Headers["X-Forwarded-For"] = forwardedFor;
			context.Request.Headers["X-Forwarded-Proto"] = "https";
		}

		var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
			Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, Options.Create(options));
		await middleware.Invoke(context);
		return context;
	}

	[Test]
	public async Task ListedProxyHandsOnThePlayersAddress()
	{
		var context = await RunAsync(Proxy, Player, ("ForwardedHeaders:KnownProxies:0", Proxy));

		await Assert.That(WebSocketServer.ClientAddress(context.Connection.RemoteIpAddress)).IsEqualTo(Player);
		await Assert.That(context.Request.IsHttps).IsTrue();
	}

	[Test]
	public async Task ProxyNetworkHandsOnThePlayersAddress()
	{
		var context = await RunAsync(Proxy, Player, ("ForwardedHeaders:KnownNetworks:0", "172.29.0.0/16"));

		await Assert.That(WebSocketServer.ClientAddress(context.Connection.RemoteIpAddress)).IsEqualTo(Player);
	}

	[Test]
	public async Task OnlyTheProxysOwnEntryCounts()
	{
		// A client can send its own X-Forwarded-For; the proxy appends the address it saw, and that last
		// entry is the one taken.
		var context = await RunAsync(Proxy, $"198.51.100.1, {Player}", ("ForwardedHeaders:KnownProxies:0", Proxy));

		await Assert.That(WebSocketServer.ClientAddress(context.Connection.RemoteIpAddress)).IsEqualTo(Player);
	}

	[Test]
	public async Task UnlistedRemoteCannotNameItsOwnAddress()
	{
		var context = await RunAsync("198.51.100.20", Player, ("ForwardedHeaders:KnownProxies:0", Proxy));

		await Assert.That(WebSocketServer.ClientAddress(context.Connection.RemoteIpAddress)).IsEqualTo("198.51.100.20");
		await Assert.That(context.Request.IsHttps).IsFalse();
	}

	[Test]
	public async Task NoProxyConfiguredTrustsNoHeader()
	{
		var context = await RunAsync("198.51.100.20", Player);

		await Assert.That(WebSocketServer.ClientAddress(context.Connection.RemoteIpAddress)).IsEqualTo("198.51.100.20");
	}

	[Test]
	public async Task DualStackPeerIsRecordedAsIPv4()
	{
		var address = WebSocketServer.ClientAddress(IPAddress.Parse("::ffff:" + Player));

		await Assert.That(address).IsEqualTo(Player);
		await Assert.That(SitelockMatcher.Matches(Player, address, address)).IsTrue();
	}
}
