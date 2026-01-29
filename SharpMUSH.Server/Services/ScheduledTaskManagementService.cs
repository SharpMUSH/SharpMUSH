using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Hosted service that manages scheduled task timings for warnings and purges using Quartz.NET.
/// Schedules recurring jobs to update NextWarningTime and NextPurgeTime in the UptimeData based on configuration intervals.
/// </summary>
public partial class ScheduledTaskManagementService(
	ISchedulerFactory schedulerFactory,
	IOptions<SharpMUSHOptions> options,
	ILogger<ScheduledTaskManagementService> logger) : IHostedService
{
	private readonly TimeSpan _warnInterval = ParseTimeInterval(options.Value.Warning.WarnInterval);
	private readonly TimeSpan _purgeInterval = ParseTimeInterval(options.Value.Dump.PurgeInterval);
	private IScheduler? _scheduler;

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

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation(
			"Scheduled task management service started - warn_interval: {WarnInterval}, purge_interval: {PurgeInterval}",
			_warnInterval, _purgeInterval);

		_scheduler = await schedulerFactory.GetScheduler(cancellationToken);

		// Schedule warning time update job if interval is configured
		if (_warnInterval > TimeSpan.Zero)
		{
			var warningJob = JobBuilder.Create<UpdateWarningTimeJob>()
				.WithIdentity("UpdateWarningTime", "ScheduledTaskManagement")
				.UsingJobData("interval", _warnInterval.TotalSeconds)
				.Build();

			// Calculate check interval - shorter intervals need more frequent checks, but cap at 1 minute
			var checkInterval = _warnInterval < TimeSpan.FromMinutes(1) ? _warnInterval : TimeSpan.FromMinutes(1);
			if (checkInterval < TimeSpan.FromSeconds(10))
			{
				checkInterval = TimeSpan.FromSeconds(10);
			}

			var warningTrigger = TriggerBuilder.Create()
				.WithIdentity("UpdateWarningTimeTrigger", "ScheduledTaskManagement")
				.StartAt(DateTimeOffset.UtcNow.AddSeconds(10)) // Wait for StartupHandler
				.WithSimpleSchedule(x => x
					.WithInterval(checkInterval)
					.RepeatForever())
				.Build();

			await _scheduler.ScheduleJob(warningJob, warningTrigger, cancellationToken);
			logger.LogInformation("Scheduled warning time update job with interval: {Interval}", checkInterval);
		}

		// Schedule purge time update job if interval is configured
		if (_purgeInterval > TimeSpan.Zero)
		{
			var purgeJob = JobBuilder.Create<UpdatePurgeTimeJob>()
				.WithIdentity("UpdatePurgeTime", "ScheduledTaskManagement")
				.UsingJobData("interval", _purgeInterval.TotalSeconds)
				.Build();

			// Calculate check interval - shorter intervals need more frequent checks, but cap at 1 minute
			var checkInterval = _purgeInterval < TimeSpan.FromMinutes(1) ? _purgeInterval : TimeSpan.FromMinutes(1);
			if (checkInterval < TimeSpan.FromSeconds(10))
			{
				checkInterval = TimeSpan.FromSeconds(10);
			}

			var purgeTrigger = TriggerBuilder.Create()
				.WithIdentity("UpdatePurgeTimeTrigger", "ScheduledTaskManagement")
				.StartAt(DateTimeOffset.UtcNow.AddSeconds(10)) // Wait for StartupHandler
				.WithSimpleSchedule(x => x
					.WithInterval(checkInterval)
					.RepeatForever())
				.Build();

			await _scheduler.ScheduleJob(purgeJob, purgeTrigger, cancellationToken);
			logger.LogInformation("Scheduled purge time update job with interval: {Interval}", checkInterval);
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_scheduler != null)
		{
			logger.LogInformation("Scheduled task management service stopping");
			// Jobs will be cleaned up by Quartz shutdown
		}
		await Task.CompletedTask;
	}

	/// <summary>
	/// Quartz job that updates the NextWarningTime in UptimeData.
	/// </summary>
	public class UpdateWarningTimeJob(
		IExpandedObjectDataService dataService,
		ILogger<UpdateWarningTimeJob> logger) : IJob
	{
		public async Task Execute(IJobExecutionContext context)
		{
			try
			{
				var interval = TimeSpan.FromSeconds(context.JobDetail.JobDataMap.GetDouble("interval"));
				var data = await dataService.GetExpandedServerDataAsync<UptimeData>();
				
				if (data == null)
				{
					logger.LogWarning("UptimeData not found - skipping warning time update");
					return;
				}

				var now = DateTimeOffset.UtcNow;
				if (data.NextWarningTime <= now)
				{
					var newWarningTime = now + interval;
					data = data with { NextWarningTime = newWarningTime };
					await dataService.SetExpandedServerDataAsync(data);
					logger.LogInformation("Updated NextWarningTime to {Time}", newWarningTime);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error updating warning time");
			}
		}
	}

	/// <summary>
	/// Quartz job that updates the NextPurgeTime in UptimeData.
	/// </summary>
	public class UpdatePurgeTimeJob(
		IExpandedObjectDataService dataService,
		ILogger<UpdatePurgeTimeJob> logger) : IJob
	{
		public async Task Execute(IJobExecutionContext context)
		{
			try
			{
				var interval = TimeSpan.FromSeconds(context.JobDetail.JobDataMap.GetDouble("interval"));
				var data = await dataService.GetExpandedServerDataAsync<UptimeData>();
				
				if (data == null)
				{
					logger.LogWarning("UptimeData not found - skipping purge time update");
					return;
				}

				var now = DateTimeOffset.UtcNow;
				if (data.NextPurgeTime <= now)
				{
					var newPurgeTime = now + interval;
					data = data with { NextPurgeTime = newPurgeTime };
					await dataService.SetExpandedServerDataAsync(data);
					logger.LogInformation("Updated NextPurgeTime to {Time}", newPurgeTime);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error updating purge time");
			}
		}
	}
}
