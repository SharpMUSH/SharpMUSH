namespace SharpMUSH.Server.Strategy.Prometheus;

/// <summary>
/// Provides the appropriate Prometheus strategy based on environment configuration.
/// </summary>
public static class PrometheusStrategyProvider
{
	public static PrometheusStrategy GetStrategy()
	{
		var prometheusUrl = Environment.GetEnvironmentVariable("PROMETHEUS_URL");

		if (string.IsNullOrWhiteSpace(prometheusUrl))
		{
			// No Prometheus URL configured, use TestContainer for local development
			return new PrometheusTestContainerStrategy();
		}
		else
		{
			// Prometheus URL is configured (e.g., in Kubernetes or Docker Compose)
			return new PrometheusExternalStrategy(prometheusUrl);
		}
	}
}
