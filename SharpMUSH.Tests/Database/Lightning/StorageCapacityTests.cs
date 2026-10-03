using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Capacity reporting for a Lightning world (#1465): the map limit, the file's length, what the disk has
/// allocated, the live data inside it, what the next backup needs, and the earlier worlds beside it —
/// each told apart, and each checked against the thing it claims to measure.
/// </summary>
public class StorageCapacityTests
{
	private const long MapSize = 256L << 20;

	private static string TempPath() => Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));

	private static void Delete(string path)
	{
		try
		{
			if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
		}
	}

	private static LightningDatabase Database(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = MapSize }, Substitute.For<IPasswordService>(), relations: null);

	private static (LightningWorldBackupService Backups, LightningStorageCapacityService Capacity) Services(
		LightningDatabase db, string root, bool compact = true)
	{
		var backups = new LightningWorldBackupService(db, new WorldBackupOptions { Root = root, Keep = 2 }, compact,
			NullLogger<LightningWorldBackupService>.Instance);
		return (backups, new LightningStorageCapacityService(db, backups, compact));
	}

	[Test]
	public async Task TheFourFiguresAreToldApart()
	{
		var path = TempPath();
		var root = TempPath();
		var db = Database(path);
		try
		{
			await db.Migrate();
			var (_, capacity) = Services(db, root);

			var report = capacity.Measure();

			await Assert.That(report.MapSizeBytes).IsEqualTo(MapSize);
			await Assert.That(report.FileBytes).IsEqualTo(new FileInfo(Path.Join(path, "data.mdb")).Length);
			await Assert.That(report.UsedPages).IsGreaterThan(2);
			await Assert.That(report.UsedPages).IsLessThanOrEqualTo(report.FilePages);
			await Assert.That(report.LiveBytes).IsEqualTo(report.UsedPages * report.PageSize);
			await Assert.That(report.MapHeadroomBytes).IsEqualTo(MapSize - report.FilePages * report.PageSize);
			await Assert.That(report.FilePages * report.PageSize).IsLessThanOrEqualTo(report.FileBytes);
			await Assert.That(report.WorldDiskFreeBytes).IsGreaterThan(0);
			if (OperatingSystem.IsLinux())
			{
				// statx reads the blocks the filesystem allocated; the file is sparse up to its length.
				await Assert.That(report.AllocatedBytes).IsGreaterThan(0);
				await Assert.That(report.AllocatedBytes).IsLessThanOrEqualTo(report.FileBytes + (1L << 20));
			}
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// The estimate a backup run checks free space against is the size the copy actually comes out at:
	/// the live pages for a compacting copy, the used file for a plain one. Too low, and the preflight
	/// passes a run that fills the disk.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task TheNextCopyEstimateMatchesTheCopy(bool compact)
	{
		var path = TempPath();
		var root = TempPath();
		var db = Database(path);
		try
		{
			await db.Migrate();
			var (backups, capacity) = Services(db, root, compact);
			var estimate = capacity.Measure().Backup.NextCopyBytes;

			var copy = (await backups.CreateAsync()).Expect<WorldBackup>();
			var written = new FileInfo(Path.Join(copy.Path, "data.mdb")).Length;
			var pageSize = capacity.Measure().PageSize;

			await Assert.That(estimate).IsGreaterThanOrEqualTo(written - 2 * pageSize)
				.Because($"the estimate ({estimate}) must not undershoot the copy ({written})");
			await Assert.That(estimate).IsLessThanOrEqualTo(written + 4 * pageSize);

			var after = capacity.Measure().Backup;
			await Assert.That(after.Copies).IsEqualTo(1);
			await Assert.That(after.CopiesBytes).IsEqualTo(copy.SizeBytes);
			await Assert.That(after.PeakBytes).IsEqualTo(after.CopiesBytes + after.NextCopyBytes);
			await Assert.That(after.RequiredFreeBytes).IsEqualTo(WorldBackupWriter.RequiredFreeBytes(after.NextCopyBytes));
			await Assert.That(after.SharesDiskWithWorld).IsEqualTo(OperatingSystem.IsLinux() || OperatingSystem.IsWindows());
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// The world a promotion replaced, the original a startup compaction kept, a staging world never
	/// promoted, and a backup cut off mid-write are
	/// all reported — none of them is ever deleted automatically, so the report is how an operator finds them.
	/// </summary>
	[Test]
	public async Task EarlierWorldsBesideTheLiveOneAreListed()
	{
		var path = TempPath();
		var root = TempPath();
		var db = Database(path);
		var previous = path + ".previous";
		var precompact = path + LightningCompaction.OriginalSuffix;
		var staging = path + ".staging-abc12345";
		var incoming = Path.Join(root, WorldBackupWriter.IncomingPrefix + "deadbeef");
		try
		{
			await db.Migrate();
			Directory.CreateDirectory(previous);
			await File.WriteAllBytesAsync(Path.Join(previous, "data.mdb"), new byte[4096]);
			Directory.CreateDirectory(precompact);
			Directory.CreateDirectory(staging);
			Directory.CreateDirectory(incoming);
			var (_, capacity) = Services(db, root);

			var leftovers = capacity.Measure().Leftovers;

			await Assert.That(leftovers.Select(l => (l.Kind, l.Path))).IsEquivalentTo(
				[("previous", previous), ("precompact", precompact), ("staging", staging), ("incoming", incoming)]);
			await Assert.That(leftovers.Single(l => l.Kind == "previous").Bytes).IsEqualTo(4096);
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
			Delete(previous);
			Delete(precompact);
			Delete(staging);
		}
	}

	/// <summary>
	/// What a long reader does to the file while writes continue — the operational case behind a slow
	/// backup: LMDB cannot reuse a page an open read transaction can still see, so every overwrite while it
	/// is open lands on fresh pages and the file grows. Once it ends the same churn reuses freed pages and
	/// growth stops. The capacity report is what shows it: file pages up, live pages flat.
	/// </summary>
	[Test]
	public async Task ALongReaderDefersPageReuseAndTheFileGrowsMeanwhile()
	{
		var path = TempPath();
		try
		{
			using var store = new LightningStore(new LightningStoreOptions { Path = path, MapSize = MapSize });
			var value = new byte[4000];
			Random.Shared.NextBytes(value);

			async Task ChurnAsync(int rounds)
			{
				for (var round = 0; round < rounds; round++)
				{
					await store.WriteAsync(tx =>
					{
						for (var key = 0; key < 64; key++) tx.Put(Tables.Meta, Keys.Str($"churn-{key}"), value);
					});
				}
			}

			// Warm up until the file stops growing under churn alone: from here, overwrites reuse pages.
			await ChurnAsync(20);
			var steady = store.Usage();
			await ChurnAsync(20);
			var withoutReader = store.Usage().FilePages - steady.FilePages;

			// A reader that stays open across the churn, as a backup's read transaction does for as long as the
			// copy takes.
			using var opened = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			var reader = Task.Run(() => store.Read(tx =>
			{
				opened.Set();
				release.Wait(TimeSpan.FromMinutes(1));
				return tx.Count(Tables.Meta);
			}));
			opened.Wait(TimeSpan.FromMinutes(1));
			var beforeHeld = store.Usage();
			await ChurnAsync(20);
			var whileHeld = store.Usage();
			release.Set();
			await reader;

			var growthWhileHeld = whileHeld.FilePages - beforeHeld.FilePages;
			await Assert.That(growthWhileHeld).IsGreaterThan(withoutReader + 20)
				.Because($"held: +{growthWhileHeld} pages, unheld: +{withoutReader}");
			await Assert.That(Math.Abs(whileHeld.UsedPages - beforeHeld.UsedPages)).IsLessThan(10)
				.Because("the live data did not change; only the file did");

			// Released, the pages it pinned are free again, and the same churn stops growing the file.
			await ChurnAsync(5);
			var settled = store.Usage();
			await ChurnAsync(20);
			await Assert.That(store.Usage().FilePages - settled.FilePages).IsLessThanOrEqualTo(withoutReader + 2);
			await Assert.That(store.Usage().FreePages).IsGreaterThan(growthWhileHeld / 2)
				.Because("the space the reader forced the file to take stays inside it as free pages");
		}
		finally
		{
			Delete(path);
		}
	}
}
