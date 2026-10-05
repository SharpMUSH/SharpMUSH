using SharpMUSH.Tests.Wiki;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Retention over the wiki's revision streams (#1464), against a real LMDB world: what a policy purges
/// is gone from the live table, and what it must keep — the latest revision of every stream, the newest
/// N, anything younger than the age bound, anything on a protected page — survives.
/// </summary>
public class WikiHistoryRetentionTests
{
	/// <summary>The pass's clock. A page's first revision is stamped on the wall clock at creation, just after this.</summary>
	private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

	private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private static async Task WithDatabaseAsync(Func<LightningDatabase, IWikiService, Task> body)
	{
		var path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
		var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(),
			relations: null);
		try
		{
			await body(db, new WikiStoreService(db, new WikiMarkdigPipeline()));
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

	private static HistoryRetentionService Retention(LightningDatabase db, HistoryRetentionRule rule, int batch = 256,
		string archive = "")
		=> new([db.WikiHistory],
			new HistoryRetentionOptions
			{
				Rules = new Dictionary<string, HistoryRetentionRule> { ["wiki"] = rule },
				BatchSize = batch,
				ArchivePath = archive
			},
			NullLogger<HistoryRetentionService>.Instance,
			new FrozenClock(Now));

	/// <summary>A page with <paramref name="revisions"/> revisions, the n-th written <c>revisions - n</c> days before <see cref="Now"/>.</summary>
	private static async Task<string> PageWithRevisionsAsync(LightningDatabase db, IWikiService wiki, string title, int revisions)
	{
		var page = (await wiki.CreateAsync(title, "r1", "#1")).Expect<WikiPage>();
		for (var n = 2; n <= revisions; n++)
		{
			(await db.UpdatePageBodyAsync(page.Id, new WikiBody($"r{n}", $"<p>r{n}</p>", $"r{n}"), "#1", null,
				Now.AddDays(n - revisions))).Expect<WikiPage>();
		}

		return page.Id;
	}

	/// <summary>The revision numbers physically present in the live table for the page's source stream.</summary>
	private static IReadOnlyList<int> StoredRevisions(LightningDatabase db, string pageId)
		=> db.Store.Read(tx => tx.Range(Tables.WikiRev, Keys.Concat(Keys.Str(pageId), Keys.Sep, Keys.Sep))
			.Select(row => (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(row.Key.AsSpan(^4)))
			.ToList());

	[Test]
	public async Task TheDefaultPolicyKeepsEveryRevision() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var id = await PageWithRevisionsAsync(db, wiki, "Kept Forever", 6);

		var outcome = (await Retention(db, HistoryRetentionRule.KeepEverything).PurgeAsync()).Single();

		await Assert.That(outcome.Value).IsTypeOf<HistoryKeptEverything>();
		await Assert.That(StoredRevisions(db, id)).IsEquivalentTo([1, 2, 3, 4, 5, 6]);
	});

	/// <summary>
	/// Keep the newest two: the rest are purged from the table itself, the service answers NotFound for
	/// them (so a rollback to one is refused), the page still shows its latest text, and the next edit is
	/// numbered on from the page — never reusing a purged number.
	/// </summary>
	[Test]
	public async Task KeepNewestPurgesTheOlderRevisionsFromTheLiveTable() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var id = await PageWithRevisionsAsync(db, wiki, "Bounded Page", 6);

		var purged = (await Retention(db, new HistoryRetentionRule { KeepNewest = 2 }).PurgeAsync()).Single()
			.Expect<HistoryPurged>();

		await Assert.That(purged.Records).IsEqualTo(4);
		await Assert.That(purged.Bytes).IsGreaterThan(0);
		await Assert.That(StoredRevisions(db, id)).IsEquivalentTo([5, 6]);
		await Assert.That((await wiki.GetRevisionAsync(id, 3)).Value).IsTypeOf<NotFound>();
		await Assert.That((await wiki.GetRevisionAsync(id, 5)).Expect<WikiRevision>().MarkdownSource).IsEqualTo("r5");
		await Assert.That((await wiki.GetByIdAsync(id)).Expect<WikiPage>().MarkdownSource).IsEqualTo("r6");

