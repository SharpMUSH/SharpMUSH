using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that monitors the health of the Server and reports it to telemetry.
/// </summary>
public class HealthMonitoringService(ITelemetryService telemetryService, ILogger<HealthMonitoringService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Set initial healthy state
		telemetryService.SetServerHealthState(true);
		
		logger.LogInformation("Health monitoring service started - Server is healthy");
		
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				// In a real implementation, you might check database connectivity,
				// message queue connectivity, etc. For now, we assume healthy if running.
				telemetryService.SetServerHealthState(true);
				
				await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
			}
			catch (OperationCanceledException)
			{
				// Normal shutdown
				break;
			}
			catch (Exception ex) when (IsCatchableExceptionType(ex))
			{
				logger.LogError(ex, "Error in health monitoring service");
				telemetryService.SetServerHealthState(false);
				
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
		
		logger.LogInformation("Health monitoring service stopped");
	}
	/// <summary>
	/// Helper method that determines whether the exception is safe to catch.
	/// This avoids swallowing critical exceptions that should not be handled.
	/// </summary>
	private static bool IsCatchableExceptionType(Exception ex)
	{
		return ex switch
		{
			OutOfMemoryException => false,
			StackOverflowException => false,
			ThreadAbortException => false,
			null => false,
			_ => true
		};
	}
}
