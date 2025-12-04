namespace SharpMUSH.Server.Strategy.Prometheus;

/// <summary>
/// Base strategy for configuring Prometheus connection.
/// </summary>
public abstract class PrometheusStrategy
{
	/// <summary>
	/// Gets the Prometheus URL for querying metrics.
	/// </summary>
	/// <returns>The Prometheus URL</returns>
	public abstract string GetPrometheusUrl();

	/// <summary>
	/// Performs any initialization required for the Prometheus configuration.
	/// </summary>
	public abstract ValueTask InitializeAsync();

	/// <summary>
	/// Performs cleanup when shutting down.
	/// </summary>
	public abstract ValueTask DisposeAsync();
}
