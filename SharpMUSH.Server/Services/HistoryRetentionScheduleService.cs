using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Runs a history retention pass every <c>SHARPMUSH_HISTORY_INTERVAL</c>. Off unless an interval is set,
/// and a pass under rules that keep everything scans nothing — so a game that configures neither loses
/// nothing and pays nothing.
/// </summary>
public sealed class HistoryRetentionScheduleService(
	IHistoryRetentionService retention,
	ILogger<HistoryRetentionScheduleService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var interval = retention.Options.Interval;
		if (interval <= TimeSpan.Zero)
		{
			logger.LogInformation("Scheduled history retention is off; @storage/purge still runs a pass on demand");
			return;
		}

		foreach (var kind in retention.Kinds)
		{
			logger.LogInformation("History retention for {Kind}: {Rule}", kind, retention.Options.RuleFor(kind).Describe());
		}

		using var timer = new PeriodicTimer(interval);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				foreach (var outcome in await retention.PurgeAsync(stoppingToken))
				{
					// A failed kind is reported and the schedule goes on: the next pass resumes from the
					// start, and everything before the failure is already archived and gone.
					if (outcome is HistoryPurgeFailed failed)
					{
						logger.LogError("Scheduled history retention for {Kind} stopped after {Records} records: {Reason}",
							failed.Kind, failed.Records, failed.Reason);
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			logger.LogDebug("Scheduled history retention stopped for shutdown");
		}
	}
}
