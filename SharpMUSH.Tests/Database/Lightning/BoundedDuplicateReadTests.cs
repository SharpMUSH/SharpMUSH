using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>Real variable-DUPSORT reads with literal ordering, exact-key and shared-work oracles.</summary>
public class BoundedDuplicateReadTests
{
	private const string FullRows = "4020:10|4020:1000|4020:80|4020:FF0102";

	private LightningStore Open(string? existing = null)
	{
		// Root owns cleanup after terminal completion and verified native closure. Retain every
		// unique world if construction/disposal fails; an After(Test) hook cannot certify closure.
		var scratch = Path.GetFullPath(Environment.GetEnvironmentVariable("TMPDIR")
			?? Path.Combine(AppContext.BaseDirectory, "work", "snapshot-primitive-tests"));
		if (scratch is "/tmp" or "/var/tmp"
			|| scratch.StartsWith("/tmp/", StringComparison.Ordinal)
			|| scratch.StartsWith("/var/tmp/", StringComparison.Ordinal))
			throw new InvalidOperationException("Bounded duplicate fixtures require disk-backed TMPDIR.");
		Directory.CreateDirectory(scratch);
		var path = existing ?? Path.Combine(scratch, "duplicates-" + Guid.NewGuid().ToString("N"));
		return new LightningStore(new LightningStoreOptions { Path = path, MapSize = 256L << 20 });
	}

	// Fixture allowances, not a proposed authorization request envelope.
	private static NativeReadBudget Budget(int probes = 1024, long bytes = 1 << 20,
		long working = 1 << 20, int? records = null, CancellationToken ct = default)
		=> new(probes, bytes, working, ct, records);

