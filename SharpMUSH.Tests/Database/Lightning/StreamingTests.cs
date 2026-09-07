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

	/// <summary>
	/// Exercises the duplicate-table resumption path that <see cref="RangeAsyncResumesAcrossPagesWhileWritesLand"/>
	/// never touches: <c>Tables.SessionAccount</c> is a duplicate-values table (<c>TableDef.Duplicates</c>
	/// true), so <c>RangeAsync</c> must resume with <c>afterValue</c> set, and <c>Tx.RangeFrom</c> must
	/// filter on <c>keyCompare == 0 &amp;&amp; entry.Value &lt;= afterValue</c> to skip only what was already
	/// yielded within a key's duplicate run. Three keys carry 40 duplicate values each; pageSize 25 puts
	/// every page boundary mid-duplicate-run for at least one key.
	/// </summary>
	[Test]
	public async Task RangeAsyncResumesAcrossDuplicateValuesWithinAKey()
	{
		using var store = new LightningStore(new LightningStoreOptions
		{
			Path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N")),
			MapSize = 256L << 20
		});
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
}
