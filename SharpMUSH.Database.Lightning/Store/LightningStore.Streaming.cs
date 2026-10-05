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
	/// once the keys run past whatever upper bound it cares about. Every page after the first re-seeks to the
	/// last entry it yielded, which is <paramref name="startKey"/>'s own semantics carried forward: a start key
	/// and no prefix bound. The empty prefix below says exactly that — not "scan the table from the top".
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
				(lastKey is null ? tx.RangeFromKey(table, startKey) : tx.RangeFrom(table, prefix: [], lastKey, dup ? lastValue : null))
					.Take(pageSize).ToList());
			foreach (var entry in page) yield return entry;
			if (page.Count < pageSize) yield break;
			(lastKey, lastValue) = page[^1];
		}
	}

	/// <summary>The first page of <see cref="RangeMapAsync{T}"/> and <see cref="DupsMapAsync{T}"/>: small, so a
	/// consumer that stops after the first few items (<c>AnyAsync</c>, <c>FirstOrDefaultAsync</c>) pays for a few
	/// rows rather than a full page. Each page after it doubles, up to the caller's page size.</summary>
	internal const int FirstMapPageSize = 16;

	/// <summary>
	/// <see cref="RangeAsync"/> with each entry turned into its item inside the transaction that read it:
	/// <paramref name="map"/> gets that page's <see cref="ITx"/> and may read whatever else the item needs (the
	/// row an index entry names, say) from the same snapshot, so a page costs one transaction rather than one
	/// per row. An entry mapped to <see langword="null"/> is skipped. No transaction is held across the
	/// consumer's awaits: pages start at <see cref="FirstMapPageSize"/> entries and double up to
	/// <paramref name="pageSize"/>, and each resumes strictly after the last entry the previous one read, so the
	/// order is the table's key (and duplicate) order exactly as one <see cref="ITx.Range"/> would give it.
	/// <para>
	/// The pages are separate snapshots: an entry written past the resume point between two pages is included,
	/// one deleted before its page is read is not, and two pages can see different versions of the rows they
	/// join to. A caller that needs one consistent view of several edges reads them inside a single
	/// <see cref="Read{T}"/>. <paramref name="map"/> runs inside the transaction, so it decodes and maps only:
	/// it must not await, open another read, or run unbounded work, and what it returns must not point into
	/// the transaction (the store already hands it copies).
	/// </para>
	/// </summary>
	public IAsyncEnumerable<T> RangeMapAsync<T>(TableDef table, byte[] prefix, Func<ITx, byte[], byte[], T?> map,
		int pageSize = 256, CancellationToken ct = default) where T : class
		=> PagedMapAsync<T>(table, prefix, exactKey: false, (tx, key, value, sink) =>
		{
			if (map(tx, key, value) is { } item)
			{
				sink.Add(item);
			}
		}, pageSize, ct);

	/// <summary>
	/// The duplicate values stored under exactly <paramref name="key"/>, in value order, each mapped inside the
	/// page's transaction — <see cref="ITx.Dups"/> paged the way <see cref="RangeMapAsync{T}"/> pages a range.
	/// </summary>
	public IAsyncEnumerable<T> DupsMapAsync<T>(TableDef table, byte[] key, Func<ITx, byte[], T?> map,
		int pageSize = 256, CancellationToken ct = default) where T : class
		=> PagedMapAsync<T>(table, key, exactKey: true, (tx, _, value, sink) =>
		{
			if (map(tx, value) is { } item)
			{
				sink.Add(item);
			}
		}, pageSize, ct);

	/// <summary>
	/// <see cref="DupsMapAsync{T}"/> for a value-type item, such as the <c>DBRef</c> a ref projection yields:
	/// an entry mapped to <see langword="null"/> is skipped, and the paging is the same.
	/// </summary>
	public IAsyncEnumerable<T> DupsMapValuesAsync<T>(TableDef table, byte[] key, Func<ITx, byte[], T?> map,
		int pageSize = 256, CancellationToken ct = default) where T : struct
		=> PagedMapAsync<T>(table, key, exactKey: true, (tx, _, value, sink) =>
		{
			if (map(tx, value) is { } item)
			{
				sink.Add(item);
			}
		}, pageSize, ct);

	/// <summary>Maps one entry, adding what it yields (nothing, for an entry the caller skips) to the page.</summary>
	private delegate void PageMapper<T>(ITx tx, byte[] key, byte[] value, List<T> sink);

	private async IAsyncEnumerable<T> PagedMapAsync<T>(TableDef table, byte[] prefix, bool exactKey,
		PageMapper<T> map, int pageSize, [EnumeratorCancellation] CancellationToken ct)
	{
		if (pageSize < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "A page holds at least one entry.");
		}

		byte[]? lastKey = null;
		byte[]? lastValue = null;
		var dup = table.Duplicates;
		var take = Math.Min(FirstMapPageSize, pageSize);
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			var limit = take;
			var (items, read, ended, pageKey, pageValue) = Read(tx =>
			{
				var mapped = new List<T>();
				byte[]? k = null;
				byte[]? v = null;
				var count = 0;
				var entries = lastKey is null ? tx.Range(table, prefix) : tx.RangeFrom(table, prefix, lastKey, dup ? lastValue : null);
				foreach (var (key, value) in entries)
				{
					// Range matches by prefix; a duplicate read wants this key's values and no longer key's.
					if (exactKey && !key.AsSpan().SequenceEqual(prefix))
					{
						return (mapped, count, true, k, v);
					}

					count++;
					(k, v) = (key, value);
					map(tx, key, value, mapped);

					if (count == limit)
					{
						return (mapped, count, false, k, v);
					}
				}

				return (mapped, count, true, k, v);
			});

			foreach (var item in items)
			{
				ct.ThrowIfCancellationRequested();
				yield return item;
			}

			if (ended || read < limit || pageKey is null)
			{
				yield break;
			}

			(lastKey, lastValue) = (pageKey, pageValue);
			take = Math.Min(take * 2, pageSize);
		}
	}
}
