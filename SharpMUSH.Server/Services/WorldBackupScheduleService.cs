using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Takes a hot copy of the world every <see cref="IWorldBackupService.ScheduledInterval"/>, so an
/// external snapshot tool always finds a recent consistent one instead of reading the live database
/// out from under the writer.
///
/// <para>Off unless an interval is configured, and inert on a provider that cannot copy its own
/// world. Set the interval to comfortably less than the snapshot tool's own period: the copy the
/// snapshot picks up is as stale as the last run, and it costs a full copy each time.</para>
///
/// <para>This is PennMUSH's dump cycle: everyone is warned five minutes and one minute before each copy, and the
/// copy raises the dump events (<see cref="IBackupRunner"/>).</para>
/// </summary>
public sealed class WorldBackupScheduleService(
	IWorldBackupService backups,
	IBackupRunner runner,
	TimeProvider time,
	ILogger<WorldBackupScheduleService> logger) : BackgroundService
{
	private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

	private DateTimeOffset? _cycleStart;
	private bool _warnedFive;
	private bool _warnedOne;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!backups.IsSupported)
		{
			logger.LogDebug("Scheduled world backups are unavailable: {Reason}", backups.UnavailableReason);
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

		// Checked every second so the warnings land on time; the first copy is one interval away rather than
		// competing with startup.
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				await TickAsync(time.GetUtcNow(), interval, stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
			// Shutdown.
		}
	}

	/// <summary>
	/// PennMUSH's dump cycle (src/timer.c:194-240) at <paramref name="now"/>: the warnings five minutes and one
	/// minute before the copy, each only when the interval is longer, then the copy, and the next cycle starts.
	/// A failed copy is reported and the schedule carries on, because the next one may well succeed and a stopped
	/// schedule is silent.
	/// </summary>
	public async ValueTask TickAsync(DateTimeOffset now, TimeSpan interval, CancellationToken cancellationToken = default)
	{
		if (_cycleStart is not { } start)
		{
			StartCycle(now);
			return;
		}

		var due = start + interval;
		if (!_warnedFive && interval > FiveMinutes && now >= due - FiveMinutes)
		{
			_warnedFive = true;
			await runner.WarnAsync(5);
		}

		if (!_warnedOne && interval > OneMinute && now >= due - OneMinute)
		{
			_warnedOne = true;
			await runner.WarnAsync(1);
		}

		if (now < due)
		{
			return;
		}

		StartCycle(now);
		if (await runner.RunAsync(null, cancellationToken) is WorldBackup backup)
		{
			logger.LogInformation("Scheduled world backup {Name} written", backup.Name);
		}
	}

	private void StartCycle(DateTimeOffset now)
	{
		_cycleStart = now;
		_warnedFive = false;
		_warnedOne = false;
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
