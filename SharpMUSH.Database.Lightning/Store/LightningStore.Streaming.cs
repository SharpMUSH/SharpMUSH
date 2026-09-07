using System.Runtime.CompilerServices;

using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	/// <summary>
	/// Streams a prefix range without holding a read transaction across the consumer's awaits: each
	/// page is read in its own transaction and the next page resumes strictly after the last entry.
	/// Entries written between pages at keys not yet passed are included; entries deleted are skipped.
	/// </summary>
	public async IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix,
		int pageSize = 256, [EnumeratorCancellation] CancellationToken ct = default)
	{
		byte[]? lastKey = null;
		byte[]? lastValue = null;
		var dup = table.Duplicates;
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			var page = Read(tx =>
				(lastKey is null ? tx.Range(table, prefix) : tx.RangeFrom(table, prefix, lastKey, dup ? lastValue : null))
					.Take(pageSize).ToList());
			foreach (var entry in page) yield return entry;
			if (page.Count < pageSize) yield break;
			(lastKey, lastValue) = page[^1];
		}
	}
}
