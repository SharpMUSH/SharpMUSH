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
/// The audit log's table (#1565), against a real LMDB world: entries come back newest first, every
/// filter narrows them, a listing continues from its cursor without repeating or skipping an entry, and
/// age retention purges only what is past its age.
/// </summary>
public class AuditStoreTests
{
	private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

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

	private static AuditDraft Draft(DateTimeOffset at, string action, string actor = "wiz", string target = "Alice",
		string? details = null, AuditSource source = AuditSource.Game)
		=> new(at, action, source, new AuditActor("acc", actor, "#1:1", "One"),
			new AuditTarget(AuditTargetKinds.Character, $"#7:{target.Length}", target), details);

	[Test]
	public async Task EntriesComeBackNewestFirst_EvenWithinOneMillisecond() => await WithDatabaseAsync(async db =>
	{
		var first = await db.AppendAuditAsync(Draft(Noon, AuditActions.PlayerBoot));
		var second = await db.AppendAuditAsync(Draft(Noon, AuditActions.PlayerPassword));
		var third = await db.AppendAuditAsync(Draft(Noon.AddSeconds(1), AuditActions.ConfigSet, source: AuditSource.Portal));

		var page = await db.GetAuditEntriesAsync(new AuditFilter());

		await Assert.That(page.Entries.Select(e => e.Id)).IsEquivalentTo([third.Id, second.Id, first.Id],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(page.Next).IsNull();
		await Assert.That(page.Entries[0].Source).IsEqualTo(AuditSource.Portal);
		await Assert.That(page.Entries[0].At).IsEqualTo(Noon.AddSeconds(1));
		await Assert.That(page.Entries[0].Target!.Name).IsEqualTo("Alice");
	});

	[Test]
	public async Task EveryFilterNarrowsTheListing() => await WithDatabaseAsync(async db =>
	{
		await db.AppendAuditAsync(Draft(Noon.AddHours(-2), AuditActions.RoleAssign, actor: "alpha", target: "Bob", details: "builder"));
		await db.AppendAuditAsync(Draft(Noon.AddHours(-1), AuditActions.RoleSave, actor: "beta", target: "builder"));
		await db.AppendAuditAsync(Draft(Noon, AuditActions.PlayerBoot, actor: "alpha", target: "Carol"));

		async Task<IReadOnlyList<string>> Actions(AuditFilter filter)
			=> (await db.GetAuditEntriesAsync(filter)).Entries.Select(e => e.Action).ToList();

		await Assert.That(await Actions(new AuditFilter(Action: "role."))).IsEquivalentTo([AuditActions.RoleSave, AuditActions.RoleAssign]);
		await Assert.That(await Actions(new AuditFilter(Action: AuditActions.RoleSave))).IsEquivalentTo([AuditActions.RoleSave]);
		await Assert.That(await Actions(new AuditFilter(Actor: "ALPHA"))).IsEquivalentTo([AuditActions.PlayerBoot, AuditActions.RoleAssign]);
		await Assert.That(await Actions(new AuditFilter(Text: "builder"))).IsEquivalentTo([AuditActions.RoleSave, AuditActions.RoleAssign])
			.Because("the text matches a target name or the details");
		await Assert.That(await Actions(new AuditFilter(From: Noon.AddMinutes(-90)))).IsEquivalentTo([AuditActions.PlayerBoot, AuditActions.RoleSave]);
		await Assert.That(await Actions(new AuditFilter(To: Noon.AddHours(-1)))).IsEquivalentTo([AuditActions.RoleSave, AuditActions.RoleAssign])
			.Because("the end of the range is inclusive");
	});

	[Test]
	public async Task AListingContinuesFromItsCursor() => await WithDatabaseAsync(async db =>
	{
		var written = new List<string>();
		for (var n = 0; n < 7; n++)
			written.Add((await db.AppendAuditAsync(Draft(Noon.AddMinutes(n), AuditActions.ConfigSet, details: $"n{n}"))).Id);
		written.Reverse();

		var read = new List<string>();
		string? cursor = null;
		var pages = 0;
		do
		{
			var page = await db.GetAuditEntriesAsync(new AuditFilter(Before: cursor, Limit: 3));
			read.AddRange(page.Entries.Select(e => e.Id));
			cursor = page.Next;
			pages++;
		} while (cursor is not null && pages < 10);

		await Assert.That(read).IsEquivalentTo(written, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	});

	[Test]
	public async Task RetentionPurgesOnlyEntriesPastTheirAge() => await WithDatabaseAsync(async db =>
	{
		await db.AppendAuditAsync(Draft(Noon.AddDays(-40), AuditActions.PlayerBoot, details: "old"));
		await db.AppendAuditAsync(Draft(Noon.AddDays(-31), AuditActions.PlayerBoot, details: "older than the bound"));
		await db.AppendAuditAsync(Draft(Noon.AddDays(-29), AuditActions.PlayerBoot, details: "kept"));
		await db.AppendAuditAsync(Draft(Noon, AuditActions.PlayerBoot, details: "today"));

		var retention = new HistoryRetentionService([db.AuditHistory],
			new HistoryRetentionOptions
			{
				Rules = new Dictionary<string, HistoryRetentionRule> { ["audit"] = new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(30) } },
				BatchSize = 1,
				ArchivePath = ""
			},
			NullLogger<HistoryRetentionService>.Instance,
			new FrozenClock(Noon));

		await retention.PurgeAsync();

		var left = (await db.GetAuditEntriesAsync(new AuditFilter())).Entries.Select(e => e.Details ?? string.Empty).ToList();
		await Assert.That(left).IsEquivalentTo(["today", "kept"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	});
}
