using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that periodically checks all objects for warnings.
/// Runs at the interval specified by warn_interval configuration.
/// </summary>
public class WarningCheckService(
	IWarningService warningService,
	IOptions<SharpMUSHOptions> options,
	ILogger<WarningCheckService> logger) : BackgroundService
{
	private readonly int _warnInterval = options.Value.Warning.WarnInterval;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Check if automatic warning checks are enabled
		if (_warnInterval <= 0)
		{
			logger.LogInformation("Automatic warning checks are disabled (warn_interval = {Interval})", _warnInterval);
			return;
		}

		logger.LogInformation("Warning check service started - will run every {Interval} seconds", _warnInterval);

		// Wait a bit before first check to let the system start up
		await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				logger.LogInformation("Starting automatic warning check of all objects");

				// Call the warning service to check all objects
				// This will notify owners of their warnings
				var objectsChecked = await warningService.CheckAllObjectsAsync();

				logger.LogInformation("Completed automatic warning check - {Count} objects checked", objectsChecked);

				// Wait for the configured interval
				await Task.Delay(TimeSpan.FromSeconds(_warnInterval), stoppingToken);
			}
			catch (OperationCanceledException)
			{
				// Normal shutdown
				break;
			}
			catch (Exception ex) when (IsCatchableExceptionType(ex))
			{
				logger.LogError(ex, "Error in warning check service");

				try
				{
					// Wait a bit before retrying on error
					await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}

		logger.LogInformation("Warning check service stopped");
	}

	private static bool IsCatchableExceptionType(Exception ex)
	{
		return ex is not (OutOfMemoryException or StackOverflowException);
	}
}
