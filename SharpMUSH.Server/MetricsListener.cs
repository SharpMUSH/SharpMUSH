using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace SharpMUSH.Server;

/// <summary>
/// The plain-HTTP port a Prometheus scraper reads <c>/metrics</c> from: <c>Metrics:Port</c>, 9092 unless set,
/// 0 for none. The game's own HTTP listener redirects to HTTPS, so a scrape of it gets a 307 to a port and
/// certificate the scraper does not follow. This port is never redirected and answers <c>/metrics</c> only, and
/// no other port answers <c>/metrics</c>.
/// </summary>
public static class MetricsListener
{
	public const int DefaultPort = 9092;

	public static readonly PathString Path = "/metrics";

	public static int Port(IConfiguration configuration) => configuration.GetValue("Metrics:Port", DefaultPort);

	/// <summary>
	/// The metrics port the host actually listens on: <see cref="Port"/> when <see cref="Listen"/> adds it, else 0,
	/// in which case <c>/metrics</c> stays on the main listener.
	/// </summary>
	public static int ListenedPort(IConfiguration configuration) =>
		Port(configuration) > 0 && !string.IsNullOrWhiteSpace(Urls(configuration)) ? Port(configuration) : 0;

	/// <summary>
	/// Adds the metrics port to the addresses the host already listens on (<c>ASPNETCORE_URLS</c>, or
	/// <c>ASPNETCORE_HTTP_PORTS</c>/<c>ASPNETCORE_HTTPS_PORTS</c>). With none of those set, Kestrel's own default is
	/// left alone and the metrics stay on the main listener.
	/// </summary>
	public static void Listen(IWebHostBuilder webHost, IConfiguration configuration)
	{
		var port = ListenedPort(configuration);
		if (port <= 0)
		{
			return;
		}

		webHost.UseUrls($"{Urls(configuration)};http://*:{port}");
	}

	/// <summary>True when the request came in on the metrics port.</summary>
	public static bool Serves(HttpContext context, int port) => port > 0 && context.Connection.LocalPort == port;

	/// <summary>
	/// True when the request must be answered 404: anything but <c>/metrics</c> on the metrics port, and
	/// <c>/metrics</c> on any other port, which a reverse proxy may publish.
	/// </summary>
	public static bool Refuses(HttpContext context, int port) =>
		port > 0 && (Serves(context, port)
			? context.Request.Path != Path
			: context.Request.Path.StartsWithSegments(Path));

	private static string Urls(IConfiguration configuration)
	{
		var urls = configuration[WebHostDefaults.ServerUrlsKey];
		return string.IsNullOrWhiteSpace(urls)
			? string.Join(';',
				Ports(configuration[WebHostDefaults.HttpPortsKey]).Select(p => $"http://*:{p}")
					.Concat(Ports(configuration[WebHostDefaults.HttpsPortsKey]).Select(p => $"https://*:{p}")))
			: urls;
	}

	private static IEnumerable<string> Ports(string? ports) =>
		(ports ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