	private static IBoundedDuplicateReadTx Checked(ITx tx) =>
		tx as IBoundedDuplicateReadTx ?? throw new InvalidOperationException("Native bounded duplicate capability is required.");

	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}

	private static string Rows(NativeReadBatch batch) =>
		string.Join('|', batch.Entries.Select(row => Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value)));

	private static async Task<TableDef> Seed(LightningStore store)
	{
		var table = store.OpenTable("bounded.duplicates", duplicates: true);
		await store.WriteAsync(tx =>
		{
			// Adjacent keys must never enter the requested run. Values are deliberately unsorted,
			// have different lengths and cross the signed-byte boundary.
			tx.Put(table, [0x40, 0x21], [0x00]);
			tx.Put(table, [0x40, 0x20], [0xFF, 0x01, 0x02]);
			tx.Put(table, [0x40, 0x20], [0x80]);
			tx.Put(table, [0x40, 0x1F], [0xFF]);
			tx.Put(table, [0x40, 0x20], [0x10, 0x00]);
			tx.Put(table, [0x40, 0x20], [0x10]);
		});
		return table;
	}

	[Test]
	[Arguments(null, FullRows, 6, 15L, 4)]
	[Arguments("", FullRows, 6, 15L, 4)]
	[Arguments("01", FullRows, 6, 15L, 4)]
	[Arguments("10", "4020:1000|4020:80|4020:FF0102", 6, 15L, 3)]
	[Arguments("1000", "4020:80|4020:FF0102", 5, 12L, 2)]
	[Arguments("1080", "4020:80|4020:FF0102", 4, 8L, 2)]
	[Arguments("80", "4020:FF0102", 4, 8L, 1)]
	[Arguments("FF0102", "", 3, 5L, 0)]
	[Arguments("FFFF", "", 1, 0L, 0)]
	public async Task ExactRunAndStrictContinuationHaveLiteralOrderAndAccounting(
		string? afterHex, string expected, int probes, long bytes, int records)
	{
		using var store = Open();
		var table = await Seed(store);
		var budget = Budget();
		var after = afterHex is null ? null : Convert.FromHexString(afterHex);
		using (var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], after, budget)))
		{
			await Assert.That(Rows(batch)).IsEqualTo(expected);
			await Assert.That(batch.Exhausted).IsTrue();
			await Assert.That(budget.Probes).IsEqualTo(probes);
			await Assert.That(budget.Bytes).IsEqualTo(bytes);
			await Assert.That(budget.Records).IsEqualTo(records);
			await Assert.That(budget.WorkingBytes).IsGreaterThan(0L);
			if (after is not null) await Assert.That(Convert.ToHexString(after)).IsEqualTo(afterHex);
		}
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
		var unchanged = store.Read(tx => string.Join('|', tx.Dups(table, [0x40, 0x20]).Select(Convert.ToHexString)));
		await Assert.That(unchanged).IsEqualTo("10|1000|80|FF0102");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MissingExactKeyDoesNotAdmitItsNeighbors(bool resume)
	{
		using var store = Open();
		var table = store.OpenTable("bounded.missing", duplicates: true);
		await store.WriteAsync(tx =>
		{
			tx.Put(table, [0x40, 0x1F], [0xFF]);
			tx.Put(table, [0x40, 0x21], [0x00]);
		});
		var budget = Budget();
		using (var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], resume ? [0x10] : null, budget)))
		{
			await Assert.That(batch.Entries.Length).IsEqualTo(0);
			await Assert.That(batch.Exhausted).IsTrue();
		}
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((1, 0L, 0, 0L));
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NullAndEmptyContinuationAlsoReadSingletonInlineValue(bool emptyContinuation)
	{
		using var store = Open();
		var table = store.OpenTable("bounded.singleton", duplicates: true);
		await store.WriteAsync(tx => tx.Put(table, [0x23], [0x80, 0x01]));
		var budget = Budget();
		using (var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x23], emptyContinuation ? [] : null, budget)))
		{
			await Assert.That(Rows(batch)).IsEqualTo("23:8001");
			await Assert.That(batch.Exhausted).IsTrue();
		}
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((3, 3L, 1, 0L));
	}

	[Test]
	[Arguments(128, 132, 258L, "")]
	[Arguments(129, 133, 260L, "44:81")]
	public async Task Local128BoundaryContinuesInsideTheSameCallbackAndBudget(int rows, int probes, long bytes, string tail)
	{
		using var store = Open();
		var table = store.OpenTable("bounded.boundary", duplicates: true);
		await store.WriteAsync(tx =>
		{
			for (var i = rows; i > 0; i--) tx.Put(table, [0x44], [(byte)i]);
		});
		var budget = Budget();
		var observed = store.Read(tx =>
		{
			var capability = Checked(tx);
			using var first = capability.ReadDuplicates(table, [0x44], null, budget);
			var firstValues = Convert.ToHexString(first.Entries.SelectMany(entry => entry.Value).ToArray());
			var firstKeys = first.Entries.All(entry => entry.Key.AsSpan().SequenceEqual(new byte[] { 0x44 }));
			var initial = (Count: first.Entries.Length, first.Exhausted, budget.Probes, budget.Bytes, budget.Records);
			first.Dispose();
			var betweenWorking = budget.WorkingBytes;
			using var second = capability.ReadDuplicates(table, [0x44], [0x80], budget);
			return (firstValues, firstKeys, initial, betweenWorking, Tail: Rows(second), second.Exhausted);
		});
		await Assert.That(observed.firstValues).IsEqualTo(
			"0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20" +
			"2122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F40" +
			"4142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D5E5F60" +
			"6162636465666768696A6B6C6D6E6F707172737475767778797A7B7C7D7E7F80");
		await Assert.That(observed.firstKeys).IsTrue();
		await Assert.That(observed.initial).IsEqualTo((128, false, 129, 256L, 128));
		await Assert.That(observed.betweenWorking).IsEqualTo(0L);
		await Assert.That(observed.Tail).IsEqualTo(tail);
		await Assert.That(observed.Exhausted).IsTrue();
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((probes, bytes, rows, 0L));
	}

	[Test]
	public async Task Shared128RecordLimitRefusesThe129thInsteadOfReturningTruncation()
	{
		using var store = Open();
		var table = store.OpenTable("bounded.shared-records", duplicates: true);
		await store.WriteAsync(tx =>
		{
			for (var i = 129; i > 0; i--) tx.Put(table, [0x44], [(byte)i]);
		});
		var budget = Budget(records: 128);
		var observed = store.Read(tx =>
		{
			var capability = Checked(tx);
			using var first = capability.ReadDuplicates(table, [0x44], null, budget);
			var initial = (first.Entries.Length, first.Exhausted);
			first.Dispose();
			var error = Capture(() => { using var next = capability.ReadDuplicates(table, [0x44], [0x80], budget); });
			return (initial, error);
		});
		await Assert.That(observed.initial).IsEqualTo((128, false));
		await Assert.That(observed.error is NativeReadLimitException { Dimension: "records" }).IsTrue();
		// Resume candidate 80 is inspected/skipped; candidate 81 is inspected but cannot be admitted.
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((132, 260L, 128, 0L));
	}

	[Test]
	[Arguments("probes", 1, 100L, null, null, 1, 0L, 0)]
	[Arguments("probes", 2, 100L, null, null, 2, 3L, 1)]
	[Arguments("probes", 5, 100L, null, null, 5, 15L, 4)]
	[Arguments("probes", 2, 100L, null, "10", 2, 3L, 0)]
	[Arguments("bytes", 100, 3L, null, null, 3, 3L, 1)]
	[Arguments("records", 100, 100L, 1, null, 3, 7L, 1)]
	public async Task SharedLimitsThrowAndReleaseCopiesWithExactCumulativeCharges(
		string dimension, int maxProbes, long maxBytes, int? maxRecords, string? afterHex,
		int probes, long bytes, int records)
	{
		using var store = Open();
		var table = await Seed(store);
		var budget = Budget(maxProbes, maxBytes, records: maxRecords);
		var after = afterHex is null ? null : Convert.FromHexString(afterHex);
		var error = Capture(() =>
		{
			using var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], after, budget));
		});
		await Assert.That(error is NativeReadLimitException limit && limit.Dimension == dimension).IsTrue();
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((probes, bytes, records, 0L));
		using var fresh = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, Budget()));
		await Assert.That(Rows(fresh)).IsEqualTo(FullRows);
	}

	[Test]
	[Arguments(128L)]
	[Arguments(1400L)]
	[Arguments(1900L)]
	public async Task WorkingLimitRefusesBeforeStagingEntryOrFinalOverlapAndCleansUp(long working)
	{
		using var store = Open();
		var table = await Seed(store);
		var budget = Budget(working: working);
		var error = Capture(() =>
		{
			using var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, budget));
		});
		await Assert.That(error is NativeReadLimitException { Dimension: "working-bytes" }).IsTrue();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
		// Accepted accounting: staging+lease1312; each short copied entry144;
		// four entries1888; final immutable reference-array overlap64.
		var expected = working switch
		{
			128 => (0, 0L, 0),
			1400 => (2, 3L, 1),
			_ => (6, 15L, 4)
		};
		await Assert.That((budget.Probes, budget.Bytes, budget.Records)).IsEqualTo(expected);
		using var fresh = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, Budget()));
		await Assert.That(Rows(fresh)).IsEqualTo(FullRows);
	}

	[Test]
	public async Task FailedDuplicateReadSharesPointRecordAllowanceAndRetainsPriorOwner()
	{
		using var store = Open();
		var table = await Seed(store);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [9], [7]));
		var budget = Budget(records: 2);
		var observed = store.Read(tx =>
		{
			var points = tx as IBoundedReadTx ?? throw new InvalidOperationException("Plain bounded capability is required.");
			if (points.ReadValue(Tables.Meta, [9], budget) is not NativeReadValue prior)
				throw new InvalidOperationException("The literal prior record was absent.");
			try
			{
				var owned = budget.WorkingBytes;
				var error = Capture(() => { using var batch = Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, budget); });
				return (prior, owned, Remaining: budget.WorkingBytes, error);
			}
			catch { prior.Dispose(); throw; }
		});
		using (observed.prior)
		{
			await Assert.That(observed.error is NativeReadLimitException { Dimension: "records" }).IsTrue();
			await Assert.That(observed.Remaining).IsEqualTo(observed.owned);
			await Assert.That(observed.prior.Value[0]).IsEqualTo((byte)7);
			await Assert.That((budget.Probes, budget.Bytes, budget.Records)).IsEqualTo((4, 9L, 2));
		}
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

	[Test]
	public async Task ActualFlagsWinOverCounterfeitDefinitionsAndPersistedReopen()
	{
		string path;
		using (var store = Open())
		{
			path = store.Path;
			var variable = await Seed(store);
			using var accepted = store.Read(tx => Checked(tx).ReadDuplicates(TableDef.Node(variable.Name), [0x40, 0x20], null, Budget()));
			await Assert.That(Rows(accepted)).IsEqualTo(FullRows);
			foreach (var rejected in new[]
			{
				Tables.Meta,
				TableDef.Index(Tables.Meta.Name, duplicates: true),
				Tables.RevLocation,
				TableDef.Index(Tables.RevLocation.Name, duplicates: true, fixedDuplicates: false)
			})
			{
				var budget = Budget();
				var error = Capture(() => { using var batch = store.Read(tx => Checked(tx).ReadDuplicates(rejected, [1], null, budget)); });
				await Assert.That(error is NotSupportedException).IsTrue();
				await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((0, 0L, 0, 0L));
			}
		}
		using var reopened = Open(path);
		var declaredPlain = reopened.OpenTable("bounded.duplicates", duplicates: false);
		using var persisted = reopened.Read(tx => Checked(tx).ReadDuplicates(declaredPlain, [0x40, 0x20], null, Budget()));
		await Assert.That(Rows(persisted)).IsEqualTo(FullRows);
	}

	[Test]
	public async Task UnknownTableDoesNotCreateOrChargeACursor()
	{
		using var store = Open();
		var budget = Budget();
		var error = Capture(() =>
		{
			using var batch = store.Read(tx => Checked(tx).ReadDuplicates(TableDef.Index("bounded.unknown", duplicates: true), [1], null, budget));
		});
		await Assert.That(error is KeyNotFoundException).IsTrue();
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((0, 0L, 0, 0L));
	}

	[Test]
	public async Task PrecancellationPreservesTokenWithoutNativeWork()
	{
		using var store = Open();
		var table = await Seed(store);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var budget = Budget(ct: cancellation.Token);
		var error = Capture(() => { using var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, budget)); });
		await Assert.That(error is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token).IsTrue();
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((0, 0L, 0, 0L));
		using var fresh = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, Budget()));
		await Assert.That(Rows(fresh)).IsEqualTo(FullRows);
	}

	[Test]
	public async Task NativeInvalidKeyRemainsAnErrorAndReleasesCursorAccounting()
	{
		using var store = Open();
		var table = await Seed(store);
		var budget = Budget();
		var error = Capture(() => { using var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [], null, budget)); });
		await Assert.That(error is LightningStoreException { Code: MDBResultCode.BadValSize }).IsTrue();
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((1, 0L, 0, 0L));
		using var fresh = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, Budget()));
		await Assert.That(Rows(fresh)).IsEqualTo(FullRows);
	}

	[Test]
	public async Task CopiedBatchOutlivesCallbackAndOnlyReleasesItsOwnReservation()
	{
		using var store = Open();
		var table = await Seed(store);
		var budget = Budget();
		using var inputOwner = budget.ReserveWorking(32);
		var baseline = budget.WorkingBytes;
		using var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, budget));
		await Assert.That(budget.WorkingBytes).IsGreaterThan(baseline);
		await store.WriteAsync(tx =>
		{
			tx.Delete(table, [0x40, 0x20], [0x10]);
			tx.Put(table, [0x40, 0x20], [0x05]);
		});
		await Assert.That(Rows(batch)).IsEqualTo(FullRows);
		batch.Dispose();
		batch.Dispose();
		await Assert.That(() => batch.Entries).Throws<ObjectDisposedException>();
		await Assert.That(budget.WorkingBytes).IsEqualTo(baseline);
		await Assert.That((budget.Probes, budget.Bytes, budget.Records)).IsEqualTo((6, 15L, 4));
		inputOwner.Dispose();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task EscapedCapabilityRejectsBeforeChargesEvenDuringAFreshCallback(bool callbackThrows)
	{
		using var store = Open();
		var table = await Seed(store);
		IBoundedDuplicateReadTx? escaped = null;
		var sentinel = new InvalidOperationException("callback failure");
		var initial = Capture(() => store.Read(tx =>
		{
			escaped = Checked(tx);
			if (callbackThrows) throw sentinel;
			return 0;
		}));
		await Assert.That(ReferenceEquals(initial, callbackThrows ? sentinel : null)).IsTrue();
		var capability = escaped ?? throw new InvalidOperationException("Read callback did not expose the capability.");
		var budget = Budget();
		var error = Capture(() => { using var batch = capability.ReadDuplicates(table, [0x40, 0x20], null, budget); });
		await Assert.That(error is ObjectDisposedException).IsTrue();
		var next = store.Read(tx =>
		{
			var stale = Capture(() => { using var batch = capability.ReadDuplicates(table, [0x40, 0x20], null, budget); });
			using var current = Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, Budget());
			return (stale, Current: Rows(current));
		});
		await Assert.That(next.stale is ObjectDisposedException).IsTrue();
		await Assert.That(next.Current).IsEqualTo(FullRows);
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((0, 0L, 0, 0L));
	}

	[Test]
	public async Task WriteWrapperRejectsReadOnlyDuplicateCapabilityWithoutCharges()
	{
		using var store = Open();
		var table = await Seed(store);
		var budget = Budget();
		var error = await store.WriteAsync(tx => Capture(() =>
		{
			using var batch = Checked(tx).ReadDuplicates(table, [0x40, 0x20], null, budget);
		}));
		await Assert.That(error is NotSupportedException).IsTrue();
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes)).IsEqualTo((0, 0L, 0, 0L));
	}
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LocalByteCapChargesLookaheadAndStrictResumeOrSharedRefusal(bool sharedCap)
	{
		using var store = Open();
		var table = store.OpenTable("bounded.byte-cap", duplicates: true);
		await store.WriteAsync(tx =>
		{
			for (var i = 65; i > 0; i--)
			{
				var value = new byte[2048];
				value[0] = (byte)i;
				tx.Put(table, [0x44], value);
			}
		});
		var budget = Budget(bytes: sharedCap ? 128 * 1024 : 1 << 20);
		if (sharedCap)
		{
			var error = Capture(() =>
			{
				using var batch = store.Read(tx => Checked(tx).ReadDuplicates(table, [0x44], null, budget));
			});
			await Assert.That(error is NativeReadLimitException { Dimension: "bytes" }).IsTrue();
			// 63 complete pairs, then the rejected lookahead's checked one-byte key.
			await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes))
				.IsEqualTo((65, 129088L, 63, 0L));
			return;
		}

		using var inputOwner = budget.ReserveWorking(32L + 2048);
		var resume = new byte[2048];
		resume[0] = 63;
		var baseline = budget.WorkingBytes;
		var observed = store.Read(tx =>
		{
			var capability = Checked(tx);
			using var first = capability.ReadDuplicates(table, [0x44], null, budget);
			var initial = (first.Entries.Length, first.Exhausted, budget.Probes, budget.Bytes, budget.Records);
			var prefix = Convert.ToHexString(first.Entries.Select(entry => entry.Value[0]).ToArray());
			var validFirst = first.Entries.All(entry => entry.Key.AsSpan().SequenceEqual(new byte[] { 0x44 })
				&& entry.Value.Length == 2048 && entry.Value.AsSpan(1).IndexOfAnyExcept((byte)0) < 0);
			first.Dispose();
			var betweenWorking = budget.WorkingBytes;
			using var second = capability.ReadDuplicates(table, [0x44], resume, budget);
			var suffix = Convert.ToHexString(second.Entries.Select(entry => entry.Value[0]).ToArray());
			var validSecond = second.Entries.All(entry => entry.Key.AsSpan().SequenceEqual(new byte[] { 0x44 })
				&& entry.Value.Length == 2048 && entry.Value.AsSpan(1).IndexOfAnyExcept((byte)0) < 0);
			return (initial, prefix, validFirst, betweenWorking, suffix, validSecond, second.Exhausted);
		});
		await Assert.That(observed.initial).IsEqualTo((63, false, 65, 131136L, 63));
		await Assert.That(observed.prefix).IsEqualTo(
			"0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20" +
			"2122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F");
		await Assert.That(observed.suffix).IsEqualTo("4041");
		await Assert.That(observed.validFirst && observed.validSecond && observed.Exhausted).IsTrue();
		await Assert.That(observed.betweenWorking).IsEqualTo(baseline);
		await Assert.That((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes))
			.IsEqualTo((70, 137283L, 65, baseline));
		inputOwner.Dispose();
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

}
