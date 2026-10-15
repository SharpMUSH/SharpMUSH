using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Activity days against a real LMDB world: merges add up, a window reads only its own days, the last pose
/// only moves forward, and age retention drops whole days but keeps the last poses.
/// </summary>
public class ActivityStoreTests
{
	private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
	private static readonly int Today = DateOnly.FromDateTime(Noon.UtcDateTime).DayNumber;

	private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private static async Task WithDatabaseAsync(Func<LightningDatabase, Task> body)
	{
		var path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 64L << 20 }, Substitute.For<IPasswordService>(),
			relations: null);
		try
		{
			await body(db);
		}
		finally
		{
			await db.DisposeAsync();
			try
			{
				if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
			}
		}
	}

	private static ActivityBatch Batch(int day, long player, long room, int poses, int hours, long lastMs)
		=> new([new ActivityPlayerDay(day, player, poses, hours)], [new ActivityRoomDay(day, room, poses, 1, [player])],
			new Dictionary<long, long> { [player] = lastMs, [room] = lastMs });

	[Test]
	public async Task MergesAddUpAndUnion() => await WithDatabaseAsync(async db =>
	{
		await db.MergeActivityAsync(Batch(Today, 7, 3, 2, 1 << 20, 100));
		await db.MergeActivityAsync(Batch(Today, 7, 3, 5, 1 << 21, 200));
		await db.MergeActivityAsync(Batch(Today, 8, 3, 1, 1 << 9, 150));

		var window = await db.GetActivityAsync(Today, Today);

		var seven = window.Players.Single(p => p.Player == 7);
		await Assert.That(seven.Poses).IsEqualTo(7);
		await Assert.That(seven.Hours).IsEqualTo((1 << 20) | (1 << 21));
		var room = window.Rooms.Single();
		await Assert.That(room.Poses).IsEqualTo(8);
		await Assert.That(room.Scenes).IsEqualTo(3);
		await Assert.That(room.Posers).IsEquivalentTo(new long[] { 7, 8 });
		await Assert.That(window.LastPoses[7]).IsEqualTo(200);
		await Assert.That(window.LastPoses[3]).IsEqualTo(200);
	});

	[Test]
	public async Task AWindowReadsOnlyItsDaysAndTheLastPoseNeverGoesBack() => await WithDatabaseAsync(async db =>
	{
		await db.MergeActivityAsync(Batch(Today - 10, 7, 3, 4, 1, 500));
		await db.MergeActivityAsync(Batch(Today - 1, 7, 3, 1, 1, 400));
		await db.MergeActivityAsync(Batch(Today + 1, 7, 3, 9, 1, 300));

		var window = await db.GetActivityAsync(Today - 5, Today);

		await Assert.That(window.Players.Select(p => p.Day)).IsEquivalentTo(new[] { Today - 1 });
		await Assert.That(window.Rooms.Select(r => r.Day)).IsEquivalentTo(new[] { Today - 1 });
		await Assert.That(window.LastPoses[7]).IsEqualTo(500);
	});

	[Test]
	public async Task RetentionDropsOldDaysAndKeepsLastPoses() => await WithDatabaseAsync(async db =>
	{
		await db.MergeActivityAsync(Batch(Today - 40, 7, 3, 4, 1, 1000));
		await db.MergeActivityAsync(Batch(Today - 29, 8, 3, 2, 1, 2000));

		var retention = new HistoryRetentionService([db.ActivityHistory],
			new HistoryRetentionOptions
			{
				Rules = new Dictionary<string, HistoryRetentionRule> { ["activity"] = new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(30) } },
				BatchSize = 1,
				ArchivePath = ""
			},
			NullLogger<HistoryRetentionService>.Instance,
			new FrozenClock(Noon));

		await retention.PurgeAsync();

		var window = await db.GetActivityAsync(Today - 100, Today);
		await Assert.That(window.Players.Select(p => p.Player)).IsEquivalentTo(new long[] { 8 });
		await Assert.That(window.Rooms.Select(r => r.Day)).IsEquivalentTo(new[] { Today - 29 });
		// A drifter's last pose outlives their days.
		await Assert.That(window.LastPoses[7]).IsEqualTo(1000);
	});

	/// <summary>
	/// A large game's flushes: 3,000 characters over 120 days, one batch a day, then a 90-day window read back
	/// whole. Both must be quick, since a report reads the window every time it runs.
	/// </summary>
	[Test]
	public async Task ALargeWindowWritesAndReadsQuickly() => await WithDatabaseAsync(async db =>
	{
		var watch = System.Diagnostics.Stopwatch.StartNew();
		for (var back = 119; back >= 0; back--)
		{
			var day = Today - back;
			var players = Enumerable.Range(1000, 3000).Select(p => new ActivityPlayerDay(day, p, 3, 1 << (p % 24))).ToList();
			var rooms = Enumerable.Range(10, 300).Select(r => new ActivityRoomDay(day, r, 30, 1, [1000L + r, 2000L + r])).ToList();
			await db.MergeActivityAsync(new ActivityBatch(players, rooms, new Dictionary<long, long> { [1000] = day }));
		}

		var written = watch.Elapsed;
		watch.Restart();
		var window = await db.GetActivityAsync(Today - 89, Today);
		var read = watch.Elapsed;

		await Assert.That(window.Players.Count).IsEqualTo(90 * 3000);
		await Assert.That(window.Rooms.Count).IsEqualTo(90 * 300);
		await Assert.That(written).IsLessThan(TimeSpan.FromSeconds(30)).Because($"120 daily flushes took {written}");
		await Assert.That(read).IsLessThan(TimeSpan.FromSeconds(5)).Because($"reading 90 days took {read}");
	});
}
