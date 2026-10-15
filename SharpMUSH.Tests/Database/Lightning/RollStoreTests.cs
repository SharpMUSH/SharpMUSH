using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Recorded rolls against a real LMDB world: ids start at 1 and only grow, a roll reads back as kept,
/// and age retention purges old rolls without their ids ever being handed out again.
/// </summary>
public class RollStoreTests
{
	private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

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

	private static RollDraft Draft(DateTimeOffset at, string text = "1d20[17] = 17", string? by = null)
		=> new(at, "#7:1", "Alice", by, "1d20", 17, text, """{"formula":"1d20","total":17}""");

	[Test]
	public async Task IdsStartAtOneAndGrow() => await WithDatabaseAsync(async db =>
	{
		var first = await db.RecordRollAsync(Draft(Noon));
		var second = await db.RecordRollAsync(Draft(Noon));

		await Assert.That(first.Id).IsEqualTo(1);
		await Assert.That(second.Id).IsEqualTo(2);
	});

	[Test]
	public async Task ARollReadsBackAsKept() => await WithDatabaseAsync(async db =>
	{
		var kept = await db.RecordRollAsync(Draft(Noon, by: "#9:2"));

		var read = (await db.GetRollAsync(kept.Id)).Expect<RecordedRoll>();

		await Assert.That(read).IsEqualTo(kept);
		await Assert.That((await db.GetRollAsync(kept.Id + 1)).Value).IsTypeOf<NotFound>();
		await Assert.That((await db.GetRollAsync(-1)).Value).IsTypeOf<NotFound>();
	});

	[Test]
	public async Task RetentionPurgesOldRollsAndTheirIdsAreNotReused() => await WithDatabaseAsync(async db =>
	{
		var old = await db.RecordRollAsync(Draft(Noon.AddDays(-40), "old"));
		await db.RecordRollAsync(Draft(Noon.AddDays(-31), "older than the bound"));
		var kept = await db.RecordRollAsync(Draft(Noon.AddDays(-29), "kept"));

		var retention = new HistoryRetentionService([db.RollHistory],
			new HistoryRetentionOptions
			{
				Rules = new Dictionary<string, HistoryRetentionRule> { ["rolls"] = new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(30) } },
				BatchSize = 1,
				ArchivePath = ""
			},
			NullLogger<HistoryRetentionService>.Instance,
			new FrozenClock(Noon));

		await retention.PurgeAsync();

		await Assert.That((await db.GetRollAsync(old.Id)).Value).IsTypeOf<NotFound>();
		await Assert.That((await db.GetRollAsync(kept.Id)).Expect<RecordedRoll>().Roll.Text).IsEqualTo("kept");
		await Assert.That((await db.RecordRollAsync(Draft(Noon))).Id).IsEqualTo(kept.Id + 1);
	});
}
