using Microsoft.AspNetCore.Hosting;
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
}
