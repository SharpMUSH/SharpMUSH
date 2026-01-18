using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using System.Text;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// @metrics command - Query Prometheus metrics
	/// Switches:
	///   /slowest - Show slowest functions or commands
	///   /popular - Show most frequently called functions or commands
	///   /query - Execute a custom PromQL query
	///   /health - Show service health status
	///   /connections - Show connection metrics
	/// </summary>
	[SharpCommand(
		Name = "@METRICS",
		Switches = ["SLOWEST", "POPULAR", "QUERY", "HEALTH", "CONNECTIONS", "FUNCTIONS", "COMMANDS"],
		Behavior = CB.Default,
		MinArgs = 0,
		MaxArgs = 2,
		ParameterNames = ["time-range", "limit"])]
	public async ValueTask<Option<CallState>> Metrics(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (_prometheusQueryService == null)
		{
			await _notifyService.Notify(parser.CurrentState.Executor!.Value,
				MModule.single("Prometheus query service is not available."));
			return new None();
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;

		try
		{
			// Default time range is 5 minutes
			var timeRange = args.Count > 0 ? args["0"].Message?.ToString() ?? "5m" : "5m";
			var limit = 10;

			if (args.Count > 1 && int.TryParse(args["1"].Message?.ToString(), out var parsedLimit))
			{
				limit = int.Clamp(parsedLimit, 1, 50);
			}

			// /slowest switch - show slowest functions or commands
			if (switches.Contains("SLOWEST"))
			{
				var targetFunctions = switches.Contains("FUNCTIONS") || !switches.Contains("COMMANDS");
				var targetCommands = switches.Contains("COMMANDS") || !switches.Contains("FUNCTIONS");

				var output = new StringBuilder();
				output.AppendLine("=== Slowest Operations ===");

				if (targetFunctions)
				{
					var slowestFunctions = await _prometheusQueryService.GetSlowestFunctionsAsync(timeRange, limit);
					output.AppendLine($"\nSlowest Functions (over {timeRange}):");
					
					if (slowestFunctions.Count == 0)
					{
						output.AppendLine("  No data available.");
					}
					else
					{
						foreach (var item in slowestFunctions)
						{
							output.AppendLine($"  {item.FunctionName}: {item.AverageDurationMs:F2}ms average");
						}
					}
				}

				if (targetCommands)
				{
					var slowestCommands = await _prometheusQueryService.GetSlowestCommandsAsync(timeRange, limit);
					output.AppendLine($"\nSlowest Commands (over {timeRange}):");
					
					if (slowestCommands.Count == 0)
					{
						output.AppendLine("  No data available.");
					}
					else
					{
						foreach (var item in slowestCommands)
						{
							output.AppendLine($"  {item.CommandName}: {item.AverageDurationMs:F2}ms average");
						}
					}
				}

				await _notifyService.Notify(executor, MModule.single(output.ToString()));
				return new None();
			}

			// /popular switch - show most frequently called functions or commands
			if (switches.Contains("POPULAR"))
			{
				var targetFunctions = switches.Contains("FUNCTIONS") || !switches.Contains("COMMANDS");
				var targetCommands = switches.Contains("COMMANDS") || !switches.Contains("FUNCTIONS");

				var output = new StringBuilder();
				output.AppendLine("=== Most Popular Operations ===");

				if (targetFunctions)
				{
					var popularFunctions = await _prometheusQueryService.GetMostCalledFunctionsAsync(timeRange, limit);
					output.AppendLine($"\nMost Called Functions (over {timeRange}):");
					
					if (popularFunctions.Count == 0)
					{
						output.AppendLine("  No data available.");
					}
					else
					{
						foreach (var item in popularFunctions)
						{
							output.AppendLine($"  {item.FunctionName}: {item.CallsPerSecond:F2} calls/sec");
						}
					}
				}

				if (targetCommands)
				{
					var popularCommands = await _prometheusQueryService.GetMostCalledCommandsAsync(timeRange, limit);
					output.AppendLine($"\nMost Called Commands (over {timeRange}):");
					
					if (popularCommands.Count == 0)
					{
						output.AppendLine("  No data available.");
					}
					else
					{
						foreach (var item in popularCommands)
						{
							output.AppendLine($"  {item.CommandName}: {item.CallsPerSecond:F2} calls/sec");
						}
					}
				}

				await _notifyService.Notify(executor, MModule.single(output.ToString()));
				return new None();
			}

			// /query switch - execute custom PromQL query
			if (switches.Contains("QUERY"))
			{
				if (args.Count == 0)
				{
					await _notifyService.Notify(executor,
						MModule.single("Usage: @metrics/query <promql-query>"));
					return new None();
				}

				var query = args["0"].Message?.ToString() ?? "";
				var result = await _prometheusQueryService.ExecuteQueryAsync(query);

				await _notifyService.Notify(executor, MModule.single($"Query Result:\n{result}"));
				return new None();
			}

			// /health switch - show service health status
			if (switches.Contains("HEALTH"))
			{
				var healthStatus = await _prometheusQueryService.GetHealthStatusAsync();
				var output = new StringBuilder();
				output.AppendLine("=== Service Health Status ===");

				if (healthStatus.Count == 0)
				{
					output.AppendLine("No health data available.");
				}
				else
				{
					foreach (var (service, health) in healthStatus)
					{
						var status = health == 1 ? "HEALTHY" : "UNHEALTHY";
						output.AppendLine($"{service}: {status}");
					}
				}

				await _notifyService.Notify(executor, MModule.single(output.ToString()));
				return new None();
			}

			// /connections switch - show connection metrics
			if (switches.Contains("CONNECTIONS"))
			{
				var (activeConnections, loggedInPlayers) = await _prometheusQueryService.GetConnectionMetricsAsync();
				var output = new StringBuilder();
				output.AppendLine("=== Connection Metrics ===");
				output.AppendLine($"Active Connections: {activeConnections}");
				output.AppendLine($"Logged In Players: {loggedInPlayers}");

				await _notifyService.Notify(executor, MModule.single(output.ToString()));
				return new None();
			}

			// No switches - show usage
			await _notifyService.Notify(executor, MModule.single(@"
Usage: @metrics/<switch> [<time-range>] [<limit>]

Switches:
  /slowest [/functions|/commands] - Show slowest operations
  /popular [/functions|/commands] - Show most frequently called operations
  /query <promql> - Execute a custom PromQL query
  /health - Show service health status
  /connections - Show connection metrics

Time Range Examples: 5m, 1h, 24h, 7d
Default limit: 10 (max: 50)

Examples:
  @metrics/slowest 1h 20           - Show top 20 slowest operations in last hour
  @metrics/popular/functions 5m     - Show most called functions in last 5 minutes
  @metrics/health                   - Show service health
"));

			return new None();
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error executing @metrics command");
			await _notifyService.Notify(executor,
				MModule.single($"Error querying metrics: {ex.Message}"));
			return new None();
		}
	}
}
