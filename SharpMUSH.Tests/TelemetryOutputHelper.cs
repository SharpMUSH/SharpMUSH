using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Helper class for outputting telemetry summaries.
/// </summary>
/// <remarks>
/// Telemetry output is disabled by default during test disposal to reduce noise.
/// It can be enabled by setting the environment variable SHARPMUSH_ENABLE_TEST_TELEMETRY=true or 
/// SHARPMUSH_ENABLE_TEST_TELEMETRY=1.
/// 
/// Console logging during tests is also disabled by default. Logs are sent to Prometheus instead.
/// Console logging can be enabled by setting SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=true or 
/// SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=1.
/// </remarks>
public static class TelemetryOutputHelper
{
	/// <summary>
	/// Outputs a telemetry summary to Console.Error using the provided Prometheus service.
	/// </summary>
	/// <param name="prometheusService">The Prometheus query service to use for retrieving metrics.</param>
	/// <param name="outputStream">The output stream to write to (defaults to Console.Error).</param>
	/// <remarks>
	/// Uses synchronous WriteLine calls instead of async WriteLineAsync to ensure output is not buffered
	/// and appears immediately in test runners like TUnit that capture console output.
	/// </remarks>
	public static async Task OutputTelemetrySummaryAsync(IPrometheusQueryService prometheusService, TextWriter? outputStream = null)
	{
		outputStream ??= Console.Error;

		try
		{
			outputStream.WriteLine();
			outputStream.WriteLine("═══════════════════════════════════════════════════════════════");
			outputStream.WriteLine("                  TEST SESSION TELEMETRY SUMMARY");
			outputStream.WriteLine("═══════════════════════════════════════════════════════════════");
			outputStream.WriteLine();

			// Get health status
			var healthStatus = await prometheusService.GetHealthStatusAsync();
			if (healthStatus.Count > 0)
			{
				outputStream.WriteLine("┌─ Health Status");
				foreach (var (service, status) in healthStatus)
				{
					var statusText = status == 1 ? "✓ Healthy" : "✗ Unhealthy";
					outputStream.WriteLine($"│  {service,-20}: {statusText}");
				}
				outputStream.WriteLine();
			}

			// Get connection metrics
			var (activeConnections, loggedInPlayers) = await prometheusService.GetConnectionMetricsAsync();
			outputStream.WriteLine("┌─ Connection Metrics");
			outputStream.WriteLine($"│  Active Connections    : {activeConnections}");
			outputStream.WriteLine($"│  Logged In Players     : {loggedInPlayers}");
			outputStream.WriteLine();

			// Get most called functions (test session duration)
			var mostCalledFunctions = await prometheusService.GetMostCalledFunctionsAsync("1h", 10);
			if (mostCalledFunctions.Count > 0)
			{
				outputStream.WriteLine("┌─ Most Called Functions (Top 10)");
				outputStream.WriteLine("│  Function                    Calls/sec");
				outputStream.WriteLine("│  ────────────────────────────────────────");
				foreach (var (functionName, callsPerSecond) in mostCalledFunctions)
				{
					outputStream.WriteLine($"│  {functionName,-28} {callsPerSecond,9:F3}");
				}
				outputStream.WriteLine();
			}

			// Get slowest functions
			var slowestFunctions = await prometheusService.GetSlowestFunctionsAsync("1h", 10);
			if (slowestFunctions.Count > 0)
			{
				outputStream.WriteLine("┌─ Slowest Functions (Top 10)");
				outputStream.WriteLine("│  Function                    Avg Time (ms)");
				outputStream.WriteLine("│  ────────────────────────────────────────────");
				foreach (var (functionName, avgDuration) in slowestFunctions)
				{
					outputStream.WriteLine($"│  {functionName,-28} {avgDuration,13:F3}");
				}
				outputStream.WriteLine();
			}

			// Get most called commands
			var mostCalledCommands = await prometheusService.GetMostCalledCommandsAsync("1h", 10);
			if (mostCalledCommands.Count > 0)
			{
				outputStream.WriteLine("┌─ Most Called Commands (Top 10)");
				outputStream.WriteLine("│  Command                     Calls/sec");
				outputStream.WriteLine("│  ────────────────────────────────────────");
				foreach (var (commandName, callsPerSecond) in mostCalledCommands)
				{
					outputStream.WriteLine($"│  {commandName,-28} {callsPerSecond,9:F3}");
				}
				outputStream.WriteLine();
			}

			// Get slowest commands
			var slowestCommands = await prometheusService.GetSlowestCommandsAsync("1h", 10);
			if (slowestCommands.Count > 0)
			{
				outputStream.WriteLine("┌─ Slowest Commands (Top 10)");
				outputStream.WriteLine("│  Command                     Avg Time (ms)");
				outputStream.WriteLine("│  ────────────────────────────────────────────");
				foreach (var (commandName, avgDuration) in slowestCommands)
				{
					outputStream.WriteLine($"│  {commandName,-28} {avgDuration,13:F3}");
				}
				outputStream.WriteLine();
			}

			outputStream.WriteLine("═══════════════════════════════════════════════════════════════");
			outputStream.WriteLine("                    END TELEMETRY SUMMARY");
			outputStream.WriteLine("═══════════════════════════════════════════════════════════════");
			outputStream.WriteLine();
		}
		catch (Exception ex)
		{
			// Gracefully handle any errors - don't let telemetry reporting break test cleanup
			outputStream.WriteLine($"Note: Unable to retrieve telemetry summary: {ex.Message}");
		}
	}
}
