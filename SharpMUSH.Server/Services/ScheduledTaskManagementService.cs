using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that manages scheduled task timings for warnings and purges.
/// Updates NextWarningTime and NextPurgeTime in the UptimeData based on configuration intervals.
/// </summary>
public partial class ScheduledTaskManagementService(
	IExpandedObjectDataService dataService,
	IOptions<SharpMUSHOptions> options,
	ILogger<ScheduledTaskManagementService> logger) : BackgroundService
{
	private readonly TimeSpan _warnInterval = ParseTimeInterval(options.Value.Warning.WarnInterval);
	private readonly TimeSpan _purgeInterval = ParseTimeInterval(options.Value.Dump.PurgeInterval);

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
		logger.LogInformation(
			"Scheduled task management service started - warn_interval: {WarnInterval}, purge_interval: {PurgeInterval}",
			_warnInterval, _purgeInterval);

		// Wait a bit before first update to let StartupHandler complete initialization
		await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				// Get current uptime data
				var data = await dataService.GetExpandedServerDataAsync<UptimeData>();
				if (data == null)
				{
					logger.LogWarning("UptimeData not found - will retry later");
					await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
					continue;
				}

				var now = DateTimeOffset.UtcNow;
				var updated = false;

				// Update warning time if interval is configured and time has passed
				if (_warnInterval > TimeSpan.Zero && data.NextWarningTime <= now)
				{
					var newWarningTime = now + _warnInterval;
					data = data with { NextWarningTime = newWarningTime };
					updated = true;
					logger.LogInformation("Updated NextWarningTime to {Time}", newWarningTime);
				}

				// Update purge time if interval is configured and time has passed
				if (_purgeInterval > TimeSpan.Zero && data.NextPurgeTime <= now)
				{
					var newPurgeTime = now + _purgeInterval;
					data = data with { NextPurgeTime = newPurgeTime };
					updated = true;
					logger.LogInformation("Updated NextPurgeTime to {Time}", newPurgeTime);
				}

				// Save updated data if any changes were made
				if (updated)
				{
					await dataService.SetExpandedServerDataAsync(data);
				}

				// Calculate next check time based on configured intervals
				// Use the shorter of the two intervals, with a minimum of 10 seconds
				var checkInterval = TimeSpan.FromMinutes(1); // Default to 1 minute
				
				if (_warnInterval > TimeSpan.Zero && _purgeInterval > TimeSpan.Zero)
				{
					checkInterval = _warnInterval < _purgeInterval ? _warnInterval : _purgeInterval;
				}
				else if (_warnInterval > TimeSpan.Zero)
				{
					checkInterval = _warnInterval;
				}
				else if (_purgeInterval > TimeSpan.Zero)
				{
					checkInterval = _purgeInterval;
				}

				// Ensure minimum check interval of 10 seconds, maximum of 1 minute
				checkInterval = checkInterval < TimeSpan.FromSeconds(10) 
					? TimeSpan.FromSeconds(10) 
					: (checkInterval > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : checkInterval);

				await Task.Delay(checkInterval, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				// Normal shutdown
				break;
			}
			catch (Exception ex) when (IsCatchableExceptionType(ex))
			{
				logger.LogError(ex, "Error in scheduled task management service");

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

		logger.LogInformation("Scheduled task management service stopped");
	}

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
