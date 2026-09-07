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

	/// <summary>
	/// As <see cref="RangeAsync"/>, but the scan starts at <paramref name="startKey"/> (inclusive) instead of the
	/// start of the table, with no prefix restriction on where it ends — the caller stops the enumeration itself
	/// once the keys run past whatever upper bound it cares about.
	/// </summary>
	public async IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeFromKeyAsync(TableDef table, byte[] startKey,
		int pageSize = 256, [EnumeratorCancellation] CancellationToken ct = default)
	{
		byte[]? lastKey = null;
		byte[]? lastValue = null;
		var dup = table.Duplicates;
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			var page = Read(tx =>
				(lastKey is null ? tx.RangeFromKey(table, startKey) : tx.RangeFrom(table, [], lastKey, dup ? lastValue : null))
					.Take(pageSize).ToList());
			foreach (var entry in page) yield return entry;
			if (page.Count < pageSize) yield break;
			(lastKey, lastValue) = page[^1];
		}
	}
}
