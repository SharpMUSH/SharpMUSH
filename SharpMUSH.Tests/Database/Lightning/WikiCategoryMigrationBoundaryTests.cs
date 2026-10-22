using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class WikiCategoryMigrationTests
{
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(6)]
	[Arguments(7)]
	[Arguments(8)]
	[Arguments(11)]
	[Arguments(12)]
	[Arguments(13)]
	public Task QueuedCoordinatorCancellationPreservesCommittedBoundaryAndReopens(int prefixSteps)
		=> RunMigrationBoundary(prefixSteps, faultAfterWrites: false);

	[Test]
	[Arguments(1)]
	[Arguments(6)]
	[Arguments(8)]
	[Arguments(11)]
	[Arguments(12)]
	public Task PostWriteFaultAtUncoveredBoundaryRollsBackAndRetryReopens(int prefixSteps)
		=> RunMigrationBoundary(prefixSteps, faultAfterWrites: true);

	private async Task RunMigrationBoundary(int prefixSteps, bool faultAfterWrites)
	{
		var path = NewPath();
		var fullPath = Path.GetFullPath(path);
		Check(!fullPath.StartsWith("/tmp/", StringComparison.Ordinal) && !fullPath.StartsWith("/var/tmp/", StringComparison.Ordinal),
			"Migration boundary fixture requires disk-backed scratch.");
		var failures = new List<Exception>();
		var producers = new List<Task>();
		using var lifetime = new CancellationTokenSource();
		using var cancellation = new CancellationTokenSource();
		// Deadlines bound test waits only. Product cancellation is always the explicit CTS above.
		using var mainDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
		LightningDatabase? db = null;
		Driver? driver = null;
		Task? closing = null;
		var providerClosed = false;
		var source = RawPage(["a", "A", "b", "c", "d", "e"]);
		byte[] emptySource = "{}"u8.ToArray();
		var options = Options(ops: 1);

		async Task Observe(Task producer)
		{
			producers.Add(producer);
			await producer.WaitAsync(mainDeadline.Token);
		}
		async Task CloseCurrent()
		{
			Check(producers.All(task => task.IsCompleted), "Boundary producer remains active before close.");
			driver?.Dispose();
			driver = null;
			var current = db ?? throw new InvalidOperationException("Missing boundary provider.");
			closing = BeginBoundaryDispose(current);
			await closing.WaitAsync(mainDeadline.Token);
			db = null;
			providerClosed = true;
		}
		try
		{
			db = Open(path);
			await Observe(db.Store.WriteAsync(tx =>
			{
				for (var i = 1; i <= 5; i++) tx.Put(Tables.WikiByCategory, new byte[] { (byte)i }, new byte[] { 0, 65 });
				tx.Put(Tables.WikiPage, Keys.Dbref(0), source);
				tx.Put(Tables.WikiPage, Keys.Dbref(1), emptySource);
			}, lifetime.Token).AsTask());
			driver = new Driver(db);
			AssertBoundaryPrefix(db, 0, source, emptySource);
			for (var step = 1; step <= prefixSteps; step++)
			{
				var commits = db.Store.CommitCount;
				await Observe(driver.Step(options, ct: lifetime.Token));
				Check(db.Store.CommitCount == commits + 1, "Preparation step did not commit once.");
				AssertBoundaryPrefix(db, step, source, emptySource);
			}
			var before = Snapshot(db.Store);
			var beforeCommits = db.Store.CommitCount;
			if (faultAfterWrites)
			{
				var failed = driver.Step(options, abortAfterWrites: true, ct: lifetime.Token);
				producers.Add(failed);
				var error = await Capture(() => failed.WaitAsync(mainDeadline.Token));
				Check(error is AbortBatch, "Expected dedicated post-write callback fault.");
				Check(beforeCommits == db.Store.CommitCount && before.SequenceEqual(Snapshot(db.Store)), "Post-write fault changed the durable boundary.");
				Check(driver.LastBudget!.WorkingBytes == 0, "Post-write fault retained candidate or temporary budget.");
				AssertBoundaryPrefix(db, prefixSteps, source, emptySource);
				await Observe(driver.Step(options, ct: lifetime.Token));
				Check(db.Store.CommitCount == beforeCommits + 1, "Successful retry did not commit once.");
				AssertBoundaryPrefix(db, prefixSteps + 1, source, emptySource);
			}
			else
			{
				// The preparer owns no producer after Step. Release its expected owner before a
				// separately owned genuine coordinator captures the persisted expected authority.
				driver.Dispose();
				driver = null;
				db.Store.PauseWriter();
				var queued = db.MigrateWikiCategoryValuesAsync(options, cancellation.Token).AsTask();
				producers.Add(queued);
				Check(!queued.IsCompleted, "Actual category coordinator did not queue after preflight.");
				cancellation.Cancel();
				db.Store.ResumeWriter();
				var error = await Capture(() => queued.WaitAsync(mainDeadline.Token));
				Check(error is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token,
					"Queued coordinator cancellation lost its actual caller token.");
				Check(beforeCommits == db.Store.CommitCount && before.SequenceEqual(Snapshot(db.Store)), "Queued cancellation advanced the committed boundary.");
				AssertBoundaryPrefix(db, prefixSteps, source, emptySource);
			}
			var currentProvider = db;
			var unavailable = await Capture(async () =>
				await currentProvider.GetPagesByCategoryAsync("a", 0, 10, WikiVisibility.All).WaitAsync(mainDeadline.Token));
			Check(unavailable is InvalidOperationException { Message: "Wiki category migration is not ready." }, "Incomplete boundary became ready.");
			var persisted = Snapshot(db.Store);
			var persistedPrefix = faultAfterWrites ? prefixSteps + 1 : prefixSteps;
			await CloseCurrent();
			Check(File.Exists(Path.Join(path, "data.mdb")), "Closed boundary world missing.");
			providerClosed = false; // Constructor failure cannot establish native ownership closure.
			db = Open(path);
			closing = null;
			Check(persisted.SequenceEqual(Snapshot(db.Store)), "Reopen changed committed raw boundary before admission.");
			AssertBoundaryPrefix(db, persistedPrefix, source, emptySource);
			await Observe(db.MigrateWikiCategoryValuesAsync(options, lifetime.Token).AsTask());
			await AssertBoundaryComplete(db, source, emptySource);
			Console.WriteLine($"MIGRATION_BOUNDARY prefix={prefixSteps} fault={faultAfterWrites} persistedPrefix={persistedPrefix} reopened=true complete=true");
		}
		catch (Exception error) { failures.Add(error); }
		finally
		{
			using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
			try { lifetime.Cancel(); }
			catch (Exception error) { failures.Add(error); }
			try { cancellation.Cancel(); }
			catch (Exception error) { failures.Add(error); }
			void Resume()
			{
				if (db is null || closing is not null) return;
				try { db.Store.ResumeWriter(); }
				catch (Exception error) { failures.Add(error); }
			}
			Resume();
			foreach (var producer in producers.AsEnumerable().Reverse())
			{
				try { await producer.WaitAsync(cleanupDeadline.Token); }
				catch (Exception error)
				{
					if (!producer.IsCompleted) failures.Add(error);
					_ = producer.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
						TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
				}
				finally { Resume(); }
			}
			var producersDone = producers.All(task => task.IsCompleted);
			if (producersDone)
			{
				try { driver?.Dispose(); driver = null; }
				catch (Exception error) { failures.Add(error); }
				if (db is not null)
				{
					try
					{
						Resume();
						closing ??= BeginBoundaryDispose(db);
						await closing.WaitAsync(cleanupDeadline.Token);
						providerClosed = true;
					}
					catch (Exception error) { failures.Add(error); }
				}
			}
			if (!producersDone || !providerClosed)
			{
				// This partial shares the existing After(Test) cleanup. Remove only this retained
				// world from its list so that hook cannot delete active/unconfirmed native ownership.
				_paths.Remove(path);
				failures.Add(new InvalidOperationException($"Boundary ownership not confirmed closed; retaining {path}."));
			}
		}
		if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
		if (failures.Count > 1) throw new AggregateException(failures);
	}

	private static Task BeginBoundaryDispose(LightningDatabase database)
	{
		var closing = Task.Run(async () => await database.DisposeAsync());
		_ = closing.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		return closing;
	}

	private static void AssertBoundaryPrefix(LightningDatabase db, int steps, byte[] source, byte[] empty)
	{
		Check(steps is >= 0 and <= 13, "Unsupported boundary prefix.");
		var cleared = Math.Clamp(steps - 1, 0, 5);
		var puts = Math.Clamp(steps - 7, 0, 5);
		var completed = steps == 13 ? 2UL : steps >= 12 ? 1UL : 0UL;
		db.Store.Read(tx =>
		{
			Check(tx.Count(Tables.WikiPage) == 2 && tx.TryGet(Tables.WikiPage, Keys.Dbref(0), out var actual)
				&& actual.SequenceEqual(source) && tx.TryGet(Tables.WikiPage, Keys.Dbref(1), out var other)
				&& other.SequenceEqual(empty), "Boundary changed authoritative raw sources.");
			Check(!tx.TryGet(Tables.Meta, MarkerKey, out _), "Premature completion marker at boundary.");
			if (steps == 0) Check(!tx.TryGet(Tables.Meta, StateKey, out _), "Absent prefix has a checkpoint.");
			else
			{
				if (!tx.TryGet(Tables.Meta, StateKey, out var raw)) throw new InvalidOperationException("Missing prefix checkpoint.");
				var state = WikiCategoryCheckpointCodec.Decode(raw, db.Store.MaxKeySize);
				Check(state.Phase == (steps <= 6 ? WikiCategoryMigrationPhase.Clearing : WikiCategoryMigrationPhase.Building)
					&& state.Revision == (ulong)steps && state.ClearedRows == (ulong)cleared && state.CompletedPages == completed
					&& state.CategoryPuts == (ulong)puts && state.CompletionUnixMs == 0, "Actual checkpoint differs from selected prefix.");
				Check(state.LastClearedKey.SequenceEqual(steps is >= 2 and <= 6 ? new byte[] { (byte)cleared } : []), "Wrong deleted-key resume cursor.");
				var partial = steps is >= 8 and <= 11;
				Check(state.CurrentSourceKey.SequenceEqual(partial ? Keys.Dbref(0) : [])
					&& state.CurrentSourceHash.SequenceEqual(partial ? SHA256.HashData(source) : [])
					&& state.NextCategoryOrdinal == (partial ? (ulong)puts : 0), "Wrong actual current-page/hash/distinct ordinal.");
				Check(state.LastCompletedSourceKey.SequenceEqual(completed == 0 ? [] : Keys.Dbref(completed == 1 ? 0 : 1)), "Wrong completed-page cursor including id zero.");
			}
			var expected = new Dictionary<string, string>(StringComparer.Ordinal);
			for (var i = cleared + 1; i <= 5; i++) expected.Add(Convert.ToHexString(new byte[] { (byte)i }), "0041");
			for (var i = 0; i < puts; i++)
			{
				var category = ((char)('a' + i)).ToString();
				expected.Add(Convert.ToHexString(CategoryKey(category, "T", 0)), Convert.ToHexString(CategoryValue(category, "A")));
			}
			var rows = tx.Range(Tables.WikiByCategory, []).ToArray();
			Check(rows.Length == expected.Count && rows.All(row => expected.TryGetValue(Convert.ToHexString(row.Key), out var value)
				&& value == Convert.ToHexString(row.Value)), "Actual obsolete/rebuilt category rows differ at boundary.");
			return true;
		});
	}

	private static async Task AssertBoundaryComplete(LightningDatabase db, byte[] source, byte[] emptySource)
	{
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Revision: 14, Cleared: 5, Pages: 2, Puts: 5, Ordinal: 0 }
			&& State(db.Store).Current.Length == 0 && State(db.Store).Completed.AsSpan().SequenceEqual(Keys.Dbref(1)), "Resume did not finish exact two-page fixture.");
		AssertAuthority(db.Store);
		db.Store.Read(tx =>
		{
			Check(tx.Count(Tables.WikiPage) == 2 && tx.TryGet(Tables.WikiPage, Keys.Dbref(0), out var actual)
				&& actual.SequenceEqual(source) && tx.TryGet(Tables.WikiPage, Keys.Dbref(1), out var empty)
				&& empty.SequenceEqual(emptySource), "Resume changed source bytes.");
			Check(tx.Count(Tables.WikiByCategory) == 5, "Resume skipped/duplicated memberships or indexed empty page.");
			foreach (var category in new[] { "a", "b", "c", "d", "e" })
				Check(tx.TryGet(Tables.WikiByCategory, CategoryKey(category, "T", 0), out var value)
					&& value.SequenceEqual(CategoryValue(category, "A")), "Resume produced wrong raw membership.");
			return true;
		});
		foreach (var category in new[] { "a", "b", "c", "d", "e" })
		{
			var pages = await db.GetPagesByCategoryAsync(category, 0, 10, WikiVisibility.All);
			Check(pages.Count == 1 && pages[0].Id == "wiki_page/0" && pages[0].Title == "T" && pages[0].AuthorDbref == "A"
				&& !pages[0].Published && pages[0].Categories.SequenceEqual(new[] { "a", "A", "b", "c", "d", "e" }), "Wrong completed API membership.");
			Check(await db.CountPagesInCategoryAsync(category, WikiVisibility.All) == 1, "Wrong completed scalar membership count.");
		}
		var counts = await db.CountPagesByCategoryAsync(WikiVisibility.All);
		Check(counts.Count == 5 && new[] { "a", "b", "c", "d", "e" }.All(category => counts.TryGetValue(category, out var count) && count == 1), "Wrong completed dictionary memberships.");
		var emptyPage = await db.GetPageByIdAsync("wiki_page/1");
		Check(emptyPage is WikiPage page && page.Title == "" && page.AuthorDbref == "" && page.Categories.Count == 0
			&& page.Published && page.RevisionNumber == 1 && page.SourceLocale == "", "Literal empty record did not retain ordinary API defaults.");
	}
}
