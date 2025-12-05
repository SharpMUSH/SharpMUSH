namespace SharpMUSH.Server.Strategy.Prometheus;

/// <summary>
/// Strategy for connecting to an external Prometheus instance.
/// Used in production, Kubernetes, or Docker Compose environments.
/// </summary>
public class PrometheusExternalStrategy : PrometheusStrategy
{
	private readonly string _prometheusUrl;

	public PrometheusExternalStrategy(string prometheusUrl)
	{
		_prometheusUrl = prometheusUrl;
	}

	public override string GetPrometheusUrl() => _prometheusUrl;

	public override ValueTask InitializeAsync()
	{
		// No initialization needed for external Prometheus
		return ValueTask.CompletedTask;
	}

	public override ValueTask DisposeAsync()
	{
		// No cleanup needed for external Prometheus
		return ValueTask.CompletedTask;
	}
}
