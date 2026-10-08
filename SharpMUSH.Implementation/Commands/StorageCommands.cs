using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Where the world's disk goes, and what the version histories inside it hold. A SharpMUSH command:
	/// PennMUSH keeps its database in memory and has no file to report on.
	///
	/// <para>With no switch, the capacity report: the map limit, the file's length, what the disk has
	/// allocated for it and the live data inside it, then what a backup run needs and any earlier world
	/// still on disk. <c>/HISTORY</c> counts the wiki revisions, pose edits and deleted poses the world
	/// keeps, with the retention policy for each. <c>/PURGE</c> runs a retention pass now, under that
	/// policy — which, unless the server was configured otherwise, keeps everything.</para>
	/// </summary>
	[SharpCommand(Name = "@STORAGE", Switches = ["HISTORY", "PURGE"], Behavior = CB.Default,
		CommandLock = "PERM^server.operate", MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Storage(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (switches.Contains("PURGE"))
		{
			await StoragePurgeAsync(executor);
		}
		else if (switches.Contains("HISTORY"))
		{
			await StorageHistoryAsync(executor);
		}
		else
		{
			await StorageCapacityAsync(executor);
		}

		return CallState.Empty;
	}

	private async ValueTask StorageCapacityAsync(AnySharpObject executor)
	{
		var report = StorageCapacity.Measure();

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageWorldFormat), executor,
			report.WorldPath, DescribeKnownBytes(report.MapSizeBytes), DescribeKnownBytes(report.FileBytes),
			DescribeKnownBytes(report.AllocatedBytes));
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StoragePagesFormat), executor,
			DescribeKnownBytes(report.LiveBytes), DescribeKnownBytes(report.FreeBytes),
			DescribeKnownBytes(report.MapHeadroomBytes));
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageDiskFreeFormat), executor,
			DescribeKnownBytes(report.WorldDiskFreeBytes));
		var feeds = await Mediator.Send(new GetFeedUsageQuery(), ExecutionBudget.CurrentToken);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageFeedsFormat), executor,
			feeds.Sum(kind => kind.Messages), feeds.Sum(kind => kind.Feeds), feeds.Count,
			DescribeBytes(feeds.Sum(kind => kind.StoredBytes)));
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageCompactionNote), executor);
		if (report.StaleReadersCleared > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageStaleReadersFormat),
				executor, report.StaleReadersCleared);
		}

		var backup = report.Backup;
		if (string.IsNullOrEmpty(backup.Root))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageBackupUnavailable),
				executor);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageBackupFormat), executor,
				backup.Root, backup.Copies, DescribeKnownBytes(backup.CopiesBytes), backup.Keep,
				DescribeKnownBytes(backup.NextCopyBytes), DescribeKnownBytes(backup.RequiredFreeBytes),
				DescribeKnownBytes(backup.DiskFreeBytes), DescribeKnownBytes(backup.PeakBytes));
			if (backup.SharesDiskWithWorld)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageBackupSharesDisk),
					executor);
			}

			if (!backup.NextRunFits)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageBackupWontFit),
					executor);
			}
		}

		foreach (var leftover in report.Leftovers)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageLeftoverFormat),
				executor, leftover.Path, leftover.Kind, DescribeKnownBytes(leftover.Bytes));
		}
	}

	private async ValueTask StorageHistoryAsync(AnySharpObject executor)
	{
		var usage = await HistoryRetention.MeasureAsync();
		if (usage.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageHistoryNone), executor);
			return;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageHistoryHeader), executor);
		foreach (var kind in usage)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageHistoryRowFormat),
				executor, kind.Kind, kind.Description, kind.Records, DescribeKnownBytes(kind.Bytes), kind.Holders,
				HistoryRetention.Options.RuleFor(kind.Kind).Describe());
		}

		await NotifyArchiveAsync(executor);
	}

	private async ValueTask StoragePurgeAsync(AnySharpObject executor)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StoragePurgeStarted), executor);
		foreach (var outcome in await HistoryRetention.PurgeAsync())
		{
			await (outcome switch
			{
				HistoryPurged purged => NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.StoragePurgedFormat), executor, purged.Kind, purged.Records,
					DescribeKnownBytes(purged.Bytes), purged.Batches),
				HistoryKeptEverything kept => NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.StoragePurgeKeptFormat), executor, kept.Kind),
				HistoryPurgeFailed failed => NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.StoragePurgeFailedFormat), executor, failed.Kind, failed.Records,
					DescribeKnownBytes(failed.Bytes), failed.Reason)
			});
		}

		await NotifyArchiveAsync(executor);
	}

	private async ValueTask NotifyArchiveAsync(AnySharpObject executor)
	{
		if (string.IsNullOrWhiteSpace(HistoryRetention.Options.ArchivePath))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageHistoryNoArchive),
				executor);
			return;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageHistoryArchiveFormat),
			executor, HistoryRetention.Options.ArchivePath);
	}

	/// <summary><see cref="DescribeBytes"/> for a figure that may be unknown (-1).</summary>
	private static string DescribeKnownBytes(long bytes) => bytes < 0 ? "unknown" : DescribeBytes(bytes);
}
