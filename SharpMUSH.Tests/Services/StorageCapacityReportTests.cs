using System.Diagnostics.Metrics;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The arithmetic on a capacity report (#1465) — the import preflight and the backup budget — and the
/// gauges that carry it to Prometheus.
/// </summary>
public class StorageCapacityReportTests
{
	private const long Page = 16384;

	private static StorageCapacityReport Report(long mapSize = 64L << 30, long filePages = 1000, long usedPages = 800,
		long diskFree = 100L << 30, long backupDiskFree = 100L << 30) => new()
		{
			WorldPath = "/data/world",
			MapSizeBytes = mapSize,
			PageSize = Page,
			FileBytes = filePages * Page,
			AllocatedBytes = filePages * Page,
			FilePages = filePages,
			UsedPages = usedPages,
			WorldDiskFreeBytes = diskFree,
			Backup = new BackupCapacity("/data/world.backups", 2, 2, 2 * usedPages * Page, usedPages * Page,
				usedPages * Page + (16L << 20), backupDiskFree, SharesDiskWithWorld: true),
			Leftovers = []
		};

	[Test]
	public async Task AnImportThatFitsIsAllowed()
		=> await Assert.That(Report().ImportShortfall(100L << 20)).IsEmpty();

	[Test]
	public async Task AnImportLargerThanTheMapIsRefused()
	{
		// 1000 pages reached of a map of 1100: about 1.6 MB of room, and the import needs three times its size.
		var report = Report(mapSize: 1100 * Page);

		await Assert.That(report.ImportShortfall(10L << 20)).Contains("SHARPMUSH_LIGHTNING_MAPSIZE");
	}

	[Test]
	public async Task AnImportLargerThanTheDiskIsRefused()
		=> await Assert.That(Report(diskFree: 10L << 20).ImportShortfall(100L << 20)).Contains("disk");

	/// <summary>Free pages inside the file are used before the file grows, so they count toward both checks.</summary>
	[Test]
	public async Task FreePagesInsideTheFileCountTowardTheImport()
	{
		// 10 000 free pages (about 160 MB) inside the file, almost nothing free on the disk.
		var report = Report(filePages: 20_000, usedPages: 10_000, diskFree: 1L << 20);

		await Assert.That(report.ImportShortfall(40L << 20)).IsEmpty();
	}

	[Test]
	public async Task TheBackupPeakIsTheKeptCopiesPlusTheNextOne()
	{
		var backup = Report().Backup;

		await Assert.That(backup.PeakBytes).IsEqualTo(backup.CopiesBytes + backup.NextCopyBytes);
		await Assert.That(backup.NextRunFits).IsTrue();
		await Assert.That(Report(backupDiskFree: 1L << 20).Backup.NextRunFits).IsFalse();
	}

	/// <summary>
	/// Every figure reaches the meter, tagged by kind, and an unknown one (-1) is left out rather than
	/// exported as a number an alert would misread.
	/// </summary>
	[Test]
	public async Task TheGaugesCarryTheReport()
	{
		var capacity = Substitute.For<IStorageCapacityService>();
		capacity.Measure().Returns(Report() with { AllocatedBytes = -1 });
		using var metrics = new StorageCapacityMetrics(capacity);

		var bytes = new Dictionary<string, long>();
		var fits = -1L;
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, l) =>
		{
			if (ReferenceEquals(instrument.Meter, MeterOf(metrics))) l.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
		{
			foreach (var tag in tags)
			{
				if (tag.Key == "kind") bytes[(string)tag.Value!] = value;
			}
		});
		listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
		{
			if (instrument.Name == "sharpmush.storage.backup.fits") fits = value;
		});
		listener.Start();
		listener.RecordObservableInstruments();

		await Assert.That(bytes["map"]).IsEqualTo(64L << 30);
		await Assert.That(bytes["live"]).IsEqualTo(800 * Page);
		await Assert.That(bytes["free_pages"]).IsEqualTo(200 * Page);
		await Assert.That(bytes["backup_peak"]).IsEqualTo(3 * 800 * Page);
		await Assert.That(bytes.ContainsKey("allocated")).IsFalse();
		await Assert.That(fits).IsEqualTo(1);
	}

	private static Meter MeterOf(StorageCapacityMetrics metrics)
		=> (Meter)typeof(StorageCapacityMetrics)
			.GetField("_meter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
			.GetValue(metrics)!;
}
