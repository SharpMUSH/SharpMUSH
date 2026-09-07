using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Takes a hot copy of the world every <see cref="IWorldBackupService.ScheduledInterval"/>, so an
/// external snapshot tool always finds a recent consistent one instead of reading the live database
/// out from under the writer.
///
/// <para>Off unless an interval is configured, and inert on a provider with no world directory to
/// copy. Set the interval to comfortably less than the snapshot tool's own period: the copy the
/// snapshot picks up is as stale as the last run, and it costs a full copy each time.</para>
/// </summary>
public sealed class WorldBackupScheduleService(
	IWorldBackupService backups,
	ILogger<WorldBackupScheduleService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!backups.IsSupported)
		{
			logger.LogDebug("Scheduled world backups are unavailable: this provider has no world directory to copy");
			return;
		}

		// Before the first copy there is nothing here, and an external snapshot tool pointed at the
		// directory fails on a path that does not exist — on the first night of a new deployment, which
		// is exactly when nobody is watching. An empty directory is enough for it to snapshot nothing.
		EnsureRoot();

		var interval = backups.ScheduledInterval;
		if (interval <= TimeSpan.Zero)
		{
			logger.LogInformation("Scheduled world backups are off; @backup still takes one on demand");
			return;
		}

		logger.LogInformation(
			"Scheduled world backups every {Interval} into {Root}, keeping {Keep}", interval, backups.Root, backups.Keep);

		// PeriodicTimer, so the first copy is one interval away rather than competing with startup.
		using var timer = new PeriodicTimer(interval);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				var result = await backups.CreateAsync(stoppingToken);
				result.Switch(
					backup => logger.LogInformation("Scheduled world backup {Name} written", backup.Name),
					// Reported and dropped: one failed copy must not end the schedule, because the next
					// interval may well succeed and a stopped schedule is silent.
					error => logger.LogError("Scheduled world backup failed: {Error}", error.Value));
			}
		}
		catch (OperationCanceledException)
		{
			// Shutdown.
		}
	}

	/// <summary>
	/// Best-effort: an unwritable backup root is worth a warning, never a failed startup, because the
	/// game itself does not need it.
	/// </summary>
	private void EnsureRoot()
	{
		if (string.IsNullOrEmpty(backups.Root)) return;
		try
		{
			Directory.CreateDirectory(backups.Root);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Could not create the world backup directory {Root}", backups.Root);
		}
	}
}
