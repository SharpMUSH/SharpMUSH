using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class StreamingTests
{
	[Test]
	public async Task RangeAsyncResumesAcrossPagesWhileWritesLand()
	{
		using var store = new LightningStore(new LightningStoreOptions
		{
			Path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N")),
			MapSize = 256L << 20
		});
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
}
