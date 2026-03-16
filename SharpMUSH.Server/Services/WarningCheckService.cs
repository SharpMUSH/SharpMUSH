using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that periodically checks all objects for warnings.
/// Runs at the interval specified by warn_interval configuration.
/// </summary>
public partial class WarningCheckService(
	IWarningService warningService,
	IOptions<SharpMUSHOptions> options,
	ILogger<WarningCheckService> logger) : BackgroundService
{
	private readonly TimeSpan _warnInterval = ParseTimeInterval(options.Value.Warning.WarnInterval);

	/// <summary>
	/// Parses a PennMUSH time interval string like "1h", "10m1s", "5d" into a TimeSpan.
	/// Returns TimeSpan.Zero if the string is "0" or invalid.
	/// </summary>
	private static TimeSpan ParseTimeInterval(string interval)
	{
		if (string.IsNullOrWhiteSpace(interval) || interval == "0")
		{
			return TimeSpan.Zero;
		}

		var totalSeconds = 0.0;
		var matches = TimeIntervalRegex().Matches(interval);

		foreach (Match match in matches)
		{
			if (!double.TryParse(match.Groups[1].Value, out var value))
			{
				continue;
			}

			var unit = match.Groups[2].Value;
			totalSeconds += unit switch
			{
				"d" => value * 86400,  // days
				"h" => value * 3600,   // hours
				"m" => value * 60,     // minutes
				"s" => value,          // seconds
				_ => 0
			};
		}

		return totalSeconds > 0 ? TimeSpan.FromSeconds(totalSeconds) : TimeSpan.Zero;
	}

	[GeneratedRegex(@"(\d+(?:\.\d+)?)(d|h|m|s)", RegexOptions.IgnoreCase)]
	private static partial Regex TimeIntervalRegex();

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Check if automatic warning checks are enabled
		if (_warnInterval == TimeSpan.Zero)
		{
			logger.LogInformation("Automatic warning checks are disabled (warn_interval = 0)");
			return;
		}

		logger.LogInformation("Warning check service started - will run every {Interval}", _warnInterval);

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
				await Task.Delay(_warnInterval, stoppingToken);
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
