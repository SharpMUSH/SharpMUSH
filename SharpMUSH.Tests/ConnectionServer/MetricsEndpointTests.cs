using Microsoft.AspNetCore.Http;
using SharpMUSH.SocketServer;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The connection server answers <c>/metrics</c> on its metrics port only, and nowhere when that port is 0.
/// </summary>
public class MetricsEndpointTests
{
	private static bool Refuses(int localPort, string path, int metricsPort)
	{
		var context = new DefaultHttpContext();
		context.Connection.LocalPort = localPort;
		context.Request.Path = path;
		return MetricsEndpoint.Refuses(context, metricsPort);
	}

	[Test]
	[Arguments(9091, "/metrics", false)]
	[Arguments(9091, "/", true)]
	[Arguments(9091, "/ws", true)]
	[Arguments(4202, "/metrics", true)]
	[Arguments(4202, "/METRICS/", true)]
	[Arguments(4202, "/ws", false)]
	[Arguments(4202, "/metricsboard", false)]
	public async Task ServesTheScrapeOnTheMetricsPortOnly(int localPort, string path, bool refused)
	{
		await Assert.That(Refuses(localPort, path, metricsPort: 9091)).IsEqualTo(refused);
	}

	[Test]
	[Arguments(4202, "/metrics", true)]
	[Arguments(4202, "/ws", false)]
	[Arguments(4202, "/", false)]
	public async Task PortZeroServesTheScrapeNowhere(int localPort, string path, bool refused)
	{
		await Assert.That(Refuses(localPort, path, metricsPort: 0)).IsEqualTo(refused);
	}
}
