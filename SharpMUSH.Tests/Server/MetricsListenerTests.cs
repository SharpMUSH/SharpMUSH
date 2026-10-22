using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The metrics port is added beside the addresses the host already listens on, never in place of them.
/// </summary>
public class MetricsListenerTests
{
	private static string? ListenedUrls(Dictionary<string, string?> settings)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		string? urls = null;
		var webHost = Substitute.For<IWebHostBuilder>();
		webHost.UseSetting(WebHostDefaults.ServerUrlsKey, Arg.Do<string?>(value => urls = value)).Returns(webHost);
		MetricsListener.Listen(webHost, configuration);
		return urls;
	}

	[Test]
	public async Task AppendsToTheConfiguredUrls()
	{
		var urls = ListenedUrls(new() { [WebHostDefaults.ServerUrlsKey] = "http://+:8080;https://+:8081" });

		await Assert.That(urls).IsEqualTo("http://+:8080;https://+:8081;http://*:9092");
	}

	[Test]
	public async Task BuildsTheUrlsFromThePortSettings()
	{
		var urls = ListenedUrls(new()
		{
			[WebHostDefaults.HttpPortsKey] = "8080",
			[WebHostDefaults.HttpsPortsKey] = "8081",
			["Metrics:Port"] = "9100"
		});

		await Assert.That(urls).IsEqualTo("http://*:8080;https://*:8081;http://*:9100");
	}

	[Test]
	public async Task PortZeroLeavesTheUrlsAlone()
	{
		var urls = ListenedUrls(new() { [WebHostDefaults.ServerUrlsKey] = "http://+:8080", ["Metrics:Port"] = "0" });

		await Assert.That(urls).IsNull();
	}

	[Test]
	public async Task NoAddressesLeavesKestrelsDefault()
	{
		var urls = ListenedUrls([]);

		await Assert.That(urls).IsNull();
	}

	private static bool Refuses(int localPort, string path, int metricsPort = 9092)
	{
		var context = new DefaultHttpContext();
		context.Connection.LocalPort = localPort;
		context.Request.Path = path;
		return MetricsListener.Refuses(context, metricsPort);
	}

	[Test]
	[Arguments(9092, "/metrics", false)]
	[Arguments(9092, "/", true)]
	[Arguments(9092, "/api/health", true)]
	[Arguments(8080, "/metrics", true)]
	[Arguments(8080, "/METRICS/", true)]
	[Arguments(8081, "/metrics", true)]
	[Arguments(8080, "/", false)]
	[Arguments(8080, "/metricsboard", false)]
	public async Task ServesTheScrapeOnTheMetricsPortOnly(int localPort, string path, bool refused)
	{
		await Assert.That(Refuses(localPort, path)).IsEqualTo(refused);
	}

	[Test]
	public async Task NoMetricsPortKeepsTheScrapeOnTheMainListener()
	{
		await Assert.That(Refuses(8080, "/metrics", metricsPort: 0)).IsFalse();
	}

	[Test]
	public async Task ListenedPortIsZeroWhenKestrelsDefaultIsLeftAlone()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

		await Assert.That(MetricsListener.ListenedPort(configuration)).IsEqualTo(0);
	}
}
