using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace SharpMUSH.Server;

/// <summary>
/// The plain-HTTP port a Prometheus scraper reads <c>/metrics</c> from: <c>Metrics:Port</c>, 9092 unless set,
/// 0 for none. The game's own HTTP listener redirects to HTTPS, so a scrape of it gets a 307 to a port and
/// certificate the scraper does not follow. This port is never redirected and answers <c>/metrics</c> only.
/// </summary>
public static class MetricsListener
{
	public const int DefaultPort = 9092;

	public static readonly PathString Path = "/metrics";

	public static int Port(IConfiguration configuration) => configuration.GetValue("Metrics:Port", DefaultPort);

	/// <summary>
	/// Adds the metrics port to the addresses the host already listens on (<c>ASPNETCORE_URLS</c>, or
	/// <c>ASPNETCORE_HTTP_PORTS</c>/<c>ASPNETCORE_HTTPS_PORTS</c>). With none of those set, Kestrel's own default is
	/// left alone and the metrics stay on the main listener.
	/// </summary>
	public static void Listen(IWebHostBuilder webHost, IConfiguration configuration)
	{
		var port = Port(configuration);
		if (port <= 0)
		{
			return;
		}

		var urls = configuration[WebHostDefaults.ServerUrlsKey];
		if (string.IsNullOrWhiteSpace(urls))
		{
			urls = string.Join(';',
				Ports(configuration[WebHostDefaults.HttpPortsKey]).Select(p => $"http://*:{p}")
					.Concat(Ports(configuration[WebHostDefaults.HttpsPortsKey]).Select(p => $"https://*:{p}")));
		}

		if (string.IsNullOrWhiteSpace(urls))
		{
			return;
		}

		webHost.UseUrls($"{urls};http://*:{port}");
	}

	/// <summary>True when the request came in on the metrics port.</summary>
	public static bool Serves(HttpContext context, int port) => port > 0 && context.Connection.LocalPort == port;

	private static IEnumerable<string> Ports(string? ports) =>
		(ports ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
