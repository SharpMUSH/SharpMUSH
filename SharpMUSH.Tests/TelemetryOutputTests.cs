using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Test that outputs telemetry summary.
/// This test should be run last to provide a summary of metrics collected during the test session.
/// </summary>
public class TelemetryOutputTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactory { get; init; }

	[Test]
	public async Task OutputTelemetrySummary()
	{
		var prometheusService = WebAppFactory.Services.GetService<IPrometheusQueryService>();
		if (prometheusService == null)
		{
			Console.WriteLine("PrometheusQueryService not available");
			return;
		}

		Console.WriteLine();
		Console.WriteLine("═══════════════════════════════════════════════════════════════");
		Console.WriteLine("                  TEST SESSION TELEMETRY SUMMARY");
		Console.WriteLine("═══════════════════════════════════════════════════════════════");
		Console.WriteLine();

		// Get health status
		var healthStatus = await prometheusService.GetHealthStatusAsync();
		if (healthStatus.Count > 0)
		{
			Console.WriteLine("┌─ Health Status");
			foreach (var (service, status) in healthStatus)
			{
				var statusText = status == 1 ? "✓ Healthy" : "✗ Unhealthy";
				Console.WriteLine($"│  {service,-20}: {statusText}");
			}
			Console.WriteLine();
		}

		// Get connection metrics
		var (activeConnections, loggedInPlayers) = await prometheusService.GetConnectionMetricsAsync();
		Console.WriteLine("┌─ Connection Metrics");
		Console.WriteLine($"│  Active Connections    : {activeConnections}");
		Console.WriteLine($"│  Logged In Players     : {loggedInPlayers}");
		Console.WriteLine();

		// Get most called functions (test session duration)
		var mostCalledFunctions = await prometheusService.GetMostCalledFunctionsAsync("1h", 10);
		if (mostCalledFunctions.Count > 0)
		{
			Console.WriteLine("┌─ Most Called Functions (Top 10)");
			Console.WriteLine("│  Function                    Calls/sec");
			Console.WriteLine("│  ────────────────────────────────────────");
			foreach (var (functionName, callsPerSecond) in mostCalledFunctions)
			{
				Console.WriteLine($"│  {functionName,-28} {callsPerSecond,9:F3}");
			}
			Console.WriteLine();
		}

		// Get slowest functions
		var slowestFunctions = await prometheusService.GetSlowestFunctionsAsync("1h", 10);
		if (slowestFunctions.Count > 0)
		{
			Console.WriteLine("┌─ Slowest Functions (Top 10)");
			Console.WriteLine("│  Function                    Avg Time (ms)");
			Console.WriteLine("│  ────────────────────────────────────────────");
			foreach (var (functionName, avgDuration) in slowestFunctions)
			{
				Console.WriteLine($"│  {functionName,-28} {avgDuration,13:F3}");
			}
			Console.WriteLine();
		}

		// Get most called commands
		var mostCalledCommands = await prometheusService.GetMostCalledCommandsAsync("1h", 10);
		if (mostCalledCommands.Count > 0)
		{
			Console.WriteLine("┌─ Most Called Commands (Top 10)");
			Console.WriteLine("│  Command                     Calls/sec");
			Console.WriteLine("│  ────────────────────────────────────────");
			foreach (var (commandName, callsPerSecond) in mostCalledCommands)
			{
				Console.WriteLine($"│  {commandName,-28} {callsPerSecond,9:F3}");
			}
			Console.WriteLine();
		}

		// Get slowest commands
		var slowestCommands = await prometheusService.GetSlowestCommandsAsync("1h", 10);
		if (slowestCommands.Count > 0)
		{
			Console.WriteLine("┌─ Slowest Commands (Top 10)");
			Console.WriteLine("│  Command                     Avg Time (ms)");
			Console.WriteLine("│  ────────────────────────────────────────────");
			foreach (var (commandName, avgDuration) in slowestCommands)
			{
				Console.WriteLine($"│  {commandName,-28} {avgDuration,13:F3}");
			}
			Console.WriteLine();
		}

		Console.WriteLine("═══════════════════════════════════════════════════════════════");
		Console.WriteLine("                    END TELEMETRY SUMMARY");
		Console.WriteLine("═══════════════════════════════════════════════════════════════");
		Console.WriteLine();
	}
}
