using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The hot backup an operator's snapshot tool reads instead of the live world: a copy that opens as
/// a valid environment carrying the same data, a bounded number of them kept on disk, and a
/// <c>latest</c> pointer at the newest.
/// </summary>
public class BackupTests
{
	private static string TempPath() => Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));

	private static void Delete(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
		}
	}

	/// <summary>A migrated world plus a backup service writing into <paramref name="root"/>.</summary>
	private static (LightningDatabase Db, LightningWorldBackupService Backups) Fixture(string path, string root,
		int keep = 2, bool compact = true, int packageKeep = 2)
	{
		// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
		var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(),
			relations: null);
		var backups = new LightningWorldBackupService(db,
			new WorldBackupOptions { Root = root, Keep = keep, PackageOperationKeep = packageKeep }, compact,
			NullLogger<LightningWorldBackupService>.Instance);
		return (db, backups);
	}

	[Test]
	public async Task CreateWritesACopyThatOpensAsAnEnvironmentWithTheSameData()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();
			var objects = db.Store.Count(Tables.Obj);

			var result = await backups.CreateAsync();

			var backup = result.Expect<WorldBackup>();
			using var copy = new LightningStore(new LightningStoreOptions { Path = backup.Path, MapSize = 256L << 20 });
			await Assert.That(copy.Count(Tables.Obj)).IsEqualTo(objects);
			var godName = copy.Read(tx => tx.TryGet(Tables.Obj, Keys.Dbref(1), out var v)
				? Codec.Deserialize<ObjectRecord>(v).Name
				: null);
			await Assert.That(godName).IsEqualTo("God");
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// <c>SHARPMUSH_LIGHTNING_BACKUP_COMPACT=false</c> trades size for speed. The copy it produces has
	/// to open and carry the same data as a compacted one, or the setting is a way to make unusable
	/// backups.
	/// </summary>
	[Test]
	public async Task AnUncompactedCopyOpensWithTheSameDataToo()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root, compact: false);
		try
		{
			await db.Migrate();
			var objects = db.Store.Count(Tables.Obj);

			var result = (await backups.CreateAsync()).Expect<WorldBackup>();

			using var copy = new LightningStore(new LightningStoreOptions
			{
				Path = result.Path,
				MapSize = 256L << 20
			});
			await Assert.That(copy.Count(Tables.Obj)).IsEqualTo(objects);
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	[Test]
	public async Task ACopyCarriesWritesCommittedBeforeItAndNotThoseAfter()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();
			await db.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("before"), Keys.Str("1")));

			var result = (await backups.CreateAsync()).Expect<WorldBackup>();

			await db.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("after"), Keys.Str("1")));

			using var copy = new LightningStore(new LightningStoreOptions
			{
				Path = result.Path,
				MapSize = 256L << 20
			});
			await Assert.That(copy.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("before"), out _))).IsTrue();
			await Assert.That(copy.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("after"), out _))).IsFalse();
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// The copies taken before package operations (#1333) have a directory and a count of their own:
	/// however many package operations run, the scheduled and manual copies all stay, and those
	/// copies' retention never deletes a pre-package one.
	/// </summary>
	[Test]
	public async Task PackageOperationCopies_NeverEvictTheScheduledOnes()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root, keep: 2, packageKeep: 1);
		try
		{
			await db.Migrate();
			var scheduled = new[]
			{
				(await backups.CreateAsync()).Expect<WorldBackup>().Name,
				(await backups.CreateAsync()).Expect<WorldBackup>().Name
			};

			var before = new List<WorldBackup>();
			for (var i = 0; i < 3; i++)
			{
				before.Add((await backups.CreateBeforePackageOperationAsync()).Expect<WorldBackup>());
			}

			await Assert.That(backups.List().Select(b => b.Name).ToArray()).IsEquivalentTo(scheduled);
			var kept = backups.ListPackageOperationBackups();
			await Assert.That(kept.Count).IsEqualTo(1);
			await Assert.That(kept[0].Name).IsEqualTo(before[^1].Name);
			await Assert.That(Path.GetDirectoryName(kept[0].Path))
				.IsEqualTo(Path.Join(root, WorldBackupOptions.PackageOperationDirectory));

			// And the other way round: a scheduled run's retention leaves the pre-package copy alone.
			await backups.CreateAsync();
			await Assert.That(backups.List().Count).IsEqualTo(2);
			await Assert.That(backups.ListPackageOperationBackups().Single().Name).IsEqualTo(before[^1].Name);
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	[Test]
	public async Task PackageOperationCopies_AreOffWhenTheirCountIsZero()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root, packageKeep: 0);
		try
		{
			await db.Migrate();

			await Assert.That(backups.PackageOperationKeep).IsEqualTo(0);
			await Assert.That((await backups.CreateBeforePackageOperationAsync()).Value).IsTypeOf<Error<string>>();
			await Assert.That(Directory.Exists(Path.Join(root, WorldBackupOptions.PackageOperationDirectory))).IsFalse();
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	[Test]
	public async Task RetentionKeepsTheNewestCopiesAndDeletesTheRest()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root, keep: 2);
		try
		{
			await db.Migrate();

			var names = new List<string>();
			for (var i = 0; i < 4; i++)
			{
				var made = await backups.CreateAsync();
				names.Add(made.Expect<WorldBackup>().Name);
			}

			var kept = backups.List().Select(b => b.Name).ToArray();
			await Assert.That(kept.Length).IsEqualTo(2);
			// List is newest-first, and the two survivors are the last two taken.
			await Assert.That(kept[0]).IsEqualTo(names[3]);
			await Assert.That(kept[1]).IsEqualTo(names[2]);
			await Assert.That(Directory.Exists(Path.Combine(root, names[0]))).IsFalse();
			await Assert.That(Directory.Exists(Path.Combine(root, names[1]))).IsFalse();
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>Keep = 0 would otherwise delete the copy it just made; the floor is one.</summary>
	[Test]
	public async Task RetentionNeverDeletesTheCopyItJustMade()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root, keep: 0);
		try
		{
			await db.Migrate();

			var made = await backups.CreateAsync();

			await Assert.That(Directory.Exists(made.Expect<WorldBackup>().Path)).IsTrue();
			await Assert.That(backups.List().Count).IsEqualTo(1);
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	[Test]
	public async Task LatestPointsAtTheNewestCopy()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();
			await backups.CreateAsync();
			var second = await backups.CreateAsync();

			await Assert.That(ReadLatestPointer(root)).IsEqualTo(second.Expect<WorldBackup>().Name);
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// Only the timestamped directories count. The pointer sitting beside them must not be listed, or
	/// retention would prune a real copy in its place.
	/// </summary>
	[Test]
	public async Task LatestIsNotListedAsABackup()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();
			await backups.CreateAsync();

			await Assert.That(backups.List().Count).IsEqualTo(1);
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// A failed copy must not leave a half-written directory that a snapshot would pick up as a
	/// backup, and must report rather than throw.
	/// </summary>
	[Test]
	public async Task AFailedCopyReportsAnErrorAndLeavesNothingBehind()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();
			// Closing the environment makes the copy routine fail on its next call.
			await db.DisposeAsync();

			var result = await backups.CreateAsync();

			await Assert.That(result.Value).IsTypeOf<Error<string>>();
			await Assert.That(backups.List().Count).IsEqualTo(0);
			var leftovers = Directory.Exists(root) ? Directory.GetDirectories(root) : [];
			await Assert.That(leftovers.Length).IsEqualTo(0);
		}
		finally
		{
			Delete(path);
			Delete(root);
		}
	}

	/// <summary><c>@dbck</c>'s check: the newest copy is opened read-only and every entry of every core table read.</summary>
	[Test]
	public async Task CheckLatestReadsTheNewestCopyThrough()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();
			await backups.CreateAsync();
			var newest = (await backups.CreateAsync()).Expect<WorldBackup>();

			var check = (await backups.CheckLatestAsync()).Expect<WorldBackupCheck>();

			await Assert.That(check.Backup).IsEqualTo(newest);
			await Assert.That(check.Tables).IsEqualTo(Tables.All.Count);
			await Assert.That(check.Entries).IsGreaterThanOrEqualTo(db.Store.Count(Tables.Obj));
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	[Test]
	public async Task CheckLatestWithNoCopyIsAnError()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();

			var error = (await backups.CheckLatestAsync()).Expect<Error<string>>();

			await Assert.That(error.Value).Contains("no backup to check");
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>A copy whose meta pages are gone fails the check with a reason instead of throwing.</summary>
	[Test]
	public async Task CheckLatestReportsACopyLMDBCannotOpen()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await db.Migrate();
			var backup = (await backups.CreateAsync()).Expect<WorldBackup>();
			await using (var file = File.OpenWrite(Path.Join(backup.Path, "data.mdb")))
			{
				await file.WriteAsync(new byte[8192]);
			}

			var error = (await backups.CheckLatestAsync()).Expect<Error<string>>();

			await Assert.That(error.Value).StartsWith($"backup {backup.Name} failed the check: ");
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// A backup root that cannot be created — a full or read-only disk, or a path already taken by a
	/// file — is reported like any other failure. A wizard typing the command gets a message, not an
	/// exception out of the command, and the scheduled run logs it and keeps its schedule.
	/// </summary>
	[Test]
	public async Task AnUnusableBackupRootIsReportedRatherThanThrown()
	{
		var path = TempPath();
		var blocker = TempPath();
		// A file where the backup root's parent directory would have to be.
		await File.WriteAllTextAsync(blocker, "not a directory");
		var (db, backups) = Fixture(path, Path.Combine(blocker, "backup"));
		try
		{
			await db.Migrate();

			var result = await backups.CreateAsync();

			await Assert.That(result.Value).IsTypeOf<Error<string>>();
			await Assert.That(backups.List().Count).IsEqualTo(0);
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			File.Delete(blocker);
		}
	}

	/// <summary>
	/// A provider that cannot copy its own world refuses with its own reason, naming the provider and
	/// what to use instead. The reason is carried per provider rather than stated once, because a
	/// database server the game only talks to is a different situation from one whose support is not
	/// written yet, and one blanket sentence would be wrong about at least one of them.
	/// </summary>
	[Test]
	public async Task AProviderThatCannotCopyItsWorldRefusesWithItsOwnReason()
	{
		IWorldBackupService backups = new UnsupportedWorldBackupService("external",
			"requires external backup tooling");

		var result = (await backups.CreateAsync()).Expect<Error<string>>();

		await Assert.That(backups.IsSupported).IsFalse();
		await Assert.That(result.Value).Contains("external");
		await Assert.That(result.Value).Contains("external backup tooling");
		await Assert.That(backups.UnavailableReason).IsEqualTo(result.Value);
		await Assert.That(backups.List().Count).IsEqualTo(0);
	}

	/// <summary>A provider that can copy its world has no reason to give, and must not invent one.</summary>
	[Test]
	public async Task ASupportedProviderReportsNoUnavailableReason()
	{
		var path = TempPath();
		var root = TempPath();
		var (db, backups) = Fixture(path, root);
		try
		{
			await Assert.That(backups.IsSupported).IsTrue();
			await Assert.That(backups.UnavailableReason).IsEmpty();
		}
		finally
		{
			await db.DisposeAsync();
			Delete(path);
			Delete(root);
		}
	}

	/// <summary>
	/// The default is derived from the world's own path, following the same <c>&lt;path&gt;.suffix</c>
	/// convention as a staging promotion's <c>.previous</c>. Two worlds on one box therefore never
	/// share a backup root — which a fixed name beside them would have let them do, and which under
	/// test would have put every run's copies in one directory in the temp folder.
	/// </summary>
	[Test]
	[Arguments("data/lightning", "data/lightning.backups")]
	[Arguments("data/lightning/", "data/lightning.backups")]
	[Arguments("lightning-data", "lightning-data.backups")]
	public async Task TheDefaultRootIsNamedAfterTheWorldDirectory(string world, string expected)
		=> await Assert.That(WorldBackupOptions.DefaultRootFor(world))
			.IsEqualTo(expected.Replace('/', Path.DirectorySeparatorChar));

	[Test]
	[Arguments("6h", 6 * 3600)]
	[Arguments("30m", 30 * 60)]
	[Arguments("1h30m", 5400)]
	[Arguments("45s", 45)]
	[Arguments("2d", 2 * 86400)]
	[Arguments("900", 900)]
	[Arguments("0", 0)]
	[Arguments("", 0)]
	[Arguments(null, 0)]
	public async Task AnIntervalSettingParsesToItsDuration(string? setting, int seconds)
	{
		await Assert.That(WorldBackupOptions.TryParseInterval(setting, out var interval)).IsTrue();
		await Assert.That(interval).IsEqualTo(TimeSpan.FromSeconds(seconds));
	}

	[Test]
	[Arguments("soon")]
	[Arguments("6x")]
	[Arguments("-1h")]
	// An interval no deployment means, and one that would overflow into a negative TimeSpan that
	// PeriodicTimer rejects — which would take the whole host down at startup rather than the setting.
	[Arguments("999999999999d")]
	[Arguments("99999999999999999999")]
	[Arguments("400d")]
	public async Task AnUnreadableIntervalSettingIsRejectedAndLeavesSchedulingOff(string setting)
	{
		await Assert.That(WorldBackupOptions.TryParseInterval(setting, out var interval)).IsFalse();
		await Assert.That(interval).IsEqualTo(TimeSpan.Zero);
	}

	/// <summary>
	/// The snapshot tool is pointed at the backup root from the day the stack comes up, and fails on a
	/// path that does not exist. Nothing has been copied yet on that first night, so the directory has
	/// to be there regardless — including when no interval is configured and the schedule itself never
	/// runs.
	/// </summary>
	[Test]
	public async Task TheScheduleCreatesTheBackupRootEvenWithSchedulingOff()
	{
		var root = TempPath();
		var backups = Substitute.For<IWorldBackupService>();
		backups.IsSupported.Returns(true);
		backups.Root.Returns(root);
		backups.ScheduledInterval.Returns(TimeSpan.Zero);
		var schedule = new WorldBackupScheduleService(backups, Substitute.For<IBackupRunner>(), Substitute.For<TimeProvider>(),
			NullLogger<WorldBackupScheduleService>.Instance);
		try
		{
			await schedule.StartAsync(CancellationToken.None);

			await Assert.That(await Eventually(() => Directory.Exists(root))).IsTrue();
			// Scheduling is off, so it must not have taken one.
			await backups.DidNotReceive().CreateAsync(Arg.Any<CancellationToken>());
		}
		finally
		{
			await schedule.StopAsync(CancellationToken.None);
			Delete(root);
		}
	}

	/// <summary>
	/// The schedule over a real <see cref="BackupRunner"/>: one copy each interval, and a failed copy does not stop the
	/// next. Driven through <see cref="WorldBackupScheduleService.TickAsync"/>, so no test waits on the clock.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task TheScheduleTakesACopyEveryIntervalWhetherOrNotTheLastFailed(bool succeed)
	{
		var backups = Substitute.For<IWorldBackupService>();
		backups.CreateAsync(Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<Result<WorldBackup>>(succeed
			? new WorldBackup("20260101-000000-000", "/backups/20260101-000000-000", DateTimeOffset.UnixEpoch, 0)
			: new Error<string>("disk full")));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var runner = new BackupRunner(backups, Substitute.For<IEventService>(), Substitute.For<IGameBroadcastService>(), options,
			NullLogger<BackupRunner>.Instance);
		var schedule = new WorldBackupScheduleService(backups, runner, Substitute.For<TimeProvider>(),
			NullLogger<WorldBackupScheduleService>.Instance);
		var interval = TimeSpan.FromHours(6);

		await schedule.TickAsync(DateTimeOffset.UnixEpoch, interval);
		await schedule.TickAsync(DateTimeOffset.UnixEpoch + interval, interval);
		await schedule.TickAsync(DateTimeOffset.UnixEpoch + interval * 2, interval);

		await backups.Received(2).CreateAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// A hosted service starts on its own schedule, so a test must wait for what it does rather than
	/// assume it has already happened when <c>StartAsync</c> returns.
	/// </summary>
	private static async Task<bool> Eventually(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (condition()) return true;
			await Task.Delay(20);
		}

		return condition();
	}

	/// <summary>
	/// Reads the <c>latest</c> pointer whichever way the platform allowed it to be written: a
	/// directory symlink where symlinks are permitted, a <c>latest.txt</c> naming the directory
	/// where they are not.
	/// </summary>
	private static string? ReadLatestPointer(string root)
	{
		var link = new DirectoryInfo(Path.Combine(root, "latest"));
		if (link.Exists && link.LinkTarget is not null)
		{
			return Path.GetFileName(link.LinkTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}

		var fallback = Path.Combine(root, "latest.txt");
		return File.Exists(fallback) ? File.ReadAllText(fallback).Trim() : null;
	}
}
