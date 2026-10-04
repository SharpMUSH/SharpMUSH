using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

public class StreamingTests
{
	private readonly List<string> _paths = [];

	private LightningStore Open()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return new LightningStore(new LightningStoreOptions { Path = path, MapSize = 256L << 20 });
	}

	[After(Test)]
	public Task Cleanup()
	{
		foreach (var path in _paths.Where(Directory.Exists))
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}

		return Task.CompletedTask;
	}

	[Test]
	public async Task RangeAsyncResumesAcrossPagesWhileWritesLand()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			for (var i = 0; i < 1000; i++) tx.Put(Tables.AttrMeta, Keys.Attr(1, $"A{i:D4}"), Keys.Str("v"));
		});
		var seen = new List<string>();
		var i = 0;
		await foreach (var entry in store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(1), pageSize: 100))
		{
			seen.Add(Keys.ParseAttr(entry.Key).LongName);
			if (i++ == 150) await store.WriteAsync(tx => tx.Put(Tables.AttrMeta, Keys.Attr(1, "A0500X"), Keys.Str("late")));
		}
		await Assert.That(seen.Count).IsEqualTo(1001);
		await Assert.That(seen).IsInOrder();
		await Assert.That(seen.Distinct().Count()).IsEqualTo(1001);
	}

	/// <summary>
	/// Exercises the duplicate-table resumption path that <see cref="RangeAsyncResumesAcrossPagesWhileWritesLand"/>
	/// never touches: <c>Tables.SessionAccount</c> is a duplicate-values table (<c>TableDef.Duplicates</c>
	/// true), so <c>RangeAsync</c> must resume with <c>afterValue</c> set, and <c>Tx.RangeFrom</c> must
	/// seek the cursor to <c>afterKey</c> with <c>SetRange</c> and then step forward through that key's
	/// duplicate run while <c>entry.Value &lt;= afterValue</c>, so only what was already yielded within the
	/// run is skipped. Three keys carry 40 duplicate values each; pageSize 25 puts every page boundary
	/// mid-duplicate-run for at least one key.
	/// </summary>
	[Test]
	public async Task RangeAsyncResumesAcrossDuplicateValuesWithinAKey()
	{
		using var store = Open();
		long[] accountIds = [1001, 1002, 1003];
		// Dbref keys are 8-byte big-endian; these three share their top 6 bytes (all zero, since
		// every id is under 65536), so that shared span is a real, non-empty prefix covering all
		// three keys and nothing else written to the table.
		var prefix = new byte[6];
		var expected = new List<string>();
		await store.WriteAsync(tx =>
		{
			foreach (var accountId in accountIds)
			{
				for (var j = 0; j < 40; j++)
				{
					var token = $"T{j:D3}";
					tx.Put(Tables.SessionAccount, Keys.Dbref(accountId), Keys.Str(token));
					expected.Add($"{accountId}:{token}");
				}
			}
		});
		var seen = new List<string>();
		await foreach (var entry in store.RangeAsync(Tables.SessionAccount, prefix, pageSize: 25))
		{
			seen.Add($"{Keys.ReadDbref(entry.Key)}:{Keys.ReadStr(entry.Value)}");
		}
		await Assert.That(seen.Count).IsEqualTo(120);
		await Assert.That(seen.Distinct().Count()).IsEqualTo(120);
		await Assert.That(string.Join(",", seen)).IsEqualTo(string.Join(",", expected));
	}

	/// <summary>
	/// A mid-table start streamed to the end of the table, paged: every page after the first resumes on
	/// the last key it yielded, and the resumption must be a seek to that key — not a rescan of everything
	/// before it. Correctness (the rows at or after the start, in order, none before) does not distinguish
	/// the two, so the cost is pinned as well: a rescan re-copies every already-yielded row on every page,
	/// which is quadratic and shows up directly as allocation per row returned. Pre-fix this allocates
	/// upwards of 2 KB per row against the 200-byte bound below; the seek allocates the row's own key and
	/// value and little else.
	/// </summary>
	[Test]
	public async Task RangeFromKeyAsyncResumesAcrossPagesFromAMidTableStart()
	{
		const int rows = 5_000;
		const int start = 2_500;
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			for (var i = 0; i < rows; i++) tx.Put(Tables.Obj, Keys.Dbref(i), Keys.Dbref(i));
		});

		var count = 0;
		var previous = -1L;
		var outOfOrder = 0;
		var beforeTheStart = 0;

		var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
		await foreach (var (key, _) in store.RangeFromKeyAsync(Tables.Obj, Keys.Dbref(start), pageSize: 100))
		{
			var dbref = Keys.ReadDbref(key);
			if (dbref < start) beforeTheStart++;
			if (dbref <= previous) outOfOrder++;
			previous = dbref;
			count++;
		}
		var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

		await Assert.That(count).IsEqualTo(rows - start);
		await Assert.That(beforeTheStart).IsEqualTo(0);
		await Assert.That(outOfOrder).IsEqualTo(0);
		await Assert.That(previous).IsEqualTo(rows - 1L);
		await Assert.That(allocated / count).IsLessThan(200);
	}

	/// <summary>
	/// <c>DupsMapAsync</c> reads one key's duplicates a page at a time: every value, in value order, none of the
	/// neighbouring keys', the entries mapped to null left out, and the map run inside each page's transaction.
	/// </summary>
	[Test]
	public async Task DupsMapAsyncPagesOneKeysDuplicatesInOrder()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			for (var i = 0L; i < 700; i++) tx.Put(Tables.Location.Reverse, Keys.Dbref(5), Keys.Dbref(i * 3));
			tx.Put(Tables.Location.Reverse, Keys.Dbref(4), Keys.Dbref(1));
			tx.Put(Tables.Location.Reverse, Keys.Dbref(6), Keys.Dbref(2));
		});
		var expected = store.Read(tx => tx.Dups(Tables.Location.Reverse, Keys.Dbref(5)).Select(v => Keys.ReadDbref(v)).ToList());

		var seen = await store.DupsMapAsync(Tables.Location.Reverse, Keys.Dbref(5),
			(tx, value) => tx.TryGet(Tables.Location.Reverse, Keys.Dbref(4), out _) && Keys.ReadDbref(value) % 2 == 0
				? (object)Keys.ReadDbref(value)
				: null,
			pageSize: 64).Select(v => (long)v).ToListAsync();

		await Assert.That(seen).IsEquivalentTo(expected.Where(v => v % 2 == 0), CollectionOrdering.Matching);
		await Assert.That(await store.DupsMapAsync(Tables.Location.Reverse, Keys.Dbref(7), (_, v) => (object)v).CountAsync()).IsEqualTo(0);
	}

	/// <summary>A consumer that stops early pays for the first, small page only.</summary>
	[Test]
	public async Task DupsMapAsyncMapsOnlyTheFirstPageForAnEarlyExit()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			for (var i = 0L; i < 500; i++) tx.Put(Tables.Location.Reverse, Keys.Dbref(5), Keys.Dbref(i));
		});
		var mapped = 0;

		var first = await store.DupsMapAsync(Tables.Location.Reverse, Keys.Dbref(5),
			(_, value) => { mapped++; return (object)Keys.ReadDbref(value); }).FirstAsync();

		await Assert.That((long)first).IsEqualTo(0L);
		await Assert.That(mapped).IsEqualTo(LightningStore.FirstMapPageSize);
	}

	[Test]
	public async Task RangeMapAsyncPagesAPrefixInKeyOrder()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			for (var i = 0; i < 600; i++) tx.Put(Tables.AttrMeta, Keys.Attr(1, $"A{i:D4}"), Keys.Str("v"));
			tx.Put(Tables.AttrMeta, Keys.Attr(2, "A0000"), Keys.Str("v"));
		});
		var expected = store.Read(tx => tx.Range(Tables.AttrMeta, Keys.AttrPrefix(1)).Select(e => Keys.ParseAttr(e.Key).LongName).ToList());

		var seen = await store.RangeMapAsync(Tables.AttrMeta, Keys.AttrPrefix(1), (_, key, _) => Keys.ParseAttr(key).LongName, pageSize: 100)
			.ToListAsync();

		await Assert.That(seen).Count().IsEqualTo(600);
		await Assert.That(seen).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}
}
