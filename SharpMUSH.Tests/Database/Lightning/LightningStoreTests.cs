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
}
