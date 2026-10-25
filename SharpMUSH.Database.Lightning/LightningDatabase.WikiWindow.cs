using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Cursor pages of the category and namespace indexes (<see cref="IWikiStore.GetPagesByCategoryWindowAsync"/>,
/// <see cref="IWikiStore.GetPagesWindowAsync"/>). A token is the index key of the row it names, so the next page is one
/// seek from it: an offset listing steps over every admitted row before the page on each request, which made the
/// fortieth page of a large category cost forty first pages.
/// </summary>
/// <remarks>
/// A category token holds only the part of the key after the category's label, and the label is put back from the
/// request, so a token cannot reach another category's rows. A namespace token is the whole key, checked for the
/// shape <see cref="WikiNamespaceKey"/> writes. Either way the reader's visibility is applied to every row read.
/// </remarks>
public partial class LightningDatabase
{
	private const string WikiCategoryCursorPrefix = "c1.";
	private const string WikiNamespaceCursorPrefix = "n1.";

	public Task<Result<WikiPageWindow>> GetPagesByCategoryWindowAsync(string category, WikiPageCursor cursor, int take,
		WikiVisibility visibility)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
		return Task.FromResult(Store.Read(tx =>
		{
			RequireWikiCategoryReady(tx);
			var prefix = WikiLabelPrefix(category);
			return WikiWindow(tx, Tables.WikiByCategory, prefix, cursor, take,
				token => WikiPageWindows.Decode(token, WikiCategoryCursorPrefix) is { } suffix && IsCategoryKeySuffix(suffix)
					? Keys.Concat(prefix, suffix) : null,
				key => WikiPageWindows.Encode(key.AsSpan(prefix.Length), WikiCategoryCursorPrefix),
				entry => WikiTryAdmitCategoryEntry(tx, entry, prefix.Length - 1, visibility, out var key, out var page)
					? (key, page) : null);
		}));
	}

	public Task<Result<WikiPageWindow>> GetPagesWindowAsync(WikiPageCursor cursor, int take, WikiVisibility visibility)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
		return Task.FromResult(Store.Read(tx => WikiWindow(tx, Tables.WikiByNamespace, [], cursor, take,
			token => WikiPageWindows.Decode(token, WikiNamespaceCursorPrefix) is { } key && IsNamespaceKey(key) ? key : null,
			key => WikiPageWindows.Encode(key, WikiNamespaceCursorPrefix),
			entry => WikiIndexPageKey(entry.Key) is var key && WikiTryAdmitEntry(tx, key, entry.Value, visibility, out var page)
				? (key, page) : null)));
	}

	/// <summary>A title in UTF-16 code units, the two-byte separator, then an eight-byte page key.</summary>
	private static bool IsCategoryKeySuffix(ReadOnlySpan<byte> suffix)
		=> suffix.Length >= 10 && suffix.Length % 2 == 0 && suffix[^10] == 0 && suffix[^9] == 0
			&& Keys.ReadDbref(suffix[^8..]) >= 0;

	/// <summary>A namespace and a slug in UTF-16 code units, each ended by the two-byte separator, then an eight-byte page key.</summary>
	private static bool IsNamespaceKey(ReadOnlySpan<byte> key)
	{
		if (key.Length < 12 || key.Length % 2 != 0 || key[^10] != 0 || key[^9] != 0 || Keys.ReadDbref(key[^8..]) < 0) return false;
		for (var i = 0; i < key.Length - 10; i += 2)
			if (key[i] == 0 && key[i + 1] == 0) return true;
		return false;
	}

	/// <summary>An admitted index row: its key, the page's key, and the page when admitting it read the page.</summary>
	private sealed record WikiWindowRow(byte[] Index, long Key, WikiPage? Page);

	/// <summary>
	/// Reads one admitted row past the page in the direction of travel, so <see cref="WikiPageWindow.Next"/> (forward) or
	/// <see cref="WikiPageWindow.Previous"/> (backward) is null only when nothing the reader may see lies beyond it.
	/// </summary>
	private static Result<WikiPageWindow> WikiWindow(ITx tx, TableDef table, byte[] prefix, WikiPageCursor cursor, int take,
		Func<string, byte[]?> decode, Func<byte[], string> encode, Func<(byte[] Key, byte[] Value), (long Key, WikiPage? Page)?> admit)
	{
		var skip = cursor is WikiPageAt at ? Math.Max(0, at.Skip) : 0;
		var (entries, from, backward) = cursor switch
		{
			WikiPageAt => (tx.Range(table, prefix), null, false),
			WikiPageAfter { Token: var token } => decode(token) is { } key
				? (tx.RangeFrom(table, prefix, key, null), token, false)
				: (null, token, false),
			WikiPageBefore { Token: var token } => decode(token) is { } key
				? (tx.RangeReverseBefore(table, prefix, key), token, true)
				: (null, token, true),
			_ => ((IEnumerable<(byte[] Key, byte[] Value)>?)null, (string?)null, false)
		};
		if (entries is null) return new Error<string>("That page of the listing is not one this wiki wrote.");

		var rows = new List<WikiWindowRow>(take + 1);
		var skipped = false;
		foreach (var row in entries
			.Select(entry => admit(entry) is { } admitted ? new WikiWindowRow(entry.Key, admitted.Key, admitted.Page) : null)
			.OfType<WikiWindowRow>())
		{
			if (skip > 0)
			{
				skip--;
				skipped = true;
				continue;
			}
			rows.Add(row);
			if (rows.Count > take) break;
		}
		var beyond = rows.Count > take;
		if (beyond) rows.RemoveAt(take);
		if (backward) rows.Reverse();

		var pages = rows.Select(row => row.Page ?? ReadWikiPage(tx, row.Key)).OfType<WikiPage>().ToList();
		// An empty page names nothing to move on from; a reader there starts again from an offset.
		if (rows.Count == 0) return new WikiPageWindow(pages, null, backward ? from : null);
		var first = encode(rows[0].Index);
		var last = encode(rows[^1].Index);
		return backward
			? new WikiPageWindow(pages, beyond ? first : null, last)
			: new WikiPageWindow(pages, from is not null || skipped ? first : null, beyond ? last : null);
	}
}
