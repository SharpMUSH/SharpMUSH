using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Background service that monitors the health of the ConnectionServer and reports it to telemetry.
/// </summary>
public class HealthMonitoringService(ITelemetryService telemetryService, ILogger<HealthMonitoringService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Set initial healthy state
		telemetryService.SetConnectionServerHealthState(true);
		
		logger.LogInformation("Health monitoring service started - ConnectionServer is healthy");
		
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				// In a real implementation, you might check message queue connectivity,
				// network socket availability, etc. For now, we assume healthy if running.
				telemetryService.SetConnectionServerHealthState(true);
				
				await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
			}
			catch (OperationCanceledException)
			{
				// Normal shutdown
				break;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error in health monitoring service");
				telemetryService.SetConnectionServerHealthState(false);
				
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
}
