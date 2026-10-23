using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class WikiCategoryMigrationTests
{
	private readonly List<string> _paths = [];
	private static readonly byte[] StateKey = "core.wiki.cat.value-upgrade/state"u8.ToArray();
	private static readonly byte[] MarkerKey = "mig:0004_wiki_category_value_v1"u8.ToArray();

	private string NewPath()
	{
		var root = Environment.GetEnvironmentVariable("TMPDIR")
			?? Path.Join(AppContext.BaseDirectory, "work");
		Directory.CreateDirectory(root);
		var path = Path.Join(root, "wiki-category-coordinator-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return path;
	}

	private static LightningDatabase Open(string path, IReadOnlyList<IMigrationSource>? migrationSources = null)
		=> new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 },
		Substitute.For<IPasswordService>(), relations: null, migrationSources: migrationSources);

	[After(Test)]
	public void Cleanup()
	{
		foreach (var path in _paths)
		{
			if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
			Check(!Directory.Exists(path), "Owned native directory was not cleaned up.");
		}
	}

	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static WikiCategoryMigrationOptions Options(int ops = 2, int source = 8388608,
		long working = 268435456, int delta = 262144, int keys = 131072)
		=> WikiCategoryMigrationOptions.Parse(name => name switch
		{
			"SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_INDEX_OPS" => ops.ToString(CultureInfo.InvariantCulture),
			"SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_SOURCE_BYTES" => source.ToString(CultureInfo.InvariantCulture),
			"SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_WORKING_BYTES" => working.ToString(CultureInfo.InvariantCulture),
			"SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_DELTA_BYTES" => delta.ToString(CultureInfo.InvariantCulture),
			"SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_KEY_BATCH_BYTES" => keys.ToString(CultureInfo.InvariantCulture),
			_ => null
		});

	private static byte[]? Read(LightningStore store, byte[] key)
		=> store.Read(tx => tx.TryGet(Tables.Meta, key, out var value) ? value.ToArray() : null);

	private static string[] Rows(LightningStore store, TableDef table)
		=> store.Read(tx => tx.Range(table, []).Select(row =>
			Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value)).ToArray());

	private static Dictionary<string, byte[]> CategoryRows(LightningStore store)
		=> store.Read(tx => tx.Range(Tables.WikiByCategory, []).ToDictionary(
			row => Convert.ToHexString(row.Key), row => row.Value.ToArray(), StringComparer.Ordinal));

	// This measures committed row changes independently of checkpoint counters. It cannot detect
	// transient duplicate Put/Delete calls that cancel within the same native transaction.
	private static (int Deletes, int Puts, long Bytes) CheckCategoryDelta(
		Dictionary<string, byte[]> before, Dictionary<string, byte[]> after, WikiCategoryMigrationOptions options)
	{
		var deletes = before.Keys.Where(key => !after.ContainsKey(key)).ToArray();
		var puts = after.Where(row => !before.TryGetValue(row.Key, out var prior)
			|| !prior.AsSpan().SequenceEqual(row.Value)).ToArray();
		var bytes = deletes.Sum(key => key.Length / 2L)
			+ puts.Sum(row => row.Key.Length / 2L + row.Value.LongLength);
		Check(deletes.Length + puts.Length <= options.MaxIndexOps, "Committed native row changes exceeded index operation cap.");
		Check(bytes <= options.MaxDeltaBytes, "Committed native index payload exceeded delta byte cap.");
		return (deletes.Length, puts.Length, bytes);
	}

	private static string[] Snapshot(LightningStore store)
		=> new[] { Tables.Meta, Tables.WikiByCategory, Tables.WikiPage, Tables.WikiRecent, Tables.WikiByNamespace }
			.SelectMany(table => Rows(store, table).Select(row => table.Name + ":" + row)).ToArray();

	private static byte[] RawPage(string[] categories, string body = "", string title = "T", string author = "A")
		=> Codec.Serialize(new WikiPageRecord
		{
			Slug = "legacy", Title = title, Categories = categories, MarkdownSource = body,
			AuthorDbref = author, Published = false, RevisionNumber = 17
		});

	private static byte[] CategoryKey(string category, string title, long id)
		=> Keys.Upper(category).Concat(new byte[] { 0 }).Concat(Encoding.BigEndianUnicode.GetBytes(title))
			.Concat(new byte[] { 0, 0 }).Concat(Keys.Dbref(id)).ToArray();

	// Deliberately independent literal wire assertions; the expected category value is not produced
	// by the codec under test, and unrelated historical values retain their full bytes.
	private static byte[] CategoryValue(string category, string author)
	{
		var authorBytes = Encoding.UTF8.GetBytes(author);
		var value = new byte[authorBytes.Length + 9];
		authorBytes.CopyTo(value, 1);
		BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(authorBytes.Length + 1), checked((uint)Keys.Upper(category).Length));
		new byte[] { 255, 67, 0, 1 }.CopyTo(value, value.Length - 4);
		return value;
	}

	private sealed record View(WikiCategoryMigrationPhase Phase, ulong Revision, ulong Cleared,
		ulong Pages, ulong Puts, byte[] Clear, byte[] Completed, byte[] Current, ulong Ordinal, long Time);

	private static View State(LightningStore store)
	{
		var bytes = Read(store, StateKey) ?? throw new InvalidOperationException("Missing checkpoint.");
		var state = WikiCategoryCheckpointCodec.Decode(bytes, store.MaxKeySize);
		return new(state.Phase, state.Revision, state.ClearedRows, state.CompletedPages, state.CategoryPuts,
			state.LastClearedKey.ToArray(), state.LastCompletedSourceKey.ToArray(), state.CurrentSourceKey.ToArray(),
			state.NextCategoryOrdinal, state.CompletionUnixMs);
	}

	private static void AssertAuthority(LightningStore store)
	{
		var state = State(store);
		var marker = Read(store, MarkerKey);
		if (state.Phase != WikiCategoryMigrationPhase.Complete)
		{
			Check(marker is null, "Completion marker appeared before Complete committed.");
			return;
		}
		Check(marker is not null && marker.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(
			"{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":" + state.Time.ToString(CultureInfo.InvariantCulture) + "}")),
			"Complete and compact marker must commit with the same timestamp.");
	}

	private sealed class AbortBatch : Exception { }

	// Real production batch inside the real native writer. The expected owner is retained through
	// commit; on a fault the captured new owner is explicitly cleaned up by this caller.
	private sealed class Driver(LightningDatabase db) : IDisposable
	{
		private NativeReadValue? _expected;
		private bool _hasExpected;
		private readonly long _epoch = db.Store.Epoch;
		private readonly List<NativeReadBudget> _budgets = [];
		internal NativeReadBudget? LastBudget { get; private set; }

		internal async Task Step(WikiCategoryMigrationOptions options, bool abortAfterWrites = false,
			CancellationToken ct = default, long? epoch = null, byte[]? expectedOverride = null, bool? presentOverride = null)
		{
			var budget = options.CreateBudget(ct);
			LastBudget = budget;
			_budgets.Add(budget);
			NativeReadValue? candidate = null;
			var beforeRows = CategoryRows(db.Store);
			try
			{
				await db.Store.WriteAsync(tx =>
				{
					candidate = db.ApplyWikiCategoryMigrationBatch(tx, options, presentOverride ?? _hasExpected,
						expectedOverride ?? _expected?.Value ?? [], epoch ?? _epoch, budget);
					Check(tx.TryGet(Tables.Meta, StateKey, out var stored) && stored.SequenceEqual(candidate.Value),
						"Returned state differs from actual transaction state.");
					Check(budget.WorkingBytes == 128 + 32 + ((candidate.Value.Length + 7L) / 8 * 8),
						"Successful batch retained working reservations beyond its returned state owner.");
					if (abortAfterWrites) throw new AbortBatch();
				}, CancellationToken.None);
				_expected?.Dispose();
				_expected = candidate;
				candidate = null;
				_hasExpected = true;
				AssertAuthority(db.Store);
				CheckCategoryDelta(beforeRows, CategoryRows(db.Store), options);
			}
			finally
			{
				candidate?.Dispose();
				if (candidate is not null || !_hasExpected)
					Check(budget.WorkingBytes == 0, "Failed batch leaked a candidate or temporary reservation.");
			}
		}

		internal async Task Finish(WikiCategoryMigrationOptions options)
		{
			for (var i = 0; i < 512; i++)
			{
				if (_hasExpected && State(db.Store).Phase == WikiCategoryMigrationPhase.Complete) return;
				await Step(options);
			}
			throw new InvalidOperationException("Migration failed to make bounded progress.");
		}

		public void Dispose()
		{
			_expected?.Dispose();
			Check(_budgets.All(budget => budget.WorkingBytes == 0), "Checkpoint owner or an earlier batch budget leaked.");
		}
	}

	private static async Task<Exception?> Capture(Func<Task> action)
	{
		try { await action(); return null; }
		catch (Exception error) { return error; }
	}

	[Test]
	public async Task BoundedTransactionsClearDeletedKeysBuildManySourcesAndResumeDistinctFanout()
	{
		await using var db = Open(NewPath());
		var categories = Enumerable.Range(0, 9).Select(i => "c" + i).Concat(new[] { "C0", "c1" }).ToArray();
		const string author = "A\0é";
		await db.Store.WriteAsync(tx =>
		{
			for (var i = 0; i < 8; i++) tx.Put(Tables.WikiByCategory, new byte[] { (byte)(i + 1) }, new byte[140000]);
			for (var i = 0; i < 5; i++) tx.Put(Tables.WikiPage, Keys.Dbref(i), i == 0
				? Encoding.UTF8.GetBytes("{ \"Title\":\"T\\u0000\", \"AuthorDbref\":\"A\\u0000é\", \"Published\":false, \"Categories\":[\"c0\",\"c1\",\"c2\",\"c3\",\"c4\",\"c5\",\"c6\",\"c7\",\"c8\",\"C0\",\"c1\"], \"unknown\": [1,2] }")
				: RawPage(categories, title: "T\0", author: author));
			tx.Put(Tables.WikiRecent, [4], [0, 65, 0, 195, 169]);
			tx.Put(Tables.WikiByNamespace, [5], [1, 66, 255]);
		});
		var sources = Rows(db.Store, Tables.WikiPage);
		var recent = Rows(db.Store, Tables.WikiRecent);
		var namespaces = Rows(db.Store, Tables.WikiByNamespace);
		using var driver = new Driver(db);
		var options = Options();
		await driver.Step(options);
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Clearing, Revision: 1, Cleared: 0 }, "Begin did unrelated work.");
		var clears = 0;
		var partials = 0;
		var pages = 0;
		for (var i = 0; i < 128 && State(db.Store).Phase != WikiCategoryMigrationPhase.Complete; i++)
		{
			var before = State(db.Store);
			var beforeRows = CategoryRows(db.Store);
			await driver.Step(options);
			var after = State(db.Store);
			var actual = CheckCategoryDelta(beforeRows, CategoryRows(db.Store), options);
			Check(after.Cleared - before.Cleared == (ulong)actual.Deletes && after.Puts - before.Puts == (ulong)actual.Puts,
				"Checkpoint operation counters differ from independently observed native row changes.");
			Check(after.Revision == before.Revision + 1, "A committed batch did not advance exactly one revision.");
			Check(after.Cleared - before.Cleared <= 2 && after.Puts - before.Puts <= 2 && after.Pages - before.Pages <= 1,
				"A transaction exceeded index operations or decoded more than one source.");
			if (after.Cleared > before.Cleared)
			{
				clears++;
				Check(db.Store.Read(tx => !tx.TryGet(Tables.WikiByCategory, after.Clear, out _)), "Saved clear resume key was not deleted.");
				if (after.Cleared == 8) Check(after.Phase == WikiCategoryMigrationPhase.Clearing,
					"Exact-full clearing guessed EOF without a following native seek.");
			}
			if (after.Current.Length != 0) partials++;
			if (after.Pages > before.Pages)
			{
				pages++;
				Check(after.Phase == WikiCategoryMigrationPhase.Building && after.Current.Length == 0,
					"Completing a source must clear partial state and leave EOF to a real subsequent key seek.");
			}
		}
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Cleared: 8, Pages: 5, Puts: 45 }
			&& clears >= 4 && partials >= 4 && pages >= 5, "Did not exercise multiple actual clearing/source/fanout transactions.");
		Check(sources.SequenceEqual(Rows(db.Store, Tables.WikiPage)) && recent.SequenceEqual(Rows(db.Store, Tables.WikiRecent))
			&& namespaces.SequenceEqual(Rows(db.Store, Tables.WikiByNamespace)), "Migration changed source or unrelated index bytes.");
		for (var id = 0; id < 5; id++)
			foreach (var category in categories.Take(9))
				Check(db.Store.Read(tx => tx.TryGet(Tables.WikiByCategory, CategoryKey(category, "T\0", id), out var value)
					&& value.SequenceEqual(CategoryValue(category, author))), "Category key/value wire bytes differ from literal legacy identity.");
	}

	[Test]
	public async Task FirstBuildingBatchDoesNotDecodePoisonFollowingSourceBody()
	{
		await using var db = Open(NewPath());
		await db.Store.WriteAsync(tx =>
		{
			tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["x"]));
			tx.Put(Tables.WikiPage, Keys.Dbref(1), "{"u8.ToArray());
		});
		using var driver = new Driver(db);
		var options = Options(ops: 128);
		await driver.Step(options);
		await driver.Step(options);
		await driver.Step(options);
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Building, Pages: 1, Puts: 1 },
			"First Building batch decoded the next source body or did not finish its valid source.");
		Check(db.Store.Read(tx => tx.TryGet(Tables.WikiByCategory, CategoryKey("x", "T", 0), out var value)
			&& value.SequenceEqual(CategoryValue("x", "A"))), "Valid first source did not commit its actual category row.");
		var before = Snapshot(db.Store);
		Check(await Capture(() => driver.Step(options)) is InvalidDataException or System.Text.Json.JsonException,
			"Poison following source did not reject when its own Building batch reached it.");
		Check(before.SequenceEqual(Snapshot(db.Store)) && driver.LastBudget!.WorkingBytes == 0,
			"Poison source advanced or leaked migration state.");
	}

	[Test]
	public async Task SourceWith129DistinctCategoriesResumesBeyondAtomicCategoryLimit()
	{
		await using var db = Open(NewPath());
		var categories = Enumerable.Range(0, 129).Select(i => "category" + i).ToArray();
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(categories)));
		using var driver = new Driver(db);
		var options = Options(ops: 128);
		await driver.Step(options);
		await driver.Step(options);
		await driver.Step(options);
		Check(State(db.Store) is { Pages: 0, Puts: 128, Ordinal: 128 } && CategoryRows(db.Store).Count == 128,
			"Ordinary legacy categories were constrained by Atomic's 128-category admission policy.");
		await driver.Step(options);
		Check(State(db.Store) is { Pages: 1, Puts: 129, Ordinal: 0 } && CategoryRows(db.Store).Count == 129,
			"129th distinct category did not resume and complete the source.");
		foreach (var category in categories)
			Check(db.Store.Read(tx => tx.TryGet(Tables.WikiByCategory, CategoryKey(category, "T", 0), out var value)
				&& value.SequenceEqual(CategoryValue(category, "A"))), "Distinct fanout lost an actual category row.");
		await driver.Finish(options);
	}

	[Test]
	public async Task DeltaLimitCommitsPartialProgressWhileIndexOperationAllowanceRemains()
	{
		await using var db = Open(NewPath());
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["x", "y", "z"])));
		var onePayload = CategoryKey("x", "T", 0).Length + CategoryValue("x", "A").Length;
		var options = Options(ops: 128, delta: onePayload);
		using var driver = new Driver(db);
		await driver.Step(options);
		await driver.Step(options);
		for (var ordinal = 1UL; ordinal <= 2; ordinal++)
		{
			var before = CategoryRows(db.Store);
			await driver.Step(options);
			var actual = CheckCategoryDelta(before, CategoryRows(db.Store), options);
			Check(actual is { Deletes: 0, Puts: 1 } && actual.Bytes == onePayload && actual.Puts < options.MaxIndexOps,
				"Partial progress did not use exactly one actual output's delta while operations remained.");
			Check(State(db.Store) is { Pages: 0 } state && state.Ordinal == ordinal && state.Puts == ordinal,
				"Delta-limited batch did not persist the next distinct ordinal.");
		}
		await driver.Finish(options);
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Pages: 1, Puts: 3 }
			&& CategoryRows(db.Store).Count == 3, "Delta-limited resume lost output or stalled.");
	}

	[Test]
	[Arguments("hash")]
	[Arguments("equal")]
	[Arguments("above")]
	public async Task PartialPageRejectsChangedHashAndInvalidSavedDistinctOrdinalWithoutWrites(string corruption)
	{
		await using var db = Open(NewPath());
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["x", "X", "y", "z"])));
		using var driver = new Driver(db);
		var options = Options(ops: 1);
		await driver.Step(options);
		await driver.Step(options);
		await driver.Step(options);
		Check(State(db.Store) is { Ordinal: 1, Puts: 1, Pages: 0 }, "Partial ordinal counted raw duplicates.");
		await db.Store.WriteAsync(tx =>
		{
			if (corruption == "hash") tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["x", "X", "y", "z"], "changed"));
			else
			{
				var raw = ReadState(tx);
				BinaryPrimitives.WriteUInt64BigEndian(raw.AsSpan(96), corruption == "equal" ? 3UL : 4UL);
				BinaryPrimitives.WriteUInt64BigEndian(raw.AsSpan(36), 4);
				tx.Put(Tables.Meta, StateKey, raw);
			}
		});
		var snapshot = Snapshot(db.Store);
		var expected = Read(db.Store, StateKey)!;
		var error = await Capture(() => driver.Step(options, expectedOverride: expected));
		Check(error is InvalidDataException && snapshot.SequenceEqual(Snapshot(db.Store)), "Partial conflict advanced or rewrote native data.");
		Check(driver.LastBudget!.WorkingBytes == 0, "Partial conflict leaked migration allocations.");
	}

	private static byte[] ReadState(ITx tx)
		=> tx.TryGet(Tables.Meta, StateKey, out var value) ? value.ToArray() : throw new InvalidOperationException("Missing state.");

	[Test]
	[Arguments("bytes")]
	[Arguments("presence")]
	[Arguments("epoch")]
	public async Task CheckpointCompareAndSwapRejectsBeforeCategoryOrMetaWrites(string conflict)
	{
		await using var db = Open(NewPath());
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, [1], [9]));
		using var driver = new Driver(db);
		await driver.Step(Options());
		var raw = Read(db.Store, StateKey)!;
		if (conflict == "bytes") raw[19] ^= 1;
		var before = Snapshot(db.Store);
		var error = await Capture(() => driver.Step(Options(), epoch: conflict == "epoch" ? db.Store.Epoch + 1 : null,
			expectedOverride: raw, presentOverride: conflict == "presence" ? false : null));
		Check(error is InvalidDataException && error.Message.Contains("checkpoint conflict", StringComparison.OrdinalIgnoreCase),
			"CAS must report a fixed checkpoint conflict.");
		Check(before.SequenceEqual(Snapshot(db.Store)) && driver.LastBudget!.WorkingBytes == 0, "CAS conflict wrote or leaked.");
	}

	[Test]
	[Arguments("source")]
	[Arguments("keys")]
	[Arguments("delta")]
	[Arguments("working")]
	public async Task ExplicitCapacityStopPreservesCheckpointAndCorrectedOptionsResume(string dimension)
	{
		await using var db = Open(NewPath());
		var raw = RawPage(["x", "y"], dimension == "working" ? new string('b', 2 << 20) : "");
		await db.Store.WriteAsync(tx =>
		{
			tx.Put(Tables.WikiPage, Keys.Dbref(0), raw);
			if (dimension == "keys") tx.Put(Tables.WikiByCategory, [1, 2], [9]);
		});
		using var driver = new Driver(db);
		await driver.Step(Options());
		if (dimension != "keys") await driver.Step(Options());
		var limited = dimension switch
		{
			"source" => Options(source: raw.Length - 1),
			"keys" => Options(keys: 1),
			"delta" => Options(delta: 1),
			_ => Options(working: 1048576)
		};
		var before = Snapshot(db.Store);
		var error = await Capture(() => driver.Step(limited));
		if (dimension == "working") Check(error is NativeReadLimitException { Dimension: "working-bytes" }, "Missing working stop.");
		else Check(error is WikiCategoryMigrationCapacityException capacity
			&& capacity.Dimension == (dimension == "keys" ? "key-bytes" : dimension + "-bytes")
			&& !capacity.RequiresEnvelopeReview && capacity.RequiredBytes > capacity.ConfiguredBytes,
			"Capacity stop lost dimension or explicit restart guidance.");
		Check(before.SequenceEqual(Snapshot(db.Store)) && driver.LastBudget!.WorkingBytes == 0, "Capacity stop advanced or leaked.");
		await driver.Finish(Options());
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Pages: 1, Puts: 2 }, "Corrected settings did not resume.");
	}

	[Test]
	public async Task ClearingFirstKeyOverDeltaReportsDeltaSettingAndResumesWithCorrectedDelta()
	{
		await using var db = Open(NewPath());
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, [1, 2], [9]));
		using var driver = new Driver(db);
		await driver.Step(Options());
		var before = Snapshot(db.Store);
		var error = await Capture(() => driver.Step(Options(keys: 131072, delta: 1)));
		Check(error is WikiCategoryMigrationCapacityException
		{
			Dimension: "delta-bytes",
			SettingName: "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_DELTA_BYTES",
			ConfiguredBytes: 1,
			RequiredBytes: 2,
			RequiresEnvelopeReview: false
		}, "Clearing first-key stop attributed the stricter delta allowance to the key-batch setting.");
		Check(before.SequenceEqual(Snapshot(db.Store)) && driver.LastBudget!.WorkingBytes == 0,
			"Delta-limited first-key stop changed native rows/checkpoint or leaked ownership.");
		await driver.Step(Options(keys: 131072, delta: 2));
		Check(State(db.Store) is { Cleared: 1 } && CategoryRows(db.Store).Count == 0,
			"Increasing only the delta setting did not resume the blocked obsolete-key deletion.");
		await driver.Finish(Options(keys: 131072, delta: 2));
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Cleared: 1, Pages: 0, Puts: 0 },
			"Corrected delta allowance failed to complete the empty-source migration.");
	}

	[Test]
	public async Task CoordinatorReadsRawSourceAbovePublic128KiBCapAndCompleteIsReadOnlyOnReopen()
	{
		var path = NewPath();
		byte[] complete;
		string[] before;
		await using (var db = Open(path))
		{
			var raw = RawPage(["x"], new string('b', 150000));
			Check(raw.Length > 131072, "Fixture did not exceed public native record ceiling.");
			await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(0), raw));
			await db.MigrateWikiCategoryValuesAsync(Options());
			Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Pages: 1, Puts: 1 }, "Private coordinator rejected large legitimate source.");
			AssertAuthority(db.Store);
			complete = Read(db.Store, StateKey)!;
			before = Snapshot(db.Store);
		}
		await using var reopened = Open(path);
		await reopened.MigrateWikiCategoryValuesAsync(Options());
		Check(complete.SequenceEqual(Read(reopened.Store, StateKey)!) && before.SequenceEqual(Snapshot(reopened.Store)),
			"Valid Complete invocation rebuilt or rewrote the ordinary reopened world.");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FaultAfterActualBatchWritesRollsBackAndCommittedStepResumesOnOrdinaryReopen(bool building)
	{
		var path = NewPath();
		byte[] committed;
		await using (var db = Open(path))
		{
			await db.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.WikiByCategory, [1], [9]);
				tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["x", "y", "z"]));
			});
			using var driver = new Driver(db);
			await driver.Step(Options(ops: 1));
			if (building)
			{
				await driver.Step(Options(ops: 1));
				await driver.Step(Options(ops: 1));
			}
			var before = Snapshot(db.Store);
			var error = await Capture(() => driver.Step(Options(ops: 1), abortAfterWrites: true));
			Check(error is AbortBatch && before.SequenceEqual(Snapshot(db.Store)), "Post-write throw did not roll back real batch writes.");
			Check(driver.LastBudget!.WorkingBytes == 0, "Faulted candidate owner was not released.");
			await driver.Step(Options(ops: 1));
			committed = Read(db.Store, StateKey)!;
		}
		await using var reopened = Open(path);
		Check(committed.SequenceEqual(Read(reopened.Store, StateKey)!), "Committed checkpoint did not survive ordinary reopen.");
		await reopened.MigrateWikiCategoryValuesAsync(Options(ops: 1));
		Check(State(reopened.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Cleared: 1, Pages: 1, Puts: 3 },
			"Coordinator did not resume committed native progress after reopen.");
		AssertAuthority(reopened.Store);
	}

	[Test]
	public async Task FinalCompleteAndMarkerRollBackTogetherWhenWriterCallbackThrows()
	{
		await using var db = Open(NewPath());
		using var driver = new Driver(db);
		await driver.Step(Options());
		await driver.Step(Options());
		Check(State(db.Store).Phase == WikiCategoryMigrationPhase.Building, "Empty clearing did not reach Building.");
		var before = Snapshot(db.Store);
		Check(await Capture(() => driver.Step(Options(), abortAfterWrites: true)) is AbortBatch,
			"Fault did not execute after the real final batch wrote state and marker.");
		Check(before.SequenceEqual(Snapshot(db.Store)) && Read(db.Store, MarkerKey) is null,
			"Complete or marker escaped the aborted native transaction.");
		Check(driver.LastBudget!.WorkingBytes == 0, "Aborted Complete candidate owner leaked.");
		await driver.Step(Options());
		Check(State(db.Store).Phase == WikiCategoryMigrationPhase.Complete, "Final batch could not retry after rollback.");
		AssertAuthority(db.Store);
	}

	[Test]
	[Arguments("omitted")]
	[Arguments("null")]
	[Arguments("empty")]
	public async Task UncategorizedLegacySourcePreservesNullDefaultsAndAdvancesCompletedPage(string categories)
	{
		await using var db = Open(NewPath());
		var categoryProperty = categories switch
		{
			"null" => ", \"Categories\": null",
			"empty" => ", \"Categories\": []",
			_ => ""
		};
		var raw = Encoding.UTF8.GetBytes("{ \"Title\": \"Uncategorized\", \"AuthorDbref\": \"A\", \"Published\": false"
			+ categoryProperty + ", \"unknown\": [1,2] }");
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(0), raw));
		using var driver = new Driver(db);
		var options = Options();
		await driver.Step(options);
		await driver.Step(options);
		await driver.Step(options);
		var completed = State(db.Store);
		Check(completed is { Phase: WikiCategoryMigrationPhase.Building, Pages: 1, Puts: 0, Ordinal: 0 }
			&& completed.Completed.AsSpan().SequenceEqual(Keys.Dbref(0)) && completed.Current.Length == 0,
			"Valid uncategorized source was rejected or skipped instead of durably completing page zero.");
		Check(CategoryRows(db.Store).Count == 0 && db.Store.Read(tx =>
			tx.TryGet(Tables.WikiPage, Keys.Dbref(0), out var value) && value.SequenceEqual(raw)),
			"Uncategorized source produced category rows or changed its original source bytes.");
		await driver.Step(options);
		Check(State(db.Store) is { Phase: WikiCategoryMigrationPhase.Complete, Pages: 1, Puts: 0 }
			&& CategoryRows(db.Store).Count == 0, "Real next-source EOF did not complete an uncategorized world.");
		AssertAuthority(db.Store);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UnusableSourceIsRejectedWithoutSkippingOrAdvancing(bool nullProjection)
	{
		await using var db = Open(NewPath());
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(0),
			nullProjection ? "null"u8.ToArray() : "{\"Title\":\"T\",\"Categories\":[null]}"u8.ToArray()));
		using var driver = new Driver(db);
		await driver.Step(Options());
		await driver.Step(Options());
		var before = Snapshot(db.Store);
		Check(await Capture(() => driver.Step(Options())) is InvalidDataException or System.Text.Json.JsonException,
			"Unusable projection or null category was silently skipped.");
		Check(before.SequenceEqual(Snapshot(db.Store)) && driver.LastBudget!.WorkingBytes == 0,
			"Rejected source advanced or leaked migration ownership.");
	}

	private sealed class ConfiguredMigrationSource(IReadOnlyList<LightningMigrationStep> steps) : IMigrationSource
	{
		public IEnumerable<LightningMigrationStep> LightningSteps => steps;
	}

	private static void AssertCompleteAuthorityInTransaction(ITx tx, int maxKeySize)
	{
		Check(tx.TryGet(Tables.Meta, StateKey, out var stateBytes), "Plugin ran before category checkpoint existed.");
		var state = WikiCategoryCheckpointCodec.Decode(stateBytes, maxKeySize);
		Check(state.Phase == WikiCategoryMigrationPhase.Complete && state.CompletionUnixMs > 0,
			"Plugin ran before category migration committed Complete.");
		Check(tx.TryGet(Tables.Meta, MarkerKey, out var marker), "Plugin ran before category completion marker existed.");
		var expected = Encoding.UTF8.GetBytes("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":"
			+ state.CompletionUnixMs.ToString(CultureInfo.InvariantCulture) + "}");
		Check(marker.SequenceEqual(expected), "Plugin observed an invalid or mismatched Complete authority pair.");
	}

	[Test]
	public async Task PublicMigrateCompletesCategoryUpgradeBeforePluginStepAndItsOrdinaryWikiWrite()
	{
		var applied = 0;
		var source = new ConfiguredMigrationSource([new LightningMigrationStep("test_category_after_core", async accessor =>
		{
			var provider = accessor as LightningDatabase ?? throw new InvalidOperationException("Expected ordinary provider accessor.");
			accessor.Read(tx =>
			{
				AssertCompleteAuthorityInTransaction(tx, provider.Store.MaxKeySize);
				Check(tx.TryGet(Tables.WikiByCategory, CategoryKey("legacy", "T", 0), out var value)
					&& value.SequenceEqual(CategoryValue("legacy", "A")), "Plugin ran before legacy category rows were upgraded.");
				return 0;
			});
			Check(await provider.CountPagesInCategoryAsync("legacy", WikiVisibility.All) == 1,
				"Public migration had not published category readiness before invoking plugin.");
			var wiki = new WikiStoreService(provider, new WikiMarkdigPipeline());
			var result = await wiki.CreateAsync("Plugin page", "plugin body", "#1",
				categories: ["plugin-ready"], slug: "plugin-category-startup");
			Check(result is WikiPage, "Plugin's ordinary wiki write failed after Complete authority was observed.");
			Check(await provider.CountPagesInCategoryAsync("plugin-ready", WikiVisibility.PublishedOnly) == 1
				&& await provider.CountPagesInCategoryAsync("legacy", WikiVisibility.All) == 1,
				"Plugin's ordinary wiki write did not maintain both existing and new category memberships.");
			applied++;
		})]);
		await using var db = Open(NewPath(), [source]);
		await db.Store.WriteAsync(tx =>
		{
			tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["legacy"]));
			tx.Put(Tables.Meta, Keys.Str("next_wiki"), Keys.Dbref(1));
			tx.Put(Tables.WikiByCategory, [1, 2], [0, 65]);
		});
		await db.Migrate();
		Check(applied == 1 && Read(db.Store, Keys.Str("mig:test_category_after_core")) is not null,
			"Public migration did not execute and record the plugin after category upgrade.");
		var completed = Read(db.Store, StateKey)!;
		var rows = CategoryRows(db.Store);
		await db.Migrate();
		Check(applied == 1 && completed.SequenceEqual(Read(db.Store, StateKey)!),
			"Repeated public migration reran plugin or rewrote valid Complete state.");
		var unchanged = CheckCategoryDelta(rows, CategoryRows(db.Store), Options());
		Check(unchanged is { Deletes: 0, Puts: 0 }, "Repeated public migration rebuilt category rows.");
	}

	[Test]
	public async Task PublicMigrateRejectsReservedCategoryMigrationIdBeforeAnyUpgradeOrPluginStep()
	{
		var ordinaryApplied = false;
		var impersonatorApplied = false;
		var source = new ConfiguredMigrationSource([
			new LightningMigrationStep("test_preceding_reserved_category", _ =>
			{
				ordinaryApplied = true;
				return ValueTask.CompletedTask;
			}),
			new LightningMigrationStep("0004_wiki_category_value_v1", _ =>
			{
				impersonatorApplied = true;
				return ValueTask.CompletedTask;
			})]);
		await using var db = Open(NewPath(), [source]);
		await db.Store.WriteAsync(tx =>
		{
			tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["legacy"]));
			tx.Put(Tables.WikiByCategory, [1, 2], [0, 65]);
		});
		var categories = Rows(db.Store, Tables.WikiByCategory);
		var state = Read(db.Store, StateKey);
		var marker = Read(db.Store, MarkerKey);
		var error = await Capture(async () => await db.Migrate());
		Check(error is ArgumentException or InvalidOperationException or InvalidDataException,
			"Public migration accepted a plugin step impersonating the reserved category migration ID.");
		Check(!ordinaryApplied && !impersonatorApplied,
			"Reserved migration ID must be rejected during preflight before executing even a preceding plugin step.");
		Check(categories.SequenceEqual(Rows(db.Store, Tables.WikiByCategory))
			&& state is null && Read(db.Store, StateKey) is null && marker is null && Read(db.Store, MarkerKey) is null,
			"Reserved migration ID was rejected after category upgrade writes had already begun.");
		Check(Read(db.Store, Keys.Str("mig:test_preceding_reserved_category")) is null,
			"Reserved-ID rejection left a preceding plugin's applied marker.");
	}

	[Test]
	public async Task CancellationDoesNotBeginOrAdvanceCommittedCheckpoint()
	{
		await using var db = Open(NewPath());
		await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(0), RawPage(["x", "y"])));
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		var before = Snapshot(db.Store);
		Check(await Capture(async () => await db.MigrateWikiCategoryValuesAsync(Options(), cancelled.Token)) is OperationCanceledException,
			"Coordinator ignored cancellation before Begin.");
		Check(before.SequenceEqual(Snapshot(db.Store)), "Cancelled coordinator began a checkpoint.");
		using var driver = new Driver(db);
		await driver.Step(Options());
		before = Snapshot(db.Store);
		Check(await Capture(() => driver.Step(Options(), ct: cancelled.Token)) is OperationCanceledException,
			"Cancelled native batch ignored cancellation.");
		Check(before.SequenceEqual(Snapshot(db.Store)) && driver.LastBudget!.WorkingBytes == 0, "Cancellation advanced or retained state.");
	}
}
