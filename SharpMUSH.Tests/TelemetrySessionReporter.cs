using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Outputs telemetry summary at the end of a test session.
/// This class is registered as a PerTestSession shared resource and outputs
/// telemetry data when the test session completes.
/// </summary>
public class TelemetrySessionReporter : IAsyncDisposable
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactory { get; init; }

	public async ValueTask DisposeAsync()
	{
		await OutputTelemetrySummaryAsync();
		GC.SuppressFinalize(this);
	}

	private async Task OutputTelemetrySummaryAsync()
	{
		try
		{
			if (WebAppFactory?.Services == null)
			{
				return;
			}

			var prometheusService = WebAppFactory.Services.GetService<IPrometheusQueryService>();
			if (prometheusService == null)
			{
				return;
			}

			// Use Console.Error to ensure output is visible even if stdout is redirected
			await Console.Error.WriteLineAsync();
			await Console.Error.WriteLineAsync("═══════════════════════════════════════════════════════════════");
			await Console.Error.WriteLineAsync("                  TEST SESSION TELEMETRY SUMMARY");
			await Console.Error.WriteLineAsync("═══════════════════════════════════════════════════════════════");
			await Console.Error.WriteLineAsync();

			// Get health status
			var healthStatus = await prometheusService.GetHealthStatusAsync();
			if (healthStatus.Count > 0)
			{
				await Console.Error.WriteLineAsync("┌─ Health Status");
				foreach (var (service, status) in healthStatus)
				{
					var statusText = status == 1 ? "✓ Healthy" : "✗ Unhealthy";
					await Console.Error.WriteLineAsync($"│  {service,-20}: {statusText}");
				}
				await Console.Error.WriteLineAsync();
			}

			// Get connection metrics
			var (activeConnections, loggedInPlayers) = await prometheusService.GetConnectionMetricsAsync();
			await Console.Error.WriteLineAsync("┌─ Connection Metrics");
			await Console.Error.WriteLineAsync($"│  Active Connections    : {activeConnections}");
			await Console.Error.WriteLineAsync($"│  Logged In Players     : {loggedInPlayers}");
			await Console.Error.WriteLineAsync();

			// Get most called functions (test session duration)
			var mostCalledFunctions = await prometheusService.GetMostCalledFunctionsAsync("1h", 10);
			if (mostCalledFunctions.Count > 0)
			{
				await Console.Error.WriteLineAsync("┌─ Most Called Functions (Top 10)");
				await Console.Error.WriteLineAsync("│  Function                    Calls/sec");
				await Console.Error.WriteLineAsync("│  ────────────────────────────────────────");
				foreach (var (functionName, callsPerSecond) in mostCalledFunctions)
				{
					await Console.Error.WriteLineAsync($"│  {functionName,-28} {callsPerSecond,9:F3}");
				}
				await Console.Error.WriteLineAsync();
			}

			// Get slowest functions
			var slowestFunctions = await prometheusService.GetSlowestFunctionsAsync("1h", 10);
			if (slowestFunctions.Count > 0)
			{
				await Console.Error.WriteLineAsync("┌─ Slowest Functions (Top 10)");
				await Console.Error.WriteLineAsync("│  Function                    Avg Time (ms)");
				await Console.Error.WriteLineAsync("│  ────────────────────────────────────────────");
				foreach (var (functionName, avgDuration) in slowestFunctions)
				{
					await Console.Error.WriteLineAsync($"│  {functionName,-28} {avgDuration,13:F3}");
				}
				await Console.Error.WriteLineAsync();
			}

			// Get most called commands
			var mostCalledCommands = await prometheusService.GetMostCalledCommandsAsync("1h", 10);
			if (mostCalledCommands.Count > 0)
			{
				await Console.Error.WriteLineAsync("┌─ Most Called Commands (Top 10)");
				await Console.Error.WriteLineAsync("│  Command                     Calls/sec");
				await Console.Error.WriteLineAsync("│  ────────────────────────────────────────");
				foreach (var (commandName, callsPerSecond) in mostCalledCommands)
				{
					await Console.Error.WriteLineAsync($"│  {commandName,-28} {callsPerSecond,9:F3}");
				}
				await Console.Error.WriteLineAsync();
			}

			// Get slowest commands
			var slowestCommands = await prometheusService.GetSlowestCommandsAsync("1h", 10);
			if (slowestCommands.Count > 0)
			{
				await Console.Error.WriteLineAsync("┌─ Slowest Commands (Top 10)");
				await Console.Error.WriteLineAsync("│  Command                     Avg Time (ms)");
				await Console.Error.WriteLineAsync("│  ────────────────────────────────────────────");
				foreach (var (commandName, avgDuration) in slowestCommands)
				{
					await Console.Error.WriteLineAsync($"│  {commandName,-28} {avgDuration,13:F3}");
				}
				await Console.Error.WriteLineAsync();
			}

			await Console.Error.WriteLineAsync("═══════════════════════════════════════════════════════════════");
			await Console.Error.WriteLineAsync("                    END TELEMETRY SUMMARY");
			await Console.Error.WriteLineAsync("═══════════════════════════════════════════════════════════════");
			await Console.Error.WriteLineAsync();
		}
		catch (Exception ex)
		{
			// Gracefully handle any errors - don't let telemetry reporting break test cleanup
			await Console.Error.WriteLineAsync($"Note: Unable to retrieve telemetry summary: {ex.Message}");
		}
	}
}
