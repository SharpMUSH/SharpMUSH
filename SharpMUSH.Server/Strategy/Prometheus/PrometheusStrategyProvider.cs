namespace SharpMUSH.Server.Strategy.Prometheus;

/// <summary>
/// Provides the appropriate Prometheus strategy based on environment configuration.
/// </summary>
public static class PrometheusStrategyProvider
{
	public static PrometheusStrategy GetStrategy()
	{
		var prometheusUrl = Environment.GetEnvironmentVariable("PROMETHEUS_URL");
		var prometheusTestUrl = Environment.GetEnvironmentVariable("PROMETHEUS_TEST_URL");

		if (!string.IsNullOrWhiteSpace(prometheusUrl))
		{
			return new PrometheusExternalStrategy(prometheusUrl);
		}
		
		if (!string.IsNullOrWhiteSpace(prometheusTestUrl))
		{
			return new PrometheusExternalStrategy(prometheusTestUrl);
		}

		return new PrometheusTestContainerStrategy();
	}
}
