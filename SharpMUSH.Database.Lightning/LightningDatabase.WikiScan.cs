using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Database.Lightning.Store;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private const string WikiScanTokenPrefix = "lightning-wiki-scan:v1:";

	private static byte[] WikiScanKey(string token)
	{
		var key = WikiPageScan.Decode(token, WikiScanTokenPrefix);
		if (key.Length < 12 || (key.Length - 8) % 2 != 0)
			throw new ArgumentException("Invalid wiki namespace scan key.", nameof(token));
		var separators = 0;
		var lastSeparator = -1;
		for (var i = 0; i < key.Length - 8; i += 2)
		{
			if (key[i] != 0 || key[i + 1] != 0) continue;
			separators++;
			lastSeparator = i;
		}
		if (separators != 2 || lastSeparator != key.Length - 10
			|| Keys.ReadDbref(key.AsSpan(key.Length - 8)) < 0)
			throw new ArgumentException("Invalid wiki namespace scan key.", nameof(token));
		return key;
	}

	public Task<WikiPageScanBatch> ScanPagesAsync(WikiVisibility visibility, WikiPageScanPosition position, int take)
	{
		var offset = WikiPageScan.Validate(position, take);
		_ = checked(offset + take);
		var afterKey = position switch
		{
			WikiPageScanContinuation continuation => WikiScanKey(continuation.Token),
			_ => Array.Empty<byte>()
		};
		return Task.FromResult(Store.Read(tx =>
		{
			var source = afterKey.Length == 0
				? tx.Range(Tables.WikiByNamespace, [])
				: tx.RangeFrom(Tables.WikiByNamespace, [], afterKey, null);
			var lastKey = afterKey;
			IEnumerable<(byte[] Key, byte[] Value)> Track()
			{
				foreach (var entry in source)
				{
					offset = checked(offset + 1);
					lastKey = entry.Key;
					yield return entry;
				}
			}
			var pages = new List<WikiPage>();
			using var admitted = WikiAdmitted(tx, Track(), visibility).GetEnumerator();
			while (pages.Count < take)
			{
				if (!admitted.MoveNext()) return new WikiPageScanBatch(pages, new WikiPageScanEnd());
				var entry = admitted.Current;
				if ((entry.Page ?? ReadWikiPage(tx, entry.Key)) is { } page) pages.Add(page);
			}
			// No lookahead is consumed. An exact-full final batch is followed by a proven empty EOF batch.
			return new WikiPageScanBatch(pages, new WikiPageScanContinuation(offset,
				WikiPageScan.Encode(lastKey, WikiScanTokenPrefix)));
		}));
	}
}
