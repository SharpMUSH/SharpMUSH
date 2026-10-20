using System.Text;
using LightningDB;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryMigrationReadTests
{
	private readonly List<string> _paths = [];
	private const int KeyCap = 128 * 1024;
	private const int SourceCap = 64 * 1024 * 1024;

	private string NewPath()
	{
		var scratch = Environment.GetEnvironmentVariable("TMPDIR")
			?? Path.Join(AppContext.BaseDirectory, "work");
		Directory.CreateDirectory(scratch);
		var path = Path.Join(scratch, "sharpmush-category-migration-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return path;
	}

	private LightningStore Open(string? path = null) => new(new LightningStoreOptions { Path = path ?? NewPath(), MapSize = 256L << 20 });

	[After(Test)]
	public void Cleanup()
	{
		foreach (var path in _paths.Where(Directory.Exists)) Directory.Delete(path, recursive: true);
	}

	private static NativeReadBudget Budget(int probes = 8192, long bytes = 32L << 20, long working = 32L << 20,
		int? records = null, CancellationToken ct = default) => new(probes, bytes, working, ct, records);
	private static IWikiCategoryMigrationReadTx Reader(ITx tx) => tx as IWikiCategoryMigrationReadTx
		?? throw new InvalidOperationException("Native category migration reads are required.");
	private static Found<NativeReadValue> Page(LightningStore store, byte[] key, int cap, NativeReadBudget budget)
		=> store.Read(tx => Reader(tx).ReadWikiCategoryPage(key, cap, budget));
	private static WikiCategoryKeyBatch Batch(LightningStore store, NativeReadBudget budget, TableDef? table = null,
		byte[]? after = null, int count = 128, int bytes = KeyCap)
		=> store.Read(tx => Reader(tx).ReadWikiCategoryKeys(table ?? Tables.WikiByCategory, after ?? [], count, bytes, budget));
	// Independently written conservative model from the contract, never production accounting helpers.
	private static long ArrayCost(long payload) => 32 + ((payload + 7) / 8 * 8);
	private static long SourceCost(int length) => 64 + 64 + ArrayCost(length);
	private static long BatchCost(int capacity, params int[] lengths) => 64 + 128 + ArrayCost(8L * capacity) + lengths.Sum(length => ArrayCost(length));
	private static void Check(bool condition, string diagnostic)
	{
		if (!condition) throw new InvalidOperationException(diagnostic);
	}
	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}
	private static void Limit(Action action, string dimension) => Check(Capture(action) is NativeReadLimitException limit && limit.Dimension == dimension, "Expected native limit: " + dimension);
	private static void Counters(NativeReadBudget budget, int probes, long bytes, int records, long working)
		=> Check((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes) == (probes, bytes, records, working), "Native accounting differs from independent expected counters.");

	[Test]
	public async Task LargePageUsesPrivateSourceCapAndExactIndependentAccounting()
	{
		using var store = Open();
		var source = new byte[KeyCap + 17];
		source[0] = 11;
		source[^1] = 29;
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(7), source));
		var budget = Budget(probes: 1, bytes: source.Length + 8L, working: SourceCost(source.Length), records: 1);
		using var owned = Page(store, Keys.Dbref(7), source.Length, budget).Expect<NativeReadValue>();
		Check(owned.Value.AsSpan().SequenceEqual(source), "Large source copy differs.");
		Counters(budget, 1, source.Length + 8L, 1, SourceCost(source.Length));
		owned.Dispose();
		Counters(budget, 1, source.Length + 8L, 1, 0);
	}

	[Test]
	public async Task PublicBoundedPageReadRetainsIts128KiBLimit()
	{
		using var store = Open();
		var source = new byte[KeyCap + 17];
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(7), source));
		var budget = Budget();
		Limit(() => store.Read(tx =>
		{
			var bounded = tx as IBoundedReadTx ?? throw new InvalidOperationException("Public bounded reads are required.");
			return bounded.ReadValue(Tables.WikiPage, Keys.Dbref(7), budget);
		}), "record-bytes");
		Check(budget.Probes == 1 && budget.Records == 0 && budget.WorkingBytes == 0,
			"Public read expanded its record limit or admitted an oversized copy.");
	}

	[Test]
	public async Task RealSourceAbove64MiBEnvelopeReportsReviewBeforeValueAdmission()
	{
		using var store = Open();
		var source = new byte[SourceCap + 1];
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), source));
		var budget = Budget(bytes: 8, working: 1024);
		var error = Capture(() =>
		{
			if (Page(store, Keys.Dbref(1), SourceCap, budget) is NativeReadValue owner) owner.Dispose();
		});
		Check(error is WikiCategoryMigrationCapacityException
		{
			Dimension: "source-bytes",
			RequiredBytes: 67108865,
			ConfiguredBytes: 67108864,
			SettingName: "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_SOURCE_BYTES",
			RequiresEnvelopeReview: true
		}, "Native source above the hard envelope must report an operator review requirement.");
		Counters(budget, 1, 8, 0, 0);
	}

	[Test]
	public async Task ExactSourceCapSucceedsAndOneUnderRejectsBeforeLargeCopy()
	{
		using var store = Open();
		var source = new byte[4 << 20];
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), source));
		using (var exact = Page(store, Keys.Dbref(1), source.Length, Budget()).Expect<NativeReadValue>())
			Check(exact.Value.Length == source.Length, "Exact source cap must exclude the eight key bytes.");
		NativeReadBudget? rejected = null;
		void Reject()
		{
			rejected = Budget();
			if (Page(store, Keys.Dbref(1), source.Length - 1, rejected) is NativeReadValue owner) owner.Dispose();
		}
		for (var i = 0; i < 4; i++) Capture(Reject);
		var before = GC.GetAllocatedBytesForCurrentThread();
		var error = Capture(Reject);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Check(error is WikiCategoryMigrationCapacityException
		{
			Dimension: "source-bytes",
			SettingName: "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_SOURCE_BYTES",
			RequiresEnvelopeReview: false
		} capacity && capacity.RequiredBytes == source.Length && capacity.ConfiguredBytes == source.Length - 1,
			"Private source rejection must report required/configured bytes and its operator setting.");
		Check(allocated <= 64L << 10, "Rejected native source allocated more than 64 KiB after warmup.");
		Check(rejected is { Probes: 1, Records: 0, WorkingBytes: 0 }, "Overcap source escaped or retained ownership.");
	}

	[Test]
	public async Task ZeroAndNegativePageKeysAndEmptyRawValueAreAccepted()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiPage, new byte[8], []); tx.Put(Tables.WikiPage, Keys.Dbref(-1), [9]); });
		var budget = Budget();
		using var zero = Page(store, new byte[8], 1, budget).Expect<NativeReadValue>();
		using var negative = Page(store, Keys.Dbref(-1), SourceCap, budget).Expect<NativeReadValue>();
		Check(zero.Value.Length == 0 && negative.Value.AsSpan().SequenceEqual(new byte[] { 9 }), "Reader imposed page-id or JSON policy.");
		Counters(budget, 2, 17, 2, SourceCost(0) + SourceCost(1));
	}

	[Test]
	public void MissingSourceConsumesOnlyOneNativeProbe()
	{
		using var store = Open();
		var budget = Budget(probes: 1, records: 0);
		Check(Page(store, new byte[8], 1, budget) is NotFound, "Missing raw source must remain NotFound.");
		Counters(budget, 1, 0, 0, 0);
	}

	[Test]
	public void InvalidSourceArgumentsRejectBeforeNativeAccess()
	{
		using var store = Open();
		foreach (var length in new[] { 0, 7, 9 })
		{
			var budget = Budget();
			Check(Capture(() => Page(store, new byte[length], 1, budget)) is ArgumentException, "Invalid page key shape accepted.");
			Counters(budget, 0, 0, 0, 0);
		}
		foreach (var cap in new[] { -1, 0, SourceCap + 1 })
		{
			var budget = Budget();
			Check(Capture(() => Page(store, new byte[8], cap, budget)) is ArgumentOutOfRangeException, "Invalid source cap accepted.");
			Counters(budget, 0, 0, 0, 0);
		}
		Check(Capture(() => Page(store, new byte[8], 1, null!)) is ArgumentNullException, "Null budget accepted.");
	}

	[Test]
	public void PreCancellationRejectsSourceAndKeysWithoutAccounting()
	{
		using var store = Open();
		var budget = Budget(ct: new CancellationToken(true));
		Check(Capture(() => Page(store, new byte[8], 1, budget)) is OperationCanceledException, "Source ignored cancellation.");
		Check(Capture(() => { using var ignored = Batch(store, budget); }) is OperationCanceledException, "Keys ignored cancellation.");
		Counters(budget, 0, 0, 0, 0);
	}

	[Test]
	public async Task SourceAllocationOneUnderReleasesItsReservation()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), new byte[17]));
		var budget = Budget(working: SourceCost(17) - 1);
		Limit(() => Page(store, Keys.Dbref(1), 17, budget), "working-bytes");
		Check(budget.WorkingBytes == 0, "Failed source admission leaked ownership.");
	}

	[Test]
	public async Task SourceByteLimitCountsKeySeparatelyFromValue()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), new byte[17]));
		var budget = Budget(bytes: 24);
		Limit(() => Page(store, Keys.Dbref(1), 17, budget), "bytes");
		Counters(budget, 1, 8, 0, 0);
	}

	[Test]
	public async Task ExhaustedSourceProbesPreservePriorOwnedSource()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), [7]));
		var budget = Budget(probes: 1);
		using var prior = Page(store, Keys.Dbref(1), 1, budget).Expect<NativeReadValue>();
		Limit(() => Page(store, Keys.Dbref(2), 1, budget), "probes");
		Counters(budget, 1, 9, 1, SourceCost(1));
		Check(prior.Value[0] == 7, "Rejected read invalidated earlier source.");
	}

	[Test]
	public async Task ExhaustedSourceRecordAllowanceRejectsBeforeCopy()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), [7]));
		var budget = Budget(records: 0);
		Limit(() => Page(store, Keys.Dbref(1), 1, budget), "records");
		Counters(budget, 1, 9, 0, 0);
	}

	[Test]
	public async Task SourceOwnerOutlivesNativeTransactionAndDisposesExactlyOnce()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), [7]));
		var budget = Budget();
		using var owner = Page(store, Keys.Dbref(1), 1, budget).Expect<NativeReadValue>();
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), [8]));
		Check(owner.Value[0] == 7, "Source owner borrowed native memory.");
		owner.Dispose();
		owner.Dispose();
		Counters(budget, 1, 9, 1, 0);
		Check(Capture(() => { _ = owner.Value; }) is ObjectDisposedException, "Disposed source remains accessible.");
	}

	[Test]
	public async Task NativeSourceAndDecoderShareBudgetAndIndependentOwnerLifetimes()
	{
		using var store = Open();
		var source = Encoding.UTF8.GetBytes("{\"Title\":\"T\",\"AuthorDbref\":\"A\",\"Categories\":[\"x\"],\"MarkdownSource\":\"" + new string('b', KeyCap + 1) + "\"}");
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(1), source));
		// 128 + A(16) + 2*A(2) + A(4) + A(1) + S(1) + S(1) = 480.
		const long retained = 480;
		var peak = SourceCost(source.Length) + 64 + ArrayCost(source.Length) + 64 + retained;
		var rejectedBudget = Budget(working: peak - 1);
		using (var borrowed = Page(store, Keys.Dbref(1), source.Length, rejectedBudget).Expect<NativeReadValue>())
		{
			Limit(() => { using var ignored = WikiCategoryProjectionDecoder.Decode(borrowed.Value, rejectedBudget); }, "working-bytes");
			Counters(rejectedBudget, 1, source.Length + 8L, 1, SourceCost(source.Length));
			Check(borrowed.Value.Length == source.Length, "Failed decoder admission released borrowed native source.");
		}
		Check(rejectedBudget.WorkingBytes == 0, "Rejected decoder/source pair leaked ownership.");
		var budget = Budget(working: peak);
		using var owner = Page(store, Keys.Dbref(1), source.Length, budget).Expect<NativeReadValue>();
		using var projection = WikiCategoryProjectionDecoder.Decode(owner.Value, budget) ?? throw new InvalidOperationException("Expected projection.");
		Check(projection.RetainedBytes == retained, "Decoder retained model differs from independent fixture formula.");
		Counters(budget, 1, source.Length + 8L, 1, SourceCost(source.Length) + 64 + retained);
		projection.Dispose();
		Counters(budget, 1, source.Length + 8L, 1, SourceCost(source.Length));
		Check(owner.Value.Length == source.Length, "Decoder released borrowed source.");
		owner.Dispose();
		Counters(budget, 1, source.Length + 8L, 1, 0);
	}

	[Test]
	public async Task KeyOnlyBatchIgnoresFourMiBObsoleteValueAndItsCopyAllocation()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, [1], new byte[4 << 20]));
		void Read()
		{
			var budget = Budget(probes: 2, bytes: 1, working: BatchCost(1, 1), records: 1);
			using var batch = Batch(store, budget, count: 1);
			Check(batch.Count == 1 && batch.KeyAt(0).SequenceEqual(new byte[] { 1 }) && !batch.Exhausted, "Key-only batch changed shape.");
			Counters(budget, 2, 1, 1, BatchCost(1, 1));
		}
		for (var i = 0; i < 4; i++) Read();
		var before = GC.GetAllocatedBytesForCurrentThread();
		Read();
		Check(GC.GetAllocatedBytesForCurrentThread() - before <= 64L << 10, "Key-only read copied obsolete value storage.");
	}

	[Test]
	public async Task ExistingResumeKeyIsInspectedButExcludedStrictly()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiByCategory, [1], []); tx.Put(Tables.WikiByCategory, [2], []); tx.Put(Tables.WikiByCategory, [3], []); });
		var budget = Budget();
		using var batch = Batch(store, budget, after: [1]);
		Check(batch.Count == 2 && batch.KeyAt(0)[0] == 2 && batch.KeyAt(1)[0] == 3 && batch.Exhausted, "Resume was not strict.");
		Counters(budget, 7, 3, 2, BatchCost(128, 1, 1));
	}

	[Test]
	public async Task DeletedResumeKeySeeksNextGreaterKey()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiByCategory, [1], []); tx.Put(Tables.WikiByCategory, [2], []); tx.Put(Tables.WikiByCategory, [3], []); tx.Delete(Tables.WikiByCategory, [2]); });
		var budget = Budget();
		using var batch = Batch(store, budget, after: [2]);
		Check(batch.Count == 1 && batch.KeyAt(0)[0] == 3 && batch.Exhausted, "Deleted resume lost its successor.");
		Counters(budget, 3, 1, 1, BatchCost(128, 1));
	}

	[Test]
	public async Task ExactFullBatchIsNotEofAndNextEmptyBatchProvesEof()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, [1], []));
		var firstBudget = Budget();
		using var first = Batch(store, firstBudget, count: 1);
		Check(first.Count == 1 && !first.Exhausted, "Count-full batch guessed EOF.");
		Counters(firstBudget, 2, 1, 1, BatchCost(1, 1));
		var secondBudget = Budget();
		using var second = Batch(store, secondBudget, after: first.KeyAt(0).ToArray(), count: 1);
		Check(second.Count == 0 && second.Exhausted, "Native NotFound did not prove EOF.");
		Counters(secondBudget, 3, 1, 0, BatchCost(1));
	}

	[Test]
	public async Task Maximum128KeysStopsWithoutProbeOrCopyOf129th()
	{
		using var store = Open();
		await store.WriteAsync(tx => { for (var i = 1; i <= 129; i++) tx.Put(Tables.WikiByCategory, [(byte)i], []); });
		var budget = Budget(probes: 256, bytes: 128, records: 128);
		using var batch = Batch(store, budget);
		Check(batch.Count == 128 && batch.KeyAt(127)[0] == 128 && !batch.Exhausted, "Native batch did not stop at 128 keys.");
		Counters(budget, 256, 128, 128, BatchCost(128, Enumerable.Repeat(1, 128).ToArray()));
	}

	[Test]
	public async Task FirstKeyOverLocalByteAllowanceThrowsAndReleasesBatch()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, [1, 2], []));
		var budget = Budget();
		var error = Capture(() => { using var ignored = Batch(store, budget, bytes: 1); });
		Check(error is WikiCategoryMigrationCapacityException
		{
			Dimension: "key-bytes",
			RequiredBytes: 2,
			ConfiguredBytes: 1,
			SettingName: "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_KEY_BATCH_BYTES",
			RequiresEnvelopeReview: false
		}, "Private key rejection must report required/configured bytes and its operator setting.");
		Counters(budget, 2, 2, 0, 0);
	}

	[Test]
	public async Task PopulatedLocalByteAllowanceDefersInspectedKeyWithoutRecordOrCopyCharge()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiByCategory, [1], []); tx.Put(Tables.WikiByCategory, [2, 3], []); });
		var budget = Budget();
		using var batch = Batch(store, budget, count: 2, bytes: 2);
		Check(batch.Count == 1 && batch.KeyAt(0)[0] == 1 && !batch.Exhausted, "Local key bytes did not defer the second row.");
		Counters(budget, 4, 3, 1, BatchCost(2, 1));
		using var next = Batch(store, Budget(), after: batch.KeyAt(0).ToArray(), bytes: 2);
		Check(next.Count == 1 && next.KeyAt(0).SequenceEqual(new byte[] { 2, 3 }), "Deferred key was lost.");
	}

	[Test]
	public void InitialKeyAllocationIsReservedBeforeCursorOrProbe()
	{
		using var store = Open();
		var budget = Budget(working: BatchCost(1) - 1);
		Limit(() => { using var ignored = Batch(store, budget, count: 1); }, "working-bytes");
		Counters(budget, 0, 0, 0, 0);
	}

	[Test]
	public async Task KeyCopyAllocationOneUnderReleasesBatchWithoutPartialResult()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, [1], []));
		var budget = Budget(working: BatchCost(1, 1) - 1);
		Limit(() => { using var ignored = Batch(store, budget, count: 1); }, "working-bytes");
		Check(budget.Probes == 2 && budget.Bytes == 1 && budget.WorkingBytes == 0, "Failed key allocation leaked or skipped inspection.");
	}

	[Test]
	public async Task KeyByteBudgetFailureAfterOneCopyReleasesAllBatchOwnership()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiByCategory, [1], []); tx.Put(Tables.WikiByCategory, [2, 3], []); });
		var budget = Budget(bytes: 2);
		Limit(() => { using var ignored = Batch(store, budget); }, "bytes");
		Counters(budget, 4, 1, 1, 0);
	}

	[Test]
	public async Task KeyProbeFailureAfterOneCopyReleasesAllBatchOwnership()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiByCategory, [1], []); tx.Put(Tables.WikiByCategory, [2], []); });
		var budget = Budget(probes: 3);
		Limit(() => { using var ignored = Batch(store, budget); }, "probes");
		Counters(budget, 3, 1, 1, 0);
	}

	[Test]
	public async Task KeyRecordFailurePreservesSeparateSourceOwnerAndReleasesPartialKeys()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiPage, Keys.Dbref(1), [9]); tx.Put(Tables.WikiByCategory, [1], []); tx.Put(Tables.WikiByCategory, [2], []); });
		var budget = Budget(records: 2);
		using var source = Page(store, Keys.Dbref(1), 1, budget).Expect<NativeReadValue>();
		Limit(() => { using var ignored = Batch(store, budget); }, "records");
		Counters(budget, 5, 11, 2, SourceCost(1));
		Check(source.Value[0] == 9, "Partial key cleanup released unrelated source.");
		source.Dispose();
		Check(budget.WorkingBytes == 0, "Prior owner did not release.");
	}

	[Test]
	public async Task KeyOwnerRetainsOnlyItsExactLeaseAndRejectsUseAfterDispose()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, [1, 2], []));
		var budget = Budget(working: BatchCost(1, 2));
		using var batch = Batch(store, budget, count: 1);
		Counters(budget, 2, 2, 1, BatchCost(1, 2));
		Check(Capture(() => { _ = batch.KeyAt(-1).Length; }) is ArgumentOutOfRangeException, "Negative key index accepted.");
		Check(Capture(() => { _ = batch.KeyAt(1).Length; }) is ArgumentOutOfRangeException, "Unowned key slot accessible.");
		await store.WriteAsync(tx => tx.Delete(Tables.WikiByCategory, [1, 2]));
		Check(batch.KeyAt(0).SequenceEqual(new byte[] { 1, 2 }), "Copied key borrowed the native map.");
		batch.Dispose();
		batch.Dispose();
		Check(budget.WorkingBytes == 0 && !batch.Exhausted, "Disposal lost metadata or released twice.");
		Check(Capture(() => { _ = batch.KeyAt(0).Length; }) is ObjectDisposedException, "Disposed key storage accessible.");
	}

	[Test]
	public async Task CopiedKeysCanBeDeletedInSameWriteTransactionWhileLeaseIsAlive()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.WikiPage, Keys.Dbref(0), [1]); tx.Put(Tables.WikiPage, Keys.Dbref(1), [2]); });
		var budget = Budget();
		await store.WriteAsync(tx =>
		{
			using var batch = Reader(tx).ReadWikiCategoryKeys(Tables.WikiPage, [], 128, KeyCap, budget);
			Check(batch.Count == 2 && batch.Exhausted, "Page-key batch shape differs.");
			for (var i = 0; i < batch.Count; i++) Check(tx.Delete(Tables.WikiPage, batch.KeyAt(i)), "Copied-key delete failed.");
			Check(tx.Count(Tables.WikiPage) == 0 && budget.WorkingBytes == BatchCost(128, 8, 8), "Delete corrupted borrowed copied keys or their lease.");
		});
		Counters(budget, 5, 16, 2, 0);
		Check(store.Read(tx => tx.Count(Tables.WikiPage)) == 0, "Copied-key deletes were not committed.");
	}

	[Test]
	public void OnlySupportedTableNamesAreAllowedAndRequestedFlagsDoNotCertifyNativeFlags()
	{
		using var store = Open();
		foreach (var table in new[] { Tables.Meta, Tables.RevLocation, TableDef.Node("wiki.cat.alias") })
		{
			var budget = Budget();
			Check(Capture(() => { using var ignored = Batch(store, budget, table); }) is NotSupportedException, "Unsupported migration table accepted.");
			Counters(budget, 0, 0, 0, 0);
		}
		foreach (var name in new[] { Tables.WikiPage.Name, Tables.WikiByCategory.Name })
		{
			var budget = Budget();
			using var batch = Batch(store, budget, TableDef.Index(name, duplicates: true));
			Check(batch.Count == 0 && batch.Exhausted, "Requested flags overrode actual plain native flags.");
			Counters(budget, 1, 0, 0, BatchCost(128));
		}
	}

	[Test]
	public void PersistedNativeDuplicateFlagsRejectDespitePlainCoreTableDefinitions()
	{
		var path = NewPath();
		Directory.CreateDirectory(path);
		using (var environment = new LightningEnvironment(path) { MapSize = 256L << 20, MaxDatabases = 256 })
		{
			environment.Open();
			using var tx = environment.BeginTransaction();
			using var pages = tx.OpenDatabase(Tables.WikiPage.Name, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort });
			using var categories = tx.OpenDatabase(Tables.WikiByCategory.Name, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort });
			var commit = tx.Commit();
			if (commit != MDBResultCode.Success) throw LightningStoreException.From(commit, "seed category flags fixture");
		}
		using var store = Open(path);
		var budget = Budget();
		Check(Capture(() => Page(store, new byte[8], 1, budget)) is NotSupportedException, "Source trusted requested plain flags.");
		foreach (var table in new[] { Tables.WikiPage, Tables.WikiByCategory })
			Check(Capture(() => { using var ignored = Batch(store, budget, table); }) is NotSupportedException, "Key reader trusted requested plain flags.");
		Counters(budget, 0, 0, 0, 0);
	}

	[Test]
	public async Task InvalidStoredPageKeyShapeFailsBeforeAdmittingKeyCopy()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.WikiPage, [1], [9]));
		var budget = Budget();
		Check(Capture(() => { using var ignored = Batch(store, budget, Tables.WikiPage); }) is InvalidDataException, "Malformed native page key accepted.");
		Check(budget.Probes == 2 && budget.Records == 0 && budget.WorkingBytes == 0, "Malformed key leaked batch or admitted record.");
	}

	[Test]
	public void InvalidKeyBatchArgumentsRejectBeforeNativeOrOwnerAllocation()
	{
		using var store = Open();
		foreach (var count in new[] { -1, 0, 129 })
		{
			var budget = Budget();
			Check(Capture(() => { using var ignored = Batch(store, budget, count: count); }) is ArgumentOutOfRangeException, "Invalid key count accepted.");
			Counters(budget, 0, 0, 0, 0);
		}
		foreach (var bytes in new[] { -1, 0, KeyCap + 1 })
		{
			var budget = Budget();
			Check(Capture(() => { using var ignored = Batch(store, budget, bytes: bytes); }) is ArgumentOutOfRangeException, "Invalid key byte cap accepted.");
			Counters(budget, 0, 0, 0, 0);
		}
		foreach (var length in new[] { 1, 7, 9 })
		{
			var budget = Budget();
			Check(Capture(() => { using var ignored = Batch(store, budget, Tables.WikiPage, new byte[length]); }) is ArgumentException, "Invalid page resume accepted.");
			Counters(budget, 0, 0, 0, 0);
		}
		var oversize = Budget();
		Check(Capture(() => { using var ignored = Batch(store, oversize, after: new byte[KeyCap + 1]); }) is ArgumentOutOfRangeException, "Oversized category resume accepted.");
		Counters(oversize, 0, 0, 0, 0);
		Check(Capture(() => { using var ignored = Batch(store, null!); }) is ArgumentNullException, "Null key budget accepted.");
	}
}