		var edited = (await wiki.UpdateAsync(id, "r7", "#1")).Expect<WikiPage>();
		await Assert.That(edited.RevisionNumber).IsEqualTo(7);
		await Assert.That(StoredRevisions(db, id)).IsEquivalentTo([5, 6, 7]);
	});

	/// <summary>
	/// Age alone: what is older than the bound goes, what is younger stays, and the latest revision stays
	/// however old it is — it is the page.
	/// </summary>
	[Test]
	public async Task MaxAgePurgesOnlyOldRevisionsAndNeverTheLatest() => await WithDatabaseAsync(async (db, wiki) =>
	{
		// Revisions 2..6 are 4, 3, 2, 1 and 0 days old; revision 1 was written at creation, so is new.
		var id = await PageWithRevisionsAsync(db, wiki, "Aged Page", 6);
		var stale = await PageWithRevisionsAsync(db, wiki, "Stale Page", 1);

		var purged = (await Retention(db, new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(2) }).PurgeAsync())
			.Single().Expect<HistoryPurged>();

		await Assert.That(StoredRevisions(db, id)).IsEquivalentTo([1, 5, 6]);
		await Assert.That(StoredRevisions(db, stale)).IsEquivalentTo([1]);
		await Assert.That(purged.Records).IsEqualTo(3);
	});

	/// <summary>Protection is how an admin says a page's history is not to be touched.</summary>
	[Test]
	public async Task AProtectedPageKeepsItsWholeHistory() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var shielded = await PageWithRevisionsAsync(db, wiki, "Shielded Page", 5);
		var open = await PageWithRevisionsAsync(db, wiki, "Open Page", 5);
		(await wiki.ProtectAsync(shielded, true)).Expect<None>();

		await Retention(db, new HistoryRetentionRule { KeepNewest = 1 }).PurgeAsync();

		await Assert.That(StoredRevisions(db, shielded)).IsEquivalentTo([1, 2, 3, 4, 5]);
		await Assert.That(StoredRevisions(db, open)).IsEquivalentTo([5]);
	});

	/// <summary>A translation numbers its revisions on its own, and is bounded on its own.</summary>
	[Test]
	public async Task EachTranslationIsItsOwnStream() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var id = await PageWithRevisionsAsync(db, wiki, "Translated Page", 3);
		var expected = (int?)null;
		for (var n = 1; n <= 4; n++)
		{
			var written = await wiki.UpsertTranslationAsync(id, "fr", "Page", $"fr{n}", "#1", null, true, expected);
			expected = written.Expect<WikiTranslation>().RevisionNumber;
		}

		await Retention(db, new HistoryRetentionRule { KeepNewest = 2 }).PurgeAsync();

		await Assert.That(StoredRevisions(db, id)).IsEquivalentTo([2, 3]);
		var french = await wiki.GetRevisionsForLocaleAsync(id, "fr", 0, 10);
		await Assert.That(french.Select(r => r.RevisionNumber)).IsEquivalentTo([4, 3]);
		await Assert.That((await wiki.GetTranslationAsync(id, "fr")).Expect<WikiTranslation>().MarkdownSource).IsEqualTo("fr4");
	});

	/// <summary>
	/// A pass is many small write transactions, not one: with a batch of two, ten purgeable revisions take
	/// at least five batches — and the end state is the same as one big pass.
	/// </summary>
	[Test]
	public async Task APassWorksInBoundedBatches() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var first = await PageWithRevisionsAsync(db, wiki, "Batch One", 6);
		var second = await PageWithRevisionsAsync(db, wiki, "Batch Two", 6);

		var purged = (await Retention(db, new HistoryRetentionRule { KeepNewest = 1 }, batch: 2).PurgeAsync()).Single()
			.Expect<HistoryPurged>();

		await Assert.That(purged.Records).IsEqualTo(10);
		await Assert.That(purged.Batches).IsGreaterThanOrEqualTo(5);
		await Assert.That(StoredRevisions(db, first)).IsEquivalentTo([6]);
		await Assert.That(StoredRevisions(db, second)).IsEquivalentTo([6]);
	});

	/// <summary>With an archive configured, every purged revision is in it — written before the delete.</summary>
	[Test]
	public async Task PurgedRevisionsAreArchivedFirst() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var archive = Path.Join(Path.GetTempPath(), "sharpmush-archive-" + Guid.NewGuid().ToString("N"));
		try
		{
			await PageWithRevisionsAsync(db, wiki, "Archived Page", 4);

			await Retention(db, new HistoryRetentionRule { KeepNewest = 1 }, archive: archive).PurgeAsync();

			var lines = Directory.EnumerateFiles(archive, "wiki-*.jsonl").SelectMany(File.ReadAllLines).ToList();
			await Assert.That(lines.Count).IsEqualTo(3);
			foreach (var revision in new[] { "r1", "r2", "r3" })
			{
				await Assert.That(lines.Any(line => line.Contains($"\"MarkdownSource\":\"{revision}\""))).IsTrue();
			}

			await Assert.That(lines.All(line => System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("kind").GetString() == "wiki"))
				.IsTrue();
		}
		finally
		{
			if (Directory.Exists(archive)) Directory.Delete(archive, recursive: true);
		}
	});

	/// <summary>
	/// An archive that cannot be written stops the pass before anything is deleted: a purge that cannot
	/// keep its copy must not throw the record away.
	/// </summary>
	[Test]
	public async Task AnArchiveThatCannotBeWrittenDeletesNothing() => await WithDatabaseAsync(async (db, wiki) =>
	{
		// A file where the archive directory should be.
		var blocked = Path.Join(Path.GetTempPath(), "sharpmush-archive-" + Guid.NewGuid().ToString("N"));
		await File.WriteAllTextAsync(blocked, "not a directory");
		try
		{
			var id = await PageWithRevisionsAsync(db, wiki, "Unarchivable", 4);

			var failed = (await Retention(db, new HistoryRetentionRule { KeepNewest = 1 }, archive: blocked).PurgeAsync())
				.Single().Expect<HistoryPurgeFailed>();

			await Assert.That(failed.Records).IsEqualTo(0);
			await Assert.That(StoredRevisions(db, id)).IsEquivalentTo([1, 2, 3, 4]);
		}
		finally
		{
			File.Delete(blocked);
		}
	});

	[Test]
	public async Task UsageCountsEveryStoredRevision() => await WithDatabaseAsync(async (db, wiki) =>
	{
		await PageWithRevisionsAsync(db, wiki, "Counted One", 3);
		await PageWithRevisionsAsync(db, wiki, "Counted Two", 2);

		var usage = await db.WikiHistory.MeasureAsync();

		await Assert.That(usage.Kind).IsEqualTo("wiki");
		await Assert.That(usage.Holders).IsEqualTo(2);
		await Assert.That(usage.Records).IsEqualTo(5);
		await Assert.That(usage.Bytes).IsGreaterThan(0);
	});
}
