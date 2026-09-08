using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OneOf;
using OneOf.Types;
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
		int keep = 2, bool compact = true)
	{
		// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
		var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(),
			relations: null);
		var backups = new LightningWorldBackupService(db,
			new WorldBackupOptions { Root = root, Keep = keep }, compact,
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

			await Assert.That(result.IsT0).IsTrue();
			var backup = result.AsT0;
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

			var result = await backups.CreateAsync();

			await Assert.That(result.IsT0).IsTrue();
			using var copy = new LightningStore(new LightningStoreOptions
			{
				Path = result.AsT0.Path,
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

			var result = await backups.CreateAsync();
			await Assert.That(result.IsT0).IsTrue();

			await db.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("after"), Keys.Str("1")));

			using var copy = new LightningStore(new LightningStoreOptions
			{
				Path = result.AsT0.Path,
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
				await Assert.That(made.IsT0).IsTrue();
				names.Add(made.AsT0.Name);
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

			await Assert.That(made.IsT0).IsTrue();
			await Assert.That(Directory.Exists(made.AsT0.Path)).IsTrue();
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

			await Assert.That(second.IsT0).IsTrue();
			await Assert.That(ReadLatestPointer(root)).IsEqualTo(second.AsT0.Name);
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

			await Assert.That(result.IsT1).IsTrue();
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

			await Assert.That(result.IsT1).IsTrue();
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

		var result = await backups.CreateAsync();

		await Assert.That(backups.IsSupported).IsFalse();
		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("external");
		await Assert.That(result.AsT1.Value).Contains("external backup tooling");
		await Assert.That(backups.UnavailableReason).IsEqualTo(result.AsT1.Value);
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
		var schedule = new WorldBackupScheduleService(backups, NullLogger<WorldBackupScheduleService>.Instance);
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

	/// <summary>The 03:30 snapshot only finds a fresh copy if the interval actually fires one.</summary>
	[Test]
	public async Task TheScheduleTakesACopyEveryInterval()
	{
		var root = TempPath();
		var taken = 0;
		var backups = Substitute.For<IWorldBackupService>();
		backups.IsSupported.Returns(true);
		backups.Root.Returns(root);
		backups.ScheduledInterval.Returns(TimeSpan.FromMilliseconds(200));
		backups.CreateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			Interlocked.Increment(ref taken);
			return new ValueTask<OneOf<WorldBackup, Error<string>>>(
				new WorldBackup("20260101-000000-000", Path.Combine(root, "20260101-000000-000"),
					DateTimeOffset.UnixEpoch, 0));
		});
		var schedule = new WorldBackupScheduleService(backups, NullLogger<WorldBackupScheduleService>.Instance);
		try
		{
			await schedule.StartAsync(CancellationToken.None);

			await Assert.That(await Eventually(() => Volatile.Read(ref taken) >= 2)).IsTrue();
		}
		finally
		{
			await schedule.StopAsync(CancellationToken.None);
			Delete(root);
		}
	}

	/// <summary>A failed copy is reported and the schedule carries on to the next interval.</summary>
	[Test]
	public async Task TheScheduleSurvivesAFailedCopy()
	{
		var root = TempPath();
		var attempts = 0;
		var backups = Substitute.For<IWorldBackupService>();
		backups.IsSupported.Returns(true);
		backups.Root.Returns(root);
		backups.ScheduledInterval.Returns(TimeSpan.FromMilliseconds(200));
		backups.CreateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			Interlocked.Increment(ref attempts);
			return new ValueTask<OneOf<WorldBackup, Error<string>>>(new Error<string>("disk full"));
		});
		var schedule = new WorldBackupScheduleService(backups, NullLogger<WorldBackupScheduleService>.Instance);
		try
		{
			await schedule.StartAsync(CancellationToken.None);

			await Assert.That(await Eventually(() => Volatile.Read(ref attempts) >= 2)).IsTrue();
		}
		finally
		{
			await schedule.StopAsync(CancellationToken.None);
			Delete(root);
		}
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
