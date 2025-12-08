namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for querying Prometheus metrics.
/// Provides methods to retrieve and analyze telemetry data stored in Prometheus.
/// </summary>
public interface IPrometheusQueryService
{
	/// <summary>
	/// Gets the slowest functions within a specified time range.
	/// </summary>
	/// <param name="timeRange">Time range for the query (e.g., "5m", "1h", "24h")</param>
	/// <param name="limit">Maximum number of results to return (default: 10)</param>
	/// <returns>List of functions with their average execution times in milliseconds</returns>
	Task<List<(string FunctionName, double AverageDurationMs)>> GetSlowestFunctionsAsync(string timeRange, int limit = 10);

	/// <summary>
	/// Gets the most frequently called commands within a specified time range.
	/// </summary>
	/// <param name="timeRange">Time range for the query (e.g., "5m", "1h", "24h")</param>
	/// <param name="limit">Maximum number of results to return (default: 10)</param>
	/// <returns>List of commands with their call rates per second</returns>
	Task<List<(string CommandName, double CallsPerSecond)>> GetMostCalledCommandsAsync(string timeRange, int limit = 10);

	/// <summary>
	/// Gets the slowest commands within a specified time range.
	/// </summary>
	/// <param name="timeRange">Time range for the query (e.g., "5m", "1h", "24h")</param>
	/// <param name="limit">Maximum number of results to return (default: 10)</param>
	/// <returns>List of commands with their average execution times in milliseconds</returns>
	Task<List<(string CommandName, double AverageDurationMs)>> GetSlowestCommandsAsync(string timeRange, int limit = 10);

	/// <summary>
	/// Gets the most frequently called functions within a specified time range.
	/// </summary>
	/// <param name="timeRange">Time range for the query (e.g., "5m", "1h", "24h")</param>
	/// <param name="limit">Maximum number of results to return (default: 10)</param>
	/// <returns>List of functions with their call rates per second</returns>
	Task<List<(string FunctionName, double CallsPerSecond)>> GetMostCalledFunctionsAsync(string timeRange, int limit = 10);

	/// <summary>
	/// Executes a custom PromQL query against Prometheus.
	/// </summary>
	/// <param name="query">PromQL query string</param>
	/// <returns>Raw query result as a JSON string</returns>
	Task<string> ExecuteQueryAsync(string query);

	/// <summary>
	/// Gets the current health status of services.
	/// </summary>
	/// <returns>Dictionary of service names to health status (1 = healthy, 0 = unhealthy)</returns>
	Task<Dictionary<string, int>> GetHealthStatusAsync();

	/// <summary>
	/// Gets the current connection metrics.
	/// </summary>
	/// <returns>Tuple of (active connections, logged in players)</returns>
	Task<(int ActiveConnections, int LoggedInPlayers)> GetConnectionMetricsAsync();
}
