using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Implementation of Prometheus query service.
/// Queries Prometheus HTTP API to retrieve metrics.
/// </summary>
public class PrometheusQueryService : IPrometheusQueryService
{
	private readonly HttpClient _httpClient;
	private readonly ILogger<PrometheusQueryService> _logger;
	private readonly string _prometheusUrl;

	public PrometheusQueryService(
		HttpClient httpClient,
		ILogger<PrometheusQueryService> logger,
		string prometheusUrl)
	{
		_httpClient = httpClient;
		_logger = logger;
		_prometheusUrl = prometheusUrl;
		_logger.LogInformation("PrometheusQueryService initialized with URL: {PrometheusUrl}", _prometheusUrl);
	}

	public async Task<List<(string FunctionName, double AverageDurationMs)>> GetSlowestFunctionsAsync(string timeRange, int limit = 10)
	{
		try
		{
			// PromQL query to get average function duration
			var query = $"topk({limit}, avg by (function_name) (rate(sharpmush_function_invocation_duration_sum[{timeRange}]) / rate(sharpmush_function_invocation_duration_count[{timeRange}])))";

			var result = await ExecuteQueryAsync(query);
			return ParseMetricResults(result, "function_name");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error querying slowest functions");
			return [];
		}
	}

	public async Task<List<(string CommandName, double CallsPerSecond)>> GetMostCalledCommandsAsync(string timeRange, int limit = 10)
	{
		try
		{
			// PromQL query to get command call rate
			var query = $"topk({limit}, rate(sharpmush_command_invocation_duration_count[{timeRange}]))";

			var result = await ExecuteQueryAsync(query);
			return ParseMetricResults(result, "command_name");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error querying most called commands");
			return [];
		}
	}

	public async Task<List<(string CommandName, double AverageDurationMs)>> GetSlowestCommandsAsync(string timeRange, int limit = 10)
	{
		try
		{
			// PromQL query to get average command duration
			var query = $"topk({limit}, avg by (command_name) (rate(sharpmush_command_invocation_duration_sum[{timeRange}]) / rate(sharpmush_command_invocation_duration_count[{timeRange}])))";

			var result = await ExecuteQueryAsync(query);
			return ParseMetricResults(result, "command_name");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error querying slowest commands");
			return [];
		}
	}

	public async Task<List<(string FunctionName, double CallsPerSecond)>> GetMostCalledFunctionsAsync(string timeRange, int limit = 10)
	{
		try
		{
			// PromQL query to get function call rate
			var query = $"topk({limit}, rate(sharpmush_function_invocation_duration_count[{timeRange}]))";

			var result = await ExecuteQueryAsync(query);
			return ParseMetricResults(result, "function_name");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error querying most called functions");
			return [];
		}
	}

	public async Task<string> ExecuteQueryAsync(string query)
	{
		try
		{
			var url = $"{_prometheusUrl}/api/v1/query?query={Uri.EscapeDataString(query)}";
			_logger.LogDebug("Executing Prometheus query: {Query}", query);

			var response = await _httpClient.GetStringAsync(url);
			return response;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error executing Prometheus query: {Query}", query);
			throw;
		}
	}

	public async Task<Dictionary<string, int>> GetHealthStatusAsync()
	{
		try
		{
			var result = new Dictionary<string, int>();

			// Query server health
			var serverHealthResult = await ExecuteQueryAsync("sharpmush_server_health");
			var serverHealth = ParseSingleValueResult(serverHealthResult);
			if (serverHealth.HasValue)
			{
				result["server"] = (int)serverHealth.Value;
			}

			// Query connection server health
			var connServerHealthResult = await ExecuteQueryAsync("sharpmush_connectionserver_health");
			var connServerHealth = ParseSingleValueResult(connServerHealthResult);
			if (connServerHealth.HasValue)
			{
				result["connectionserver"] = (int)connServerHealth.Value;
			}

			return result;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error querying health status");
			return [];
		}
	}

	public async Task<(int ActiveConnections, int LoggedInPlayers)> GetConnectionMetricsAsync()
	{
		try
		{
			// Query active connections
			var activeConnectionsResult = await ExecuteQueryAsync("sharpmush_connections_active");
			var activeConnections = ParseSingleValueResult(activeConnectionsResult) ?? 0;

			// Query logged in players
			var loggedInPlayersResult = await ExecuteQueryAsync("sharpmush_players_logged_in");
			var loggedInPlayers = ParseSingleValueResult(loggedInPlayersResult) ?? 0;

			return ((int)activeConnections, (int)loggedInPlayers);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error querying connection metrics");
			return (0, 0);
		}
	}

	/// <summary>
	/// Parses Prometheus query results into a list of name-value pairs.
	/// </summary>
	private List<(string Name, double Value)> ParseMetricResults(string jsonResult, string labelName)
	{
		try
		{
			var results = new List<(string Name, double Value)>();

			using var doc = JsonDocument.Parse(jsonResult);
			var root = doc.RootElement;

			if (root.TryGetProperty("data", out var data) &&
			    data.TryGetProperty("result", out var resultArray))
			{
				foreach (var item in resultArray.EnumerateArray())
				{
					if (item.TryGetProperty("metric", out var metric) &&
					    metric.TryGetProperty(labelName, out var nameElement))
					{
						var name = nameElement.GetString() ?? "unknown";

						if (item.TryGetProperty("value", out var value) &&
						    value.GetArrayLength() >= 2)
						{
							var valueStr = value[1].GetString();
							if (double.TryParse(valueStr, out var doubleValue))
							{
								results.Add((name, doubleValue));
							}
						}
					}
				}
			}

			return results;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error parsing Prometheus result");
			return [];
		}
	}

	/// <summary>
	/// Parses a single value from Prometheus query result.
	/// </summary>
	private double? ParseSingleValueResult(string jsonResult)
	{
		try
		{
			using var doc = JsonDocument.Parse(jsonResult);
			var root = doc.RootElement;

			if (root.TryGetProperty("data", out var data) &&
			    data.TryGetProperty("result", out var resultArray) &&
			    resultArray.GetArrayLength() > 0)
			{
				var firstResult = resultArray[0];
				if (firstResult.TryGetProperty("value", out var value) &&
				    value.GetArrayLength() >= 2)
				{
					var valueStr = value[1].GetString();
					if (double.TryParse(valueStr, out var doubleValue))
					{
						return doubleValue;
					}
				}
			}

			return null;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error parsing single value result");
			return null;
		}
	}
}
