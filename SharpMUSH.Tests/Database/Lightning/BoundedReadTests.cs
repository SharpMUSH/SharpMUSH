using System.Collections.Concurrent;
using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class BoundedReadTests
{
	private readonly List<string> _paths = [];
	private const int Cap = 128 * 1024;

	private LightningStore Open(string? existing = null)
	{
		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Combine(AppContext.BaseDirectory, "work");
		Directory.CreateDirectory(scratch);
		var path = existing ?? Path.Combine(scratch, "sharpmush-bounded-" + Guid.NewGuid().ToString("N"));
		if (existing is null) _paths.Add(path);
		return new LightningStore(new LightningStoreOptions { Path = path, MapSize = 256L << 20 });
	}

	[After(Test)]
	public void Cleanup()
	{
		foreach (var path in _paths.Where(Directory.Exists)) Directory.Delete(path, recursive: true);
	}

	private static NativeReadBudget Budget(int probes = 8192, long bytes = 2 << 20, long working = 4 << 20, CancellationToken ct = default)
		=> new(probes, bytes, working, ct);

	private static IBoundedReadTx Checked(ITx tx) => tx as IBoundedReadTx ?? throw new InvalidOperationException("Native checked-read capability is required.");
	private static NativeReadBatch Range(LightningStore store, NativeReadBudget budget, byte[]? prefix = null, byte[]? start = null, byte[]? end = null, byte[]? after = null)
		=> store.Read(tx => Checked(tx).ReadRange(Tables.Meta, prefix ?? [], start ?? [], end ?? [], after ?? [], budget));

	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OversizedValueRefusedBeforeCopy(bool range)
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], new byte[4 << 20]));
		void ReadOversize()
		{
			var budget = Budget();
			if (range) { using var batch = Range(store, budget); }
			else if (store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget)) is NativeReadValue value) value.Dispose();
		}
		for (var i = 0; i < 4; i++) Capture(ReadOversize);
		var before = GC.GetAllocatedBytesForCurrentThread();
		var error = Capture(ReadOversize);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Console.WriteLine($"oversize range={range}: allocated={allocated}");
		await Assert.That(allocated).IsLessThan(64L << 10);
		await Assert.That(error is NativeReadLimitException { Dimension: "record-bytes" }).IsTrue();
	}

	[Test]
	public async Task NativeLengthBindingReadsZeroSmallAndLargeValues()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], []); tx.Put(Tables.Meta, [2], [9, 8, 7]); tx.Put(Tables.Meta, [3], new byte[2 << 20]); });
		var budget = Budget();
		using var zero = store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget)).Expect<NativeReadValue>();
		using var small = store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [2], budget)).Expect<NativeReadValue>();
		await Assert.That(zero.Value.Length).IsEqualTo(0);
		await Assert.That(small.Value).IsEquivalentTo(new byte[] { 9, 8, 7 });
		await Assert.That(budget.Bytes).IsEqualTo(5L);
		await Assert.That(() => store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [3], budget))).Throws<NativeReadLimitException>();
	}

	[Test]
	public async Task MissingPointsConsumeBudget()
	{
		using var store = Open();
		var budget = Budget(probes: 2);
		await Assert.That(store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget)) is NotFound).IsTrue();
		await Assert.That(store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [2], budget)) is NotFound).IsTrue();
		await Assert.That(() => store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [3], budget))).Throws<NativeReadLimitException>();
		await Assert.That(budget.Probes).IsEqualTo(2);
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

	[Test]
	public async Task BoundaryAndResumeConsumeBudget()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], [7]); tx.Put(Tables.Meta, [2], new byte[2 << 20]); });
		var budget = Budget();
		using var batch = Range(store, budget, end: [2], after: [1]);
		await Assert.That(batch.Entries.Length).IsEqualTo(0);
		await Assert.That(batch.Exhausted).IsTrue();
		// SetRange(1), inspect resume key, Next(), inspect excluded boundary key; no value inspection.
		await Assert.That(budget.Probes).IsEqualTo(4);
		await Assert.That(budget.Bytes).IsEqualTo(2L);
	}

	[Test]
	public async Task BatchCapsAndStrictResume()
	{
		using var store = Open();
		await store.WriteAsync(tx => { for (var i = 1; i <= 129; i++) tx.Put(Tables.Meta, [(byte)i], [(byte)i]); });
		var budget = Budget();
		using var first = Range(store, budget);
		await Assert.That(first.Entries.Length).IsEqualTo(128);
		await Assert.That(first.Exhausted).IsFalse();
		await Assert.That(budget.Probes).IsEqualTo(256); // seek + GetCurrent + 127*(advance + GetCurrent)
		await Assert.That(budget.Records).IsEqualTo(128);
		using var second = Range(store, budget, after: first.Entries[^1].Key);
		await Assert.That(second.Entries.Length).IsEqualTo(1);
		await Assert.That(second.Entries[0].Key[0]).IsEqualTo((byte)129);
		await Assert.That(second.Exhausted).IsTrue();
		await Assert.That(budget.Records).IsEqualTo(129); // the original constructor imposes no shared 128-record cap
	}

	[Test]
	public async Task OptionalRecordAllowanceIsSharedAcrossPointAndRangeAndRetainsPriorOwner()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], [7]); tx.Put(Tables.Meta, [2], [8]); tx.Put(Tables.Meta, [3], [9]); });
		var budget = new NativeReadBudget(128, Cap, 4 << 20, CancellationToken.None, maxRecords: 2);
		using var prior = store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget)).Expect<NativeReadValue>();
		var owned = budget.WorkingBytes;
		var error = Capture(() => { using var batch = Range(store, budget, start: [2]); });
		await Assert.That(error is NativeReadLimitException { Dimension: "records" }).IsTrue();
		await Assert.That(budget.Records).IsEqualTo(2);
		await Assert.That(budget.Probes).IsEqualTo(5);
		await Assert.That(budget.Bytes).IsEqualTo(6L);
		await Assert.That(budget.WorkingBytes).IsEqualTo(owned);
		await Assert.That(prior.Value[0]).IsEqualTo((byte)7);
		var pointError = Capture(() => store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [3], budget)));
		await Assert.That(pointError is NativeReadLimitException { Dimension: "records" }).IsTrue();
		await Assert.That(budget.Records).IsEqualTo(2);
		await Assert.That(budget.Probes).IsEqualTo(6);
		await Assert.That(budget.Bytes).IsEqualTo(8L);
		await Assert.That(budget.WorkingBytes).IsEqualTo(owned);
		var counters = (budget.Records, budget.Probes, budget.Bytes, budget.WorkingBytes);
		await Assert.That(Capture(budget.ChargeRecord) is NativeReadLimitException { Dimension: "records" }).IsTrue();
		await Assert.That((budget.Records, budget.Probes, budget.Bytes, budget.WorkingBytes)).IsEqualTo(counters);
		await Assert.That(store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [9], budget)) is NotFound).IsTrue();
		await Assert.That(budget.Records).IsEqualTo(2);
		await Assert.That(budget.Probes).IsEqualTo(7);
		prior.Dispose();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NulNamesRejectBeforeOrdinaryOpenAndCheckedLookup(bool wikiPage)
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], [7]));
		var alias = TableDef.Node((wikiPage ? Tables.WikiPage.Name : Tables.Meta.Name) + "\0plugin");
		await Assert.That(() => store.OpenTable(alias.Name, false)).Throws<ArgumentException>();
		await Assert.That(() => store.Read(tx => tx.Count(alias))).Throws<ArgumentException>();
		var budget = Budget();
		await Assert.That(() => store.Read(tx => Checked(tx).ReadValue(alias, [1], budget))).Throws<ArgumentException>();
		await Assert.That(() => store.Read(tx => Checked(tx).ReadRange(alias, [], [], [], [], budget))).Throws<ArgumentException>();
		await Assert.That(budget.Probes).IsEqualTo(0);
		await Assert.That(budget.Records).IsEqualTo(0);
		await Assert.That(budget.Bytes).IsEqualTo(0L);
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
		var valid = store.OpenTable("bounded.valid.\u03bb", false);
		await store.WriteAsync(tx => tx.Put(valid, [1], [8]));
		using var plugin = store.Read(tx => Checked(tx).ReadValue(valid, [1], budget)).Expect<NativeReadValue>();
		using var core = store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget)).Expect<NativeReadValue>();
		await Assert.That(plugin.Value[0]).IsEqualTo((byte)8);
		await Assert.That(core.Value[0]).IsEqualTo((byte)7);
	}

	[Test]
	[Arguments(-1)]
	[Arguments(0)]
	[Arguments(1)]
	public async Task SingleRecordByteBoundary(int adjustment)
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], new byte[Cap - 1 + adjustment]));
		var budget = Budget();
		if (adjustment > 0)
		{
			await Assert.That(() => Range(store, budget)).Throws<NativeReadLimitException>();
			await Assert.That(() => store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], Budget()))).Throws<NativeReadLimitException>();
		}
		else
		{
			using var batch = Range(store, budget);
			await Assert.That(batch.Entries.Length).IsEqualTo(1);
			await Assert.That(batch.Entries[0].Value.Length).IsEqualTo(Cap - 1 + adjustment);
			using var point = store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], Budget())).Expect<NativeReadValue>();
			await Assert.That(point.Value.Length).IsEqualTo(Cap - 1 + adjustment);
		}
	}

	[Test]
	public async Task CancellationBeforeNativeAccess()
	{
		using var store = Open();
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var budget = Budget(ct: cts.Token);
		await Assert.That(() => Range(store, budget)).Throws<OperationCanceledException>();
		await Assert.That(() => store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget))).Throws<OperationCanceledException>();
		await Assert.That(budget.Probes).IsEqualTo(0);
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

	[Test]
	public async Task DuplicateTableRejected()
	{
		using var store = Open();
		var aliases = new[] { Tables.RevLocation, TableDef.Node(Tables.RevLocation.Name) };
		foreach (var table in aliases)
		{
			var budget = Budget();
			await Assert.That(() => store.Read(tx => Checked(tx).ReadValue(table, [1], budget))).Throws<NotSupportedException>();
			await Assert.That(() => store.Read(tx => Checked(tx).ReadRange(table, [], [], [], [], budget))).Throws<NotSupportedException>();
			await Assert.That(budget.Probes).IsEqualTo(0);
		}
	}

	[Test]
	public async Task PersistedDuplicateFlagsCannotBeMisdeclared()
	{
		string path;
		using (var store = Open())
		{
			path = store.Path;
			var table = store.OpenTable("bounded.persisted", true);
			await store.WriteAsync(tx => tx.Put(table, [1], [2]));
		}
		using var reopened = Open(path);
		var requestedPlain = reopened.OpenTable("bounded.persisted", false);
		var budget = Budget();
		await Assert.That(() => reopened.Read(tx => Checked(tx).ReadValue(requestedPlain, [1], budget))).Throws<NotSupportedException>();
		await Assert.That(budget.Probes).IsEqualTo(0);
	}

	[Test]
	public async Task HandleAndFlagsPublishTogether()
	{
		using var store = Open();
		var available = new ConcurrentQueue<TableDef>();
		var done = 0;
		var producer = Task.Run(() =>
		{
			try { for (var i = 0; i < 24; i++) available.Enqueue(store.OpenTable($"bounded.dynamic.{i}", i % 2 == 0)); }
			finally { Volatile.Write(ref done, 1); }
		});
		var observations = 0;
		try
		{
			while (Volatile.Read(ref done) == 0 || !available.IsEmpty)
			{
				if (!available.TryDequeue(out var table)) { await Task.Yield(); continue; }
				var budget = Budget();
				if (table.Duplicates)
					await Assert.That(() => store.Read(tx => Checked(tx).ReadValue(TableDef.Node(table.Name), [1], budget))).Throws<NotSupportedException>();
				else
					await Assert.That(store.Read(tx => Checked(tx).ReadValue(table, [1], budget)) is NotFound).IsTrue();
				observations++;
			}
		}
		finally
		{
			// Even an assertion failure must observe native writer completion before disposing its store.
			await producer;
		}
		await Assert.That(observations).IsEqualTo(24);
	}

	[Test]
	public async Task TwoLargeValidRowsMakeBatchProgress()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], new byte[70 << 10]); tx.Put(Tables.Meta, [2], new byte[70 << 10]); });
		var budget = Budget();
		using var first = Range(store, budget);
		await Assert.That(first.Entries.Length).IsEqualTo(1);
		await Assert.That(first.Exhausted).IsFalse();
		await Assert.That(budget.Bytes).IsEqualTo((70L << 10) + 2); // second key inspected, value not copied/charged
		using var second = Range(store, budget, after: first.Entries[0].Key);
		await Assert.That(second.Entries.Length).IsEqualTo(1);
		await Assert.That(second.Entries[0].Key[0]).IsEqualTo((byte)2);
	}

	[Test]
	public async Task HardBudgetAfterOneRowThrows()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], [4]); tx.Put(Tables.Meta, [2], [5]); });
		var budget = Budget(bytes: 3);
		await Assert.That(() => Range(store, budget)).Throws<NativeReadLimitException>();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
		await Assert.That(budget.Bytes).IsEqualTo(3L);
	}

	[Test]
	public async Task OwnerDisposalReleasesExactlyOnce()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], [5]));
		var budget = Budget();
		var value = store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget)).Expect<NativeReadValue>();
		var charged = budget.WorkingBytes;
		await Assert.That(charged).IsGreaterThan(1L);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], [8]));
		await Assert.That(value.Value[0]).IsEqualTo((byte)5);
		value.Dispose();
		value.Dispose();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
		await Assert.That(() => value.Value).Throws<ObjectDisposedException>();
		await Assert.That(budget.Bytes).IsEqualTo(2L);
	}

	[Test]
	public async Task PriorOwnerSurvivesFailedRead()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], [7]); tx.Put(Tables.Meta, [2], [8]); });
		var budget = Budget(bytes: 5);
		using var prior = store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [1], budget)).Expect<NativeReadValue>();
		var owned = budget.WorkingBytes;
		await Assert.That(() => Range(store, budget)).Throws<NativeReadLimitException>();
		await Assert.That(budget.WorkingBytes).IsEqualTo(owned);
		await Assert.That(prior.Value[0]).IsEqualTo((byte)7);
	}

	[Test]
	public async Task FailedReadReleasesOnlyItsCopies()
	{
		using var store = Open();
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], [7]); tx.Put(Tables.Meta, [2], [8]); });
		var budget = Budget(working: 128);
		await Assert.That(() => Range(store, budget)).Throws<NativeReadLimitException>();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

	[Test]
	public async Task RangeCombinesPrefixIntervalAndStrictResume()
	{
		using var store = Open();
		await store.WriteAsync(tx => { for (byte i = 1; i <= 5; i++) tx.Put(Tables.Meta, [2, i], [i]); tx.Put(Tables.Meta, [3], [9]); });
		using var batch = Range(store, Budget(), prefix: [2], start: [2, 2], end: [2, 5], after: [2, 3]);
		await Assert.That(batch.Entries.Length).IsEqualTo(1);
		await Assert.That(batch.Entries[0].Value[0]).IsEqualTo((byte)4);
		await Assert.That(batch.Exhausted).IsTrue();
	}

	[Test]
	public async Task NativeErrorsDoNotBecomeEmptySuccess()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], [2]));
		var error = Capture(() => store.Read(tx => Checked(tx).ReadValue(Tables.Meta, [], Budget())));
		await Assert.That(error is LightningStoreException { Code: MDBResultCode.BadValSize }).IsTrue();
	}

	[Test]
	public async Task ReservationsCheckLimitsOverflowAndDisposal()
	{
		await Assert.That(() => Budget(probes: 0)).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => Budget(bytes: -1)).Throws<ArgumentOutOfRangeException>();
		var budget = Budget(working: long.MaxValue, bytes: long.MaxValue);
		using var reservation = budget.ReserveWorking(long.MaxValue - 64);
		await Assert.That(() => reservation.ReserveAdditional(1)).Throws<NativeReadLimitException>();
		budget.ChargeBytes(long.MaxValue);
		await Assert.That(() => budget.ChargeBytes(1)).Throws<NativeReadLimitException>();
		reservation.Dispose();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
		await Assert.That(() => reservation.ReserveAdditional(1)).Throws<ObjectDisposedException>();
	}
}
