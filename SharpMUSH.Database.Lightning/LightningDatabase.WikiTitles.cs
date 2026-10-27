using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// A trigram index over page titles, so a title search reads the pages whose titles hold every three-character run of the
/// term instead of every page in the wiki.
/// </summary>
/// <remarks>
/// <see cref="Tables.WikiTitle"/> keys are three UTF-16 code units of the upper-cased title (big-endian), the page key,
/// then the locale of the translation the title belongs to (empty for the page's own title), each with an empty value.
/// The page's own title moves with the page's other list indexes; a translation's moves when it is written or deleted.
/// A term shorter than three code units has no trigram, so it reads the titles of every page the reader may see. Every
/// candidate is checked against its titles before it is returned, and against the reader's visibility through the
/// page's summary.
/// <para>
/// The index holds from <see cref="WikiTitleMigrationId"/> on, which builds it from the page and translation rows once.
/// </para>
/// </remarks>
public partial class LightningDatabase
{
	internal const string WikiTitleMigrationId = "0008_wiki_title_index";

	private const int WikiTitleGramLength = 3;

	/// <summary>The bytes of one trigram in a <see cref="Tables.WikiTitle"/> key.</summary>
	private const int WikiTitleGramBytes = WikiTitleGramLength * 2;

	/// <summary>Each distinct run of three code units in <paramref name="title"/>, upper-cased.</summary>
	private static IEnumerable<string> WikiTitleGrams(string title)
	{
		var upper = title.ToUpperInvariant();
		return Enumerable.Range(0, Math.Max(0, upper.Length - WikiTitleGramLength + 1))
			.Select(start => upper.Substring(start, WikiTitleGramLength))
			.Distinct(StringComparer.Ordinal);
	}

	private static byte[] WikiTitleKey(string gram, long pageKey, string locale)
		=> Keys.Concat(OrdinalBytes(gram), Keys.Dbref(pageKey), OrdinalBytes(locale));

	/// <summary>Writes (<paramref name="add"/>) or removes the trigram entries of one title of a page.</summary>
	private static void WikiTitleEntries(ITx tx, long pageKey, string locale, string? title, bool add)
	{
		foreach (var gram in WikiTitleGrams(title ?? string.Empty))
		{
			var key = WikiTitleKey(gram, pageKey, locale);
			if (add) tx.Put(Tables.WikiTitle, key, []);
			else tx.Delete(Tables.WikiTitle, key);
		}
	}

	/// <summary>Removes the trigram entries of every translation of a page.</summary>
	private static void WikiTranslationTitleEntriesRemoved(ITx tx, long pageKey)
	{
		foreach (var (_, value) in tx.Range(Tables.WikiTr, WikiPagePrefix(WikiPageId(pageKey))).ToList())
		{
			var translation = Codec.Deserialize<WikiTranslationRecord>(value);
			WikiTitleEntries(tx, pageKey, translation.Locale ?? string.Empty, translation.Title, add: false);
		}
	}

	/// <summary>Builds the index from the page and translation rows, replacing whatever was there.</summary>
	private static void MigrateWikiTitles(ITx tx)
	{
		var marker = Keys.Str("mig:" + WikiTitleMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		tx.DeletePrefix(Tables.WikiTitle, []);
		foreach (var (key, value) in tx.Range(Tables.WikiPage, []))
		{
			var pageKey = Keys.ReadDbref(key);
			WikiTitleEntries(tx, pageKey, string.Empty, Codec.Deserialize<WikiPageRecord>(value).Title, add: true);
		}
		foreach (var (_, value) in tx.Range(Tables.WikiTr, []))
		{
			var translation = Codec.Deserialize<WikiTranslationRecord>(value);
			if (TryParseWikiPageId(translation.PageId ?? string.Empty, out var pageKey))
				WikiTitleEntries(tx, pageKey, translation.Locale ?? string.Empty, translation.Title, add: true);
		}
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = WikiTitleMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	public Task<IReadOnlyList<string>> GetPageIdsByCategoryAsync(string category, WikiVisibility visibility)
		=> Task.FromResult<IReadOnlyList<string>>(Store.Read(tx =>
		{
			RequireWikiCategoryReady(tx);
			return WikiCategoryAdmitted(tx, WikiLabelPrefix(category), visibility).Select(entry => WikiPageId(entry.Key)).ToList();
		}));

	public Task<IReadOnlyList<string>> FindPagesByTitleAsync(string term, WikiVisibility visibility)
		=> Task.FromResult<IReadOnlyList<string>>(Store.Read(tx =>
			WikiTitleCandidates(tx, term, visibility)
				.Where(key => ReadWikiSummary(tx, key) is { } summary && WikiSummaryAdmitted(visibility, key, summary)
					&& WikiTitles(tx, key).Any(title => title.Contains(term, StringComparison.OrdinalIgnoreCase)))
				.Select(WikiPageId)
				.ToList()));

	/// <summary>
	/// The pages whose titles may hold <paramref name="term"/>: those with every trigram of it, or, for a term with none
	/// or a world the index migration has not reached, every page <paramref name="visibility"/> could admit.
	/// </summary>
	private static IEnumerable<long> WikiTitleCandidates(ITx tx, string term, WikiVisibility visibility)
	{
		var grams = WikiTitleGrams(term).ToList();
		if (grams.Count == 0 || !tx.TryGet(Tables.Meta, Keys.Str("mig:" + WikiTitleMigrationId), out _))
			return visibility.IncludedPageIds is { } included
				? CanonicalWikiPageKeys(included).Distinct().Order()
				: tx.Range(Tables.WikiSummary, []).Select(entry => Keys.ReadDbref(entry.Key)).ToList();

		HashSet<long>? candidates = visibility.IncludedPageIds is { } members
			? CanonicalWikiPageKeys(members).ToHashSet()
			: null;
		foreach (var gram in grams)
		{
			var holding = new HashSet<long>();
			foreach (var (key, _) in tx.Range(Tables.WikiTitle, OrdinalBytes(gram)))
			{
				var page = Keys.ReadDbref(key.AsSpan(WikiTitleGramBytes, 8));
				if (candidates is null || candidates.Contains(page)) holding.Add(page);
			}
			candidates = holding;
			if (candidates.Count == 0) break;
		}
		return candidates!.Order();
	}

	/// <summary>A page's own title and the title of each of its translations.</summary>
	private static IEnumerable<string> WikiTitles(ITx tx, long key)
	{
		if (TryReadWikiPage(tx, key) is not { } record) yield break;
		yield return record.Title;
		foreach (var (_, value) in tx.Range(Tables.WikiTr, WikiPagePrefix(WikiPageId(key))))
			yield return Codec.Deserialize<WikiTranslationRecord>(value).Title ?? string.Empty;
	}
}
