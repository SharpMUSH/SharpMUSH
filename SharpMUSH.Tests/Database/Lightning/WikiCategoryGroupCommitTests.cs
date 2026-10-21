using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryGroupCommitTests
{
	private const string Category = "play\0rules";
	private static readonly byte[] StateKey = "core.wiki.cat.value-upgrade/state"u8.ToArray();
	private static readonly byte[] MarkerKey = "mig:0004_wiki_category_value_v1"u8.ToArray();
	private static readonly byte[] FailedKey = "test.category.group.failed"u8.ToArray();
	private static readonly byte[] KeptKey = "test.category.group.kept"u8.ToArray();
	private static readonly byte[] KeptValue = "kept neighbor"u8.ToArray();
	private sealed class NeighborFailure : Exception;

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SameNativeBatchIsolatesCoordinatorConflictAndThrowingNeighbor(bool failureFirst)
	{
		var configured = Environment.GetEnvironmentVariable("TMPDIR");
		var scratch = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
			? Path.Join(Environment.CurrentDirectory, "work") : configured);
		Check(scratch != "/tmp" && !scratch.StartsWith("/tmp/", StringComparison.Ordinal)
			&& scratch != "/var/tmp" && !scratch.StartsWith("/var/tmp/", StringComparison.Ordinal), "Disk-backed scratch required");
		RejectLinks(scratch);
		var owned = Path.Join(scratch, "category-group-" + Guid.NewGuid().ToString("N"));
		Check(!Directory.Exists(owned) && !File.Exists(owned) && new DirectoryInfo(owned).LinkTarget is null
			&& new FileInfo(owned).LinkTarget is null, "Owned path already exists");
		var world = Path.Join(owned, "world");
		var failures = new List<Exception>();
		var producers = new List<Task>();
		using var cancel = new CancellationTokenSource();
		LightningDatabase? db = null;
		Task? closing = null;
		var producersDone = false;
		var providerClosed = true;
		try
		{
			Directory.CreateDirectory(owned);
			providerClosed = false;
			db = Open(world);
			var source = Codec.Serialize(new WikiPageRecord
			{
				Slug = "guide", Title = "Guide", Namespace = "main", MarkdownSource = "body", RenderedHtml = "<p>body</p>",
				PlainText = "body", AuthorDbref = "#42", LastEditorDbref = "#42", CreatedAt = "2023-11-14T22:13:20.0000000+00:00",
				UpdatedAt = "2023-11-14T22:13:20.0000000+00:00", RevisionNumber = 1,
				Categories = [Category], Published = true, SourceLocale = "en"
			});
			var seed = db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, new byte[8], source), cancel.Token).AsTask();
			producers.Add(seed);
			await seed.WaitAsync(TimeSpan.FromSeconds(30));
			var store = db.Store;
			var before = store.CommitCount;
			store.PauseWriter();
			Task QueueFailure()
			{
				var job = store.WriteAsync(tx =>
				{
					tx.Put(Tables.Meta, FailedKey, "must abort"u8);
					throw new NeighborFailure();
				}, cancel.Token).AsTask();
				producers.Add(job);
				Check(!job.IsCompleted, "Throwing neighbor did not queue while paused");
				return job;
			}
			Task? neighbor = failureFirst ? QueueFailure() : null;
			var winner = db.MigrateWikiCategoryValuesAsync(WikiCategoryMigrationOptions.Parse(_ => null), cancel.Token).AsTask();
			producers.Add(winner);
			Check(!winner.IsCompleted, "Winning coordinator did not queue while paused");
			var loser = db.MigrateWikiCategoryValuesAsync(WikiCategoryMigrationOptions.Parse(_ => null), cancel.Token).AsTask();
			producers.Add(loser);
			Check(!loser.IsCompleted, "Losing coordinator did not queue while paused");
			neighbor ??= QueueFailure();
			var kept = store.WriteAsync(tx =>
			{
				tx.Put(Tables.Meta, KeptKey, KeptValue);
				store.PauseWriter();
			}, cancel.Token).AsTask();
			producers.Add(kept);
			Check(!kept.IsCompleted, "Successful neighbor did not queue while paused");
			store.ResumeWriter();
			var conflict = await Failure(loser);
			var thrown = await Failure(neighbor);
			await kept.WaitAsync(TimeSpan.FromSeconds(30));
			Check(conflict is InvalidDataException { Message: "Wiki category migration checkpoint conflict." }, "Loser did not report the actual fixed checkpoint conflict");
			Check(thrown is NeighborFailure, "Throwing neighbor did not preserve its dedicated exception");
			Check(store.CommitCount == before + 1, "Four-job cohort did not commit exactly once");
			Check(!winner.IsCompleted, "Winner advanced while the next writer turn was paused");
			store.Read(tx =>
			{
				Check(tx.TryGet(Tables.Meta, StateKey, out var state), "Missing committed Clearing checkpoint");
				// Independent exact wire oracle: WCM1/version1/algorithm1/Clearing/revision1, all counters/slots zero.
				var expected = new byte[112];
				"WCM1"u8.CopyTo(expected);
				expected[5] = 1; expected[7] = 1; expected[8] = 1; expected[19] = 1;
				Check(state.SequenceEqual(expected), "Intermediate checkpoint is not exact Clearing revision one");
				Check(!tx.TryGet(Tables.Meta, MarkerKey, out _) && tx.Count(Tables.WikiByCategory) == 0, "Completion or category rows escaped the paused first cohort");
				VerifySourceAndNeighbors(tx, source);
				return true;
			});
			var unready = await Failure(() => db.GetPagesByCategoryAsync(Category, 0, 10, WikiVisibility.All));
			Check(unready is InvalidOperationException { Message: "Wiki category migration is not ready." }, "Category reads became ready before Complete");
			store.ResumeWriter();
			await winner.WaitAsync(TimeSpan.FromSeconds(30));
			ValidateCompleteAndRows(db, source);
			await VerifyMembership(db);
			var durable = Snapshot(db);
			Check(producers.All(task => task.IsCompleted), "Tracked producers still active before closing world");
			closing = StartClosing(db);
			await closing.WaitAsync(TimeSpan.FromSeconds(25));
			db = null;
			providerClosed = true;
			Check(File.Exists(Path.Join(world, "data.mdb")), "Closed durable world missing");
			RejectLinks(Path.Join(world, "data.mdb"));
			providerClosed = false;
			db = Open(world);
			closing = null;
			// Refuse incomplete recovery BEFORE invoking any coordinator: parent cannot heal child output.
			ValidateCompleteAndRows(db, source);
			Check(durable.SequenceEqual(Snapshot(db)), "Reopen changed exact durable source/index/authority/neighbor rows");
			var reopenCommits = db.Store.CommitCount;
			var admission = db.MigrateWikiCategoryValuesAsync(WikiCategoryMigrationOptions.Parse(_ => null), cancel.Token).AsTask();
			producers.Add(admission);
			await admission.WaitAsync(TimeSpan.FromSeconds(30));
			Check(db.Store.CommitCount == reopenCommits && durable.SequenceEqual(Snapshot(db)), "Valid-Complete admission rewrote recovered world");
			await VerifyMembership(db);
			Check(db.Store.CommitCount == reopenCommits && durable.SequenceEqual(Snapshot(db)), "Recovered API verification changed durable world");
			Console.WriteLine($"category-group failureFirst={failureFirst} cohortCommits=1 durableComplete=true");
		}
		catch (Exception error) { failures.Add(error); }
		finally
		{
			using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(40));
			try { cancel.Cancel(); }
			catch (Exception error) { failures.Add(error); }
			void ResumeForCleanup()
			{
				if (db is null || closing is not null) return;
				try { db.Store.ResumeWriter(); }
				catch (Exception error) { failures.Add(error); }
			}
			ResumeForCleanup();
			// The final cohort callback can pause AFTER cleanup's first Resume. Observe it before
			// waiting for the winner, then resume again. Reverse order is fixed, never polling.
			foreach (var producer in producers.AsEnumerable().Reverse())
			{
				try { await producer.WaitAsync(cleanup.Token); }
				catch (Exception error)
				{
					// Expected cohort failures are asserted above; every original task is still observed here.
					if (!producer.IsCompleted) failures.Add(error);
					_ = producer.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
						TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
				}
				finally { ResumeForCleanup(); }
			}
			producersDone = producers.All(task => task.IsCompleted);
			if (!producersDone)
				failures.Add(new InvalidOperationException($"Tracked producer remains active; retaining owned world {world} and scratch {owned}"));
			if (db is not null && producersDone)
			{
				try
				{
					ResumeForCleanup();
					closing ??= StartClosing(db);
					await closing.WaitAsync(cleanup.Token);
					providerClosed = true;
				}
				catch (Exception error) { failures.Add(error); }
			}
			if (producersDone && providerClosed)
			{
				try { RejectLinks(owned); if (Directory.Exists(owned)) Directory.Delete(owned, true); }
				catch (Exception error) { failures.Add(error); }
			}
			else failures.Add(new InvalidOperationException($"Native ownership not confirmed closed; retaining {world} and {owned}"));
		}
		if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
		if (failures.Count > 1) throw new AggregateException(failures);
	}

	private static LightningDatabase Open(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20, MaxBatch = 4, Sync = LightningSyncMode.Full },
		Substitute.For<IPasswordService>(), relations: null);

	private static Task StartClosing(LightningDatabase db)
	{
		var closing = Task.Run(async () => await db.DisposeAsync());
		_ = closing.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		return closing;
	}

	private static async Task<Exception?> Failure(Func<Task> invoke)
	{
		try { await invoke().WaitAsync(TimeSpan.FromSeconds(30)); return null; }
		catch (Exception error) { return error; }
	}

	private static async Task<Exception?> Failure(Task task)
	{
		try { await task.WaitAsync(TimeSpan.FromSeconds(30)); return null; }
		catch (Exception error) { return error; }
	}

	private static void VerifySourceAndNeighbors(ITx tx, byte[] source)
	{
		Check(tx.Count(Tables.WikiPage) == 1 && tx.TryGet(Tables.WikiPage, new byte[8], out var actual)
			&& actual.SequenceEqual(source), "Source rows changed");
		Check(!tx.TryGet(Tables.Meta, FailedKey, out _), "Throwing neighbor's nested transaction survived");
		Check(tx.TryGet(Tables.Meta, KeptKey, out var kept) && kept.SequenceEqual(KeptValue), "Successful neighbor's write missing");
	}

	private static void ValidateCompleteAndRows(LightningDatabase db, byte[] source) => db.Store.Read(tx =>
	{
		VerifySourceAndNeighbors(tx, source);
		Check(tx.TryGet(Tables.Meta, StateKey, out var state) && tx.TryGet(Tables.Meta, MarkerKey, out _), "Missing existing Complete pair");
		if (!tx.TryGet(Tables.Meta, MarkerKey, out var marker)) throw new InvalidDataException("Missing Complete marker");
		Check(WikiCategoryAuthorityCodec.Validate(true, state, true, marker, db.Store.MaxKeySize), "Invalid persisted Complete authority");
		var checkpoint = WikiCategoryCheckpointCodec.Decode(state, db.Store.MaxKeySize);
		Check(checkpoint.Phase == WikiCategoryMigrationPhase.Complete && checkpoint.CompletedPages == 1
			&& checkpoint.CategoryPuts == 1 && checkpoint.ClearedRows == 0 && checkpoint.CompletionUnixMs > 0, "Wrong Complete counters");
		Check(tx.Count(Tables.WikiByCategory) == 1, "Unexpected category row count");
		var row = tx.Range(Tables.WikiByCategory, []).Single();
		var key = "PLAY\0RULES\0"u8.ToArray().Concat(Encoding.BigEndianUnicode.GetBytes("Guide")).Concat(new byte[10]).ToArray();
		byte[] value = [1, 35, 52, 50, 0, 0, 0, 10, 255, 67, 0, 1];
		Check(row.Key.AsSpan().SequenceEqual(key) && row.Value.AsSpan().SequenceEqual(value), "Wrong exact raw category key/value");
		return true;
	});

	private static async Task VerifyMembership(LightningDatabase db)
	{
		var pages = await db.GetPagesByCategoryAsync(Category, 0, 10, WikiVisibility.All);
		Check(pages.Count == 1 && pages[0].Id == "wiki_page/0" && pages[0].Title == "Guide"
			&& pages[0].AuthorDbref == "#42" && pages[0].Published && pages[0].Categories.SequenceEqual(new[] { Category }), "Wrong recovered category membership");
		Check(await db.CountPagesInCategoryAsync(Category, WikiVisibility.All) == 1, "Wrong exact category count");
		Check((await db.GetPagesByCategoryAsync("play", 0, 10, WikiVisibility.All)).Count == 0
			&& await db.CountPagesInCategoryAsync("play", WikiVisibility.All) == 0, "NUL label leaked an alias");
		var counts = await db.CountPagesByCategoryAsync(WikiVisibility.All);
		Check(counts.Count == 1 && counts.TryGetValue(Category, out var count) && count == 1 && !counts.ContainsKey("play"), "Wrong exact category dictionary");
	}

	private static string[] Snapshot(LightningDatabase db) => db.Store.Read(tx =>
	{
		var rows = new List<string>();
		foreach (var table in new[] { Tables.WikiPage, Tables.WikiByCategory })
			foreach (var row in tx.Range(table, [])) rows.Add(table.Name + ":" + Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value));
		foreach (var key in new[] { StateKey, MarkerKey, FailedKey, KeptKey })
			rows.Add(Convert.ToHexString(key) + ":" + (tx.TryGet(Tables.Meta, key, out var value) ? Convert.ToHexString(value) : "absent"));
		return rows.ToArray();
	});

	private static void RejectLinks(string path)
	{
		for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
			Check(new DirectoryInfo(current).LinkTarget is null && new FileInfo(current).LinkTarget is null, "Linked owned path: " + current);
	}

	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidDataException(message);
	}
}
