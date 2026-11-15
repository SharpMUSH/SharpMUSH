using Microsoft.AspNetCore.Http;

namespace SharpMUSH.SocketServer;

/// <summary>
/// Where the connection server answers <c>/metrics</c>: on <see cref="Configuration.ConnectionServerOptions.MetricsPort"/>
/// only, and nowhere when that port is 0.
/// </summary>
public static class MetricsEndpoint
{
	public static readonly PathString Path = "/metrics";

	/// <summary>
	/// True when the request must be answered 404: anything but <c>/metrics</c> on the metrics port, and
	/// <c>/metrics</c> on any other port, which a reverse proxy may publish. With no metrics port, that is every port.
	/// </summary>
	public static bool Refuses(HttpContext context, int metricsPort) =>
		metricsPort > 0 && context.Connection.LocalPort == metricsPort
			? context.Request.Path != Path
			: context.Request.Path.StartsWithSegments(Path);
}
