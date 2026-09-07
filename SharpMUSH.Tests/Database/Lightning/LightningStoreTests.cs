using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class LightningStoreTests
{
	private static LightningStore Open() => new(new LightningStoreOptions
	{
		Path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N")),
		MapSize = 256L << 20
	});

	[Test]
	public async Task OpensEveryCatalogueTable()
	{
		using var store = Open();
		var counts = store.Read(tx => Tables.All.Select(t => tx.Count(t)).ToArray());
		await Assert.That(counts.All(c => c == 0)).IsTrue();
		await Assert.That(counts.Length).IsEqualTo(Tables.All.Count);
	}

	[Test]
	public async Task RangeReturnsOnlyKeysUnderThePrefixInOrder()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.AttrMeta, Keys.Attr(5, "B"), "b"u8);
			tx.Put(Tables.AttrMeta, Keys.Attr(5, "A`X"), "ax"u8);
			tx.Put(Tables.AttrMeta, Keys.Attr(5, "A"), "a"u8);
			tx.Put(Tables.AttrMeta, Keys.Attr(6, "A"), "other"u8);
			return 0;
		});
		var names = store.Read(tx => tx.Range(Tables.AttrMeta, Keys.AttrPrefix(5))
			.Select(e => Keys.ParseAttr(e.Key).LongName).ToArray());
		await Assert.That(names).IsEquivalentTo(new[] { "A", "A`X", "B" });
	}

	[Test]
	public async Task DuplicateValuesAreSortedAndIndividuallyDeletable()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(30));
			tx.Put(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(20));
			tx.Put(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(20));
			return 0;
		});
		var before = store.Read(tx => tx.Dups(Tables.RevLocation, Keys.Dbref(1)).Select(v => Keys.ReadDbref(v)).ToArray());
		await Assert.That(before).IsEquivalentTo(new long[] { 20, 30 });
		await store.WriteAsync(tx =>
		{
			tx.Delete(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(20));
			return 0;
		});
		var after = store.Read(tx => tx.Dups(Tables.RevLocation, Keys.Dbref(1)).Select(v => Keys.ReadDbref(v)).ToArray());
		await Assert.That(after).IsEquivalentTo(new long[] { 30 });
	}

	[Test]
	public async Task EmptyPrefixRangeScansTheWholeTable()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.Obj, Keys.Dbref(1), Keys.Str("one"));
			tx.Put(Tables.Obj, Keys.Dbref(2), Keys.Str("two"));
			tx.Put(Tables.Obj, Keys.Dbref(3), Keys.Str("three"));
			tx.Put(Tables.Meta, Keys.Dbref(100), Keys.Str("meta-a"));
			tx.Put(Tables.Meta, Keys.Dbref(101), Keys.Str("meta-b"));
			return 0;
		});

		var count = store.Read(tx => tx.Range(Tables.Obj, []).Count());
		await Assert.That(count).IsEqualTo(3);

		var keys = new List<byte[]>();
		await foreach (var (key, _) in store.RangeAsync(Tables.Obj, [], pageSize: 2))
		{
			keys.Add(key);
		}
		await Assert.That(string.Join(",", keys.Select(k => Keys.ReadDbref(k)))).IsEqualTo("1,2,3");
	}

	[Test]
	public async Task ObjNameOpensWithFixedDuplicatesForItsDbrefValues()
	{
		await Assert.That(Tables.ObjName.FixedDuplicates).IsTrue();

		using var store = Open();
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.ObjName, Keys.Str("BOB"), Keys.Dbref(20));
			tx.Put(Tables.ObjName, Keys.Str("BOB"), Keys.Dbref(10));
			return 0;
		});
		var dbrefs = store.Read(tx => tx.Dups(Tables.ObjName, Keys.Str("BOB")).Select(v => Keys.ReadDbref(v)).ToArray());
		await Assert.That(dbrefs).IsEquivalentTo(new long[] { 10, 20 });
	}

	/// <summary>
	/// OpenTable must route its underlying transaction through the same writer thread that every other
	/// write uses, not begin one directly on the caller's thread. Two things prove that:
	/// (1) pausing the writer (what staging promotion will do to swap the environment) must also block a
	/// fresh OpenTable call — a transaction begun straight on the caller's thread would ignore the pause
	/// and complete immediately, which is exactly the bug: pre-fix, this assertion is RED, because
	/// OpenTable's own <c>_env.BeginTransaction()</c> never touches the writer's pause gate at all;
	/// (2) ordinary WriteAsync jobs immediately before and after an OpenTable call still land on a single
	/// thread. The idempotency assertion (repeat call returns the same instance) has no prior coverage
	/// either way.
	/// </summary>
	[Test]
	public async Task OpenTableRunsOnTheWriterThreadAndIsIdempotent()
	{
		using var store = Open();

		var first = store.OpenTable("scene.test", duplicates: true);
		var second = store.OpenTable("scene.test", duplicates: true);
		await Assert.That(second.Name).IsEqualTo(first.Name);
		await Assert.That(ReferenceEquals(first, second)).IsTrue();

		store.PauseWriter();
		try
		{
			var openTask = Task.Run(() => store.OpenTable("scene.other", duplicates: false));
			var wonRace = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromMilliseconds(300)));
			await Assert.That(ReferenceEquals(wonRace, openTask)).IsFalse();

			store.ResumeWriter();
			var opened = await openTask.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(opened.Name).IsEqualTo("scene.other");
		}
		finally
		{
			store.ResumeWriter();
		}

		var threadIds = new List<int>();
		await store.WriteAsync(tx =>
		{
			lock (threadIds) threadIds.Add(Environment.CurrentManagedThreadId);
			return 0;
		});

		store.OpenTable("scene.third", duplicates: true);

		await store.WriteAsync(tx =>
		{
			lock (threadIds) threadIds.Add(Environment.CurrentManagedThreadId);
			tx.Put(first, Keys.Dbref(1), Keys.Dbref(100));
			tx.Put(first, Keys.Dbref(1), Keys.Dbref(200));
			return 0;
		});
		await Assert.That(threadIds.Distinct().Count()).IsEqualTo(1);

		var values = store.Read(tx => tx.Dups(first, Keys.Dbref(1)).Select(v => Keys.ReadDbref(v)).ToArray());
		await Assert.That(values).IsEquivalentTo(new long[] { 100, 200 });
	}

	/// <summary>A swap that never starts — nothing to swap in — must cost the caller its call and nothing
	/// else. The environment is not even closed for this one, so the store keeps serving.</summary>
	[Test]
	public async Task SwapDirectoryRejectsAMissingIncomingPathAndKeepsServing()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("before"), "1"u8));

		await Assert.That(() => store.SwapDirectory(store.Path + ".nonexistent", store.Path + ".previous"))
			.Throws<DirectoryNotFoundException>();

		await AssertStillServesAsync(store, "after-missing");
	}

	/// <summary>
	/// A swap whose on-disk step throws after the environment is closed must still reopen it. The failure
	/// is forced by parking a plain file where the outgoing directory has to be moved to, so the very
	/// first <c>Directory.Move</c> fails with the live directory still in place — pre-fix the store stays
	/// closed for good while the gate is released and the writer resumed, and every later read throws
	/// <c>ObjectDisposedException</c> on the disposed environment.
	/// </summary>
	[Test]
	public async Task AFailedSwapReopensTheEnvironmentAndKeepsServing()
	{
		using var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("before"), "1"u8));

		var incoming = store.Path + ".incoming";
		Directory.CreateDirectory(incoming);
		var previous = store.Path + ".previous";
		await File.WriteAllTextAsync(previous, "not a directory");

		await Assert.That(() => store.SwapDirectory(incoming, previous)).Throws<IOException>();

		await AssertStillServesAsync(store, "after-failure");
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("before"), out _))).IsTrue();

		File.Delete(previous);
		Directory.Delete(incoming, recursive: true);
	}

	/// <summary>Both halves of "the store still works": a read on the calling thread and a write through
	/// the writer thread, which a failed swap must have resumed.</summary>
	private static async Task AssertStillServesAsync(LightningStore store, string key)
	{
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str(key), "1"u8));
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str(key), out _))).IsTrue();
	}
}
