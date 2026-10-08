using System.Globalization;
using MarkupString.Layout;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
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
		var feeds = await Mediator.Send(new GetFeedUsageQuery(), ExecutionBudget.CurrentToken);
		var feedBytes = feeds.Sum(kind => kind.StoredBytes);

		var parts = new List<Block>
		{
			new Fields([
				.. Meters(
				("Map", report.FileBytes, report.MapSizeBytes,
					$"{DescribeKnownBytes(report.FileBytes)} of {DescribeKnownBytes(report.MapSizeBytes)} limit"),
				("File", report.LiveBytes, report.FileBytes,
					$"{DescribeKnownBytes(report.LiveBytes)} live, {DescribeKnownBytes(report.FreeBytes)} reusable"),
				("Feeds", feedBytes, report.LiveBytes, $"{DescribeBytes(feedBytes)} of the live data")),
				.. Facts(
				("Path", report.WorldPath),
				("On disk", DescribeKnownBytes(report.AllocatedBytes)),
				("Disk free", DescribeKnownBytes(report.WorldDiskFreeBytes)))]),
		};

		if (feeds.Count > 0)
		{
			parts.Add(new Rule(MarkupText.Plain("Feeds")));
			parts.Add(ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Kind")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Feeds")) { Alignment = Alignment.Right, Wrap = false },
					new TableColumn(MarkupText.Plain("Lines")) { Alignment = Alignment.Right, Wrap = false },
					new TableColumn(MarkupText.Plain("Stored")) { Alignment = Alignment.Right, Wrap = false },
				],
				feeds.OrderByDescending(kind => kind.StoredBytes).Select(kind => (IEnumerable<string>)
				[
					kind.Kind,
					kind.Feeds.ToString(CultureInfo.InvariantCulture),
					kind.Messages.ToString(CultureInfo.InvariantCulture),
					DescribeBytes(kind.StoredBytes),
				])));
		}

		var backup = report.Backup;
		parts.Add(new Rule(MarkupText.Plain("Backups")));
		if (string.IsNullOrEmpty(backup.Root))
		{
			parts.Add(new TextBlock(MarkupText.Plain("This provider cannot take backups.")));
		}
		else
		{
			parts.Add(new Fields([
				.. Meters(("Next run", backup.RequiredFreeBytes, backup.DiskFreeBytes,
					$"needs {DescribeKnownBytes(backup.RequiredFreeBytes)} of {DescribeKnownBytes(backup.DiskFreeBytes)} free")),
				.. Facts(
					("Path", backup.Root),
					("Copies", $"{backup.Copies} of {backup.Keep} kept, {DescribeKnownBytes(backup.CopiesBytes)}"),
					("Next copy", $"{DescribeKnownBytes(backup.NextCopyBytes)}, {DescribeKnownBytes(backup.PeakBytes)} at the peak"))]));
		}

		if (report.Leftovers.Count > 0)
		{
			parts.Add(new Rule(MarkupText.Plain("Left over")));
			parts.Add(ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Path")) { Min = 10 },
					new TableColumn(MarkupText.Plain("Kind")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Size")) { Alignment = Alignment.Right, Wrap = false },
				],
				report.Leftovers.Select(leftover => (IEnumerable<string>)
					[leftover.Path, leftover.Kind, DescribeKnownBytes(leftover.Bytes)])));
		}

		parts.Add(new Rule());
		parts.Add(new Bullets([.. StorageNotes(report, backup).Select(note => (Block)new TextBlock(MarkupText.Plain(note)))]));

		await NotifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Storage"), [.. parts]), 78));
	}

	/// <summary>The few things the figures alone do not say.</summary>
	private static IEnumerable<string> StorageNotes(StorageCapacityReport report, BackupCapacity backup)
	{
		if (!string.IsNullOrEmpty(backup.Root) && !backup.NextRunFits)
		{
			yield return "The next backup will not start: too little free disk.";
		}

		if (!string.IsNullOrEmpty(backup.Root) && backup.SharesDiskWithWorld)
		{
			yield return "Backups share the world's disk.";
		}

		if (report.StaleReadersCleared > 0)
		{
			yield return $"{report.StaleReadersCleared} dead reader(s) cleared since startup.";
		}

		if (report.Leftovers.Count > 0)
		{
			yield return "Delete left-over worlds once you no longer need them.";
		}

		yield return "Deletes free space inside the file. Only a compacted copy shrinks it.";
	}

	/// <summary>
	/// One gauge per row, labels lined up, each bar followed by its figures. A row whose maximum is unknown
	/// shows its figures alone.
	/// </summary>
	private static List<Field> Meters(params ReadOnlySpan<(string Label, long Value, long Maximum, string Figures)> rows)
	{
		var fields = new List<Field>();
		foreach (var (label, value, maximum, figures) in rows)
		{
			var text = new TextBlock(MarkupText.Plain(figures));
			fields.Add(new Field(MarkupText.Plain(label), value < 0 || maximum <= 0
				? text
				: new Flex([new Gauge(value, maximum) { BarWidth = 24 }, text]) { Gap = 2 }));
		}

		return fields;
	}

	/// <summary>Labelled figures, to sit in the same column as <see cref="Meters"/>.</summary>
	private static IEnumerable<Field> Facts(params (string Label, string Value)[] rows) =>
		rows.Select(row => new Field(MarkupText.Plain(row.Label), new TextBlock(MarkupText.Plain(row.Value))));

	private async ValueTask StorageHistoryAsync(AnySharpObject executor)
	{
		var usage = await HistoryRetention.MeasureAsync();
		if (usage.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StorageHistoryNone), executor);
			return;
		}

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Kind")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Records")) { Alignment = Alignment.Right, Wrap = false },
				new TableColumn(MarkupText.Plain("Size")) { Alignment = Alignment.Right, Wrap = false },
				new TableColumn(MarkupText.Plain("Streams")) { Alignment = Alignment.Right, Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Policy")) { Min = 12 },
			],
			usage.Select(kind => (IEnumerable<string>)
			[
				kind.Kind,
				kind.Records.ToString(CultureInfo.InvariantCulture),
				DescribeKnownBytes(kind.Bytes),
				kind.Holders.ToString(CultureInfo.InvariantCulture),
				HistoryRetention.Options.RuleFor(kind.Kind).Describe(),
			]));
		var archive = string.IsNullOrWhiteSpace(HistoryRetention.Options.ArchivePath)
			? "No archive: a purged record survives only in older backups."
			: $"Purged records are archived to {HistoryRetention.Options.ArchivePath} first.";
		await NotifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("History"), table,
			new Rule(), new TextBlock(MarkupText.Plain(archive))), 78));
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
