using System.Globalization;
using System.Text;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IWikiStore"/>: wiki pages, their revision streams and their translations. Normalisation,
/// rendering and locale validation happen above this, in <c>WikiStoreService</c>.
/// </summary>
/// <remarks>
/// Four tables carry the area. <see cref="Tables.WikiPage"/> holds the page rows, keyed by an id drawn
/// from the <c>next_wiki</c> counter (its own sequence — wiki ids and dbrefs are unrelated), and
/// <see cref="Tables.WikiSlug"/> is the unique index over the page's real identity,
/// <c>(namespace, slug)</c>, keyed by <see cref="WikiHelpers.SlugKey"/>'s string so the
/// duplicate-create check is a single <c>TryGet</c> rather than a scan.
/// <para>
/// <see cref="Tables.WikiRev"/> is keyed <c>(pageId, locale, revisionNumber)</c> with the number in
/// fixed-width big-endian, so one locale's stream is a contiguous ascending range whose last entry is
/// its latest revision, and the empty locale — the canonical source-stream marker — is a range of its
/// own rather than a filter over everything. <see cref="Tables.WikiTr"/> is keyed
/// <c>(pageId, locale)</c>, which makes "the translations of this page, ordered by locale" a prefix
/// range and gives the <c>(pageId, locale)</c> uniqueness the contract asks for for free.
/// </para>
/// <para>
/// Three list indexes answer the listings without reading page rows, whose Markdown, HTML and plain
/// text dwarf what a listing filters and sorts on. Each value is the page's visibility (a published byte
/// and the author dbref), and each key ends with the page key, the deterministic tie-break:
/// <see cref="Tables.WikiRecent"/> is <c>(UpdatedAt UTC ticks, page)</c>, read backwards;
/// <see cref="Tables.WikiByNamespace"/> is <c>(namespace, slug, page)</c>; <see cref="Tables.WikiByCategory"/>
/// is <c>(upper-cased category, title, page)</c>, one entry per category the page is in. The ordered
/// strings (namespace, slug, title) are written UTF-16 big-endian with a two-byte zero separator, so the
/// key order is exactly <see cref="StringComparer.Ordinal"/>'s; the matched strings are upper-cased
/// invariantly, which is how <see cref="StringComparison.OrdinalIgnoreCase"/> compares. A listing reads
/// index entries up to <c>skip + take</c> admitted ones and decodes only the <c>take</c> pages it returns.
/// Every write that changes an indexed field drops the page's old entries and writes its new ones in the
/// same job.
/// </para>
/// <para>
/// Every mutation is one write job on the single writer thread, so <c>WriteTranslationAsync</c>'s
/// compare-and-swap reads the stored revision number and appends its revision inside the same
/// transaction: two writers holding the same <c>expectedRevisionNumber</c> cannot both win, and the
/// loser leaves no revision behind.
/// </para>
/// </remarks>
public partial class LightningDatabase : IWikiStore
{
	private const string WikiPageIdPrefix = "wiki_page/";

	private static string WikiPageId(long key) => $"{WikiPageIdPrefix}{key.ToString(CultureInfo.InvariantCulture)}";

	/// <summary>
	/// Parses a page id — either the canonical <c>wiki_page/12</c> or a bare <c>12</c> — into its numeric
	/// key. Any other shape is a miss rather than a throw, because callers hand this method ids that came
	/// from another provider's URL space (<c>node_wiki_pages/ghost_…</c>) and expect <c>NotFound</c>.
	/// </summary>
	private static bool TryParseWikiPageId(string id, out long key)
	{
		key = 0;
		var tail = id.StartsWith(WikiPageIdPrefix, StringComparison.Ordinal) ? id[WikiPageIdPrefix.Length..] : id;
		return !tail.Contains('/') && long.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out key);
	}

	/// <summary>
	/// The canonical form of a page id, so a caller passing the bare numeric key reaches the same
	/// revision and translation keys as one passing <c>wiki_page/12</c>. An id that is neither is left
	/// alone; it will simply match nothing.
	/// </summary>
	private static string CanonicalWikiPageId(string pageId)
		=> TryParseWikiPageId(pageId, out var key) ? WikiPageId(key) : pageId;

	private static byte[] WikiPageKey(long key) => Keys.Dbref(key);

	private static byte[] WikiSlugKey(string nsStr, string slug)
		=> Keys.Lower(WikiHelpers.SlugKey(nsStr, slug));

	private static byte[] WikiRevKey(string pageId, string locale, int revisionNumber)
		=> Keys.Composite(pageId, locale, (uint)revisionNumber);

	/// <summary>The key range of one <c>(pageId, locale)</c> revision stream.</summary>
	private static byte[] WikiRevPrefix(string pageId, string locale)
		=> Keys.Concat(Keys.Str(pageId), Keys.Sep, Keys.Str(locale), Keys.Sep);

	/// <summary>The key range of everything belonging to one page — its translations, or every one of its
	/// revision streams whatever the locale.</summary>
	private static byte[] WikiPagePrefix(string pageId) => Keys.Concat(Keys.Str(pageId), Keys.Sep);

	private static byte[] WikiTranslationKey(string pageId, string locale) => Keys.Composite(pageId, locale);

	private static readonly byte[] OrdinalSep = [0x00, 0x00];

	/// <summary>A string as UTF-16 big-endian code units, whose byte order is <see cref="StringComparer.Ordinal"/>'s.</summary>
	private static byte[] OrdinalBytes(string value) => Encoding.BigEndianUnicode.GetBytes(value);

	/// <summary>The stored visibility of a page: one published byte, then the author dbref.</summary>
	private static byte[] WikiVisibilityValue(WikiPageRecord r) => Keys.Concat([(r.Published ?? true) ? (byte)1 : (byte)0], Keys.Str(r.AuthorDbref));

	private static bool WikiVisibilityAdmits(WikiVisibility visibility, byte[] value)
		=> visibility.Admits(value[0] == 1, Keys.ReadStr(value.AsSpan(1)));

	/// <summary>Whether <paramref name="visibility"/> admits an index entry, reading its page only when a hidden rule needs it.</summary>
	private static bool WikiEntryAdmitted(ITx tx, (byte[] Key, byte[] Value) entry, WikiVisibility visibility)
		=> WikiVisibilityAdmits(visibility, entry.Value)
			&& (visibility.Hidden is null || ReadWikiPage(tx, WikiIndexPageKey(entry.Key)) is { } page && visibility.Admits(page));

	private static long WikiIndexPageKey(byte[] key) => Keys.ReadDbref(key.AsSpan(key.Length - 8, 8));

	private static byte[] WikiRecentKey(long pageKey, WikiPageRecord r)
		=> Keys.Concat(Keys.Dbref(ParseWikiTimestamp(r.UpdatedAt).UtcTicks), Keys.Dbref(pageKey));

	private static byte[] WikiNamespaceKey(long pageKey, WikiPageRecord r)
		=> Keys.Concat(OrdinalBytes(r.Namespace), OrdinalSep, OrdinalBytes(r.Slug), OrdinalSep, Keys.Dbref(pageKey));

	private static byte[] WikiLabelPrefix(string label) => Keys.Concat(Keys.Upper(label), Keys.Sep);

	private static byte[] WikiLabelKey(string label, long pageKey, WikiPageRecord r)
		=> Keys.Concat(WikiLabelPrefix(label), OrdinalBytes(r.Title), OrdinalSep, Keys.Dbref(pageKey));

	/// <summary>The categories a page is listed under, once each however many case variants it carries.</summary>
	private static IEnumerable<string> WikiIndexCategories(WikiPageRecord r)
		=> (r.Categories ?? []).DistinctBy(category => category.ToUpperInvariant());

	/// <summary>Writes (<paramref name="add"/>) or removes every list-index entry <paramref name="r"/> holds.</summary>
	private static void WikiListIndexes(ITx tx, long pageKey, WikiPageRecord r, bool add)
	{
		var value = WikiVisibilityValue(r);
		Apply(Tables.WikiRecent, WikiRecentKey(pageKey, r));
		Apply(Tables.WikiByNamespace, WikiNamespaceKey(pageKey, r));
		foreach (var category in WikiIndexCategories(r)) Apply(Tables.WikiByCategory, WikiLabelKey(category, pageKey, r));

		void Apply(TableDef table, byte[] key)
		{
			if (add) tx.Put(table, key, value);
			else tx.Delete(table, key);
		}
	}

	/// <summary>Replaces a page row and moves its list-index entries from <paramref name="old"/> to <paramref name="updated"/>.</summary>
	private static void PutWikiPage(ITx tx, long pageKey, WikiPageRecord? old, WikiPageRecord updated)
	{
		if (old is not null) WikiListIndexes(tx, pageKey, old, add: false);
		tx.Put(Tables.WikiPage, WikiPageKey(pageKey), Codec.Serialize(updated));
		WikiListIndexes(tx, pageKey, updated, add: true);
	}

	/// <summary>
	/// The namespaces stored in <see cref="Tables.WikiByNamespace"/>, in ordinal order, one seek each: after
	/// a namespace's first key the cursor jumps past every key under it.
	/// </summary>
	private static IEnumerable<byte[]> WikiNamespacePrefixes(ITx tx)
	{
		var next = tx.Range(Tables.WikiByNamespace, []).Select(e => e.Key).FirstOrDefault();
		while (next is not null)
		{
			var end = 0;
			while (end + 1 < next.Length && (next[end] != 0 || next[end + 1] != 0)) end += 2;
			var prefix = next[..end];
			yield return Keys.Concat(prefix, OrdinalSep);
			next = tx.RangeFromKey(Tables.WikiByNamespace, Keys.Concat(prefix, [0x00, 0x01])).Select(e => e.Key).FirstOrDefault();
		}
	}

	/// <summary>The namespace index entries a listing reads: all of them, or those of the namespaces equal to
	/// <paramref name="ns"/> ignoring case, in (namespace, slug, page) order either way.</summary>
	private static IEnumerable<(byte[] Key, byte[] Value)> WikiNamespaceEntries(ITx tx, string? ns)
		=> ns is null
			? tx.Range(Tables.WikiByNamespace, [])
			: WikiNamespacePrefixes(tx)
				.Where(prefix => Encoding.BigEndianUnicode.GetString(prefix.AsSpan(0, prefix.Length - 2))
					.Equals(ns, StringComparison.OrdinalIgnoreCase))
				.ToList()
				.SelectMany(prefix => tx.Range(Tables.WikiByNamespace, prefix));

	/// <summary>
	/// The admitted entries of an ordered index, paged, each resolved to its page row. The draft rule reads the
	/// index value alone; namespace, category and page restrictions need the row, so only a reader who has some
	/// pays for decoding the rows it skips.
	/// </summary>
	private static List<WikiPage> WikiPagesFromIndex(ITx tx, IEnumerable<(byte[] Key, byte[] Value)> entries,
		WikiVisibility visibility, int skip, int take)
		=> WikiAdmitted(tx, entries, visibility)
			.Skip(skip)
			.Take(take)
			.Select(entry => entry.Page ?? ReadWikiPage(tx, entry.Key))
			.OfType<WikiPage>()
			.ToList();

	/// <summary>The index entries <paramref name="visibility"/> admits, with the page row when it had to be read.</summary>
	private static IEnumerable<(long Key, WikiPage? Page)> WikiAdmitted(ITx tx, IEnumerable<(byte[] Key, byte[] Value)> entries,
		WikiVisibility visibility)
	{
		var admitted = entries
			.Where(entry => WikiVisibilityAdmits(visibility, entry.Value))
			.Select(entry => WikiIndexPageKey(entry.Key));
		return visibility.Hidden is null
			? admitted.Select(key => (key, (WikiPage?)null))
			: admitted
				.Select(key => (Key: key, Page: ReadWikiPage(tx, key)))
				.Where(entry => entry.Page is { } page && visibility.Admits(page));
	}

	private static WikiPage? ReadWikiPage(ITx tx, long key)
		=> TryReadWikiPage(tx, key) is { } record ? MapWikiPage(key, record) : null;

	/// <summary>Reads and increments the <c>next_wiki</c> counter inside a write job, mirroring <see cref="AllocateDbref"/>.</summary>
	private static long AllocateWikiId(ITx tx)
	{
		var current = tx.TryGet(Tables.Meta, Keys.Str("next_wiki"), out var v) ? Keys.ReadDbref(v) : 0;
		tx.Put(Tables.Meta, Keys.Str("next_wiki"), Keys.Dbref(current + 1));
		return current;
	}

	private static string WikiTimestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

	private static DateTimeOffset ParseWikiTimestamp(string? value)
		=> DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
			? parsed
			: default;

	private static WikiPageRecord? TryReadWikiPage(ITx tx, long key)
		=> tx.TryGet(Tables.WikiPage, WikiPageKey(key), out var bytes) ? Codec.Deserialize<WikiPageRecord>(bytes) : null;

	/// <summary>Resolves a caller-supplied page id to its key and record, or null when either step misses.</summary>
	private static (long Key, WikiPageRecord Record)? TryReadWikiPage(ITx tx, string id)
	{
		if (!TryParseWikiPageId(id, out var key)) return null;
		return TryReadWikiPage(tx, key) is { } record ? (key, record) : null;
	}

	private static WikiPage MapWikiPage(long key, WikiPageRecord r) => new(
		Id: WikiPageId(key),
		Slug: r.Slug,
		Title: r.Title,
		Namespace: r.Namespace,
		MarkdownSource: r.MarkdownSource,
		RenderedHtml: r.RenderedHtml,
		PlainText: r.PlainText,
		AuthorDbref: r.AuthorDbref,
		LastEditorDbref: r.LastEditorDbref,
		CreatedAt: ParseWikiTimestamp(r.CreatedAt),
		UpdatedAt: ParseWikiTimestamp(r.UpdatedAt),
		RevisionNumber: r.RevisionNumber)
	{
		Categories = r.Categories ?? [],
		Published = r.Published ?? true,
		// Read straight through: a record the backfill has not reached yields empty, which means
		// "not yet stamped". Nothing substitutes the configured default here or anywhere on the read path.
		SourceLocale = r.SourceLocale ?? string.Empty
	};

	private static string WikiRevisionId(string pageId, string locale, int revisionNumber)
	{
		var key = TryParseWikiPageId(pageId, out var parsed) ? parsed.ToString(CultureInfo.InvariantCulture) : pageId;
		return $"wiki_revision/{key}:{locale}:{revisionNumber.ToString(CultureInfo.InvariantCulture)}";
	}

	private static string WikiTranslationId(string pageId, string locale)
	{
		var key = TryParseWikiPageId(pageId, out var parsed) ? parsed.ToString(CultureInfo.InvariantCulture) : pageId;
		return $"wiki_translation/{key}:{locale}";
	}

	private static WikiRevision MapWikiRevision(WikiRevisionRecord r) => new(
		Id: WikiRevisionId(r.PageId, r.Locale ?? string.Empty, r.RevisionNumber),
		PageId: r.PageId,
		RevisionNumber: r.RevisionNumber,
		MarkdownSource: r.MarkdownSource,
		EditorDbref: r.EditorDbref,
		Timestamp: ParseWikiTimestamp(r.Timestamp),
		EditSummary: string.IsNullOrEmpty(r.EditSummary) ? null : r.EditSummary)
	{
		Locale = r.Locale ?? string.Empty
	};

	private static WikiTranslation MapWikiTranslation(WikiTranslationRecord r) => new(
		Id: WikiTranslationId(r.PageId ?? string.Empty, r.Locale ?? string.Empty),
		PageId: r.PageId ?? string.Empty,
		Locale: r.Locale ?? string.Empty,
		Title: r.Title ?? string.Empty,
		MarkdownSource: r.MarkdownSource ?? string.Empty,
		RenderedHtml: r.RenderedHtml ?? string.Empty,
		PlainText: r.PlainText ?? string.Empty,
		LastEditorDbref: r.LastEditorDbref ?? string.Empty,
		CreatedAt: ParseWikiTimestamp(r.CreatedAt),
		UpdatedAt: ParseWikiTimestamp(r.UpdatedAt),
		Published: r.Published ?? true,
		RevisionNumber: r.RevisionNumber ?? 1);

	/// <summary>
	/// Appends one revision row. The key carries <c>(pageId, locale, revisionNumber)</c>, so a stream's
	/// revisions sort ascending and a second write of the same number would overwrite rather than
	/// duplicate — which is why every caller checks the stored number first and appends only
	/// <c>expected + 1</c>.
	/// </summary>
	private static void AppendWikiRevision(ITx tx, string pageId, string locale, int revisionNumber,
		string markdown, string editorDbref, string? editSummary, DateTimeOffset timestamp)
	{
		var record = new WikiRevisionRecord
		{
			PageId = pageId,
			// Written explicitly as the empty source-stream marker rather than left absent, so every row's
			// shape is identical across the source and translation writers.
			Locale = locale,
			RevisionNumber = revisionNumber,
			MarkdownSource = markdown,
			EditorDbref = editorDbref,
			Timestamp = WikiTimestamp(timestamp),
			EditSummary = editSummary
		};

		tx.Put(Tables.WikiRev, WikiRevKey(pageId, locale, revisionNumber), Codec.Serialize(record));
	}

	public Task<Found<WikiPage>> GetPageBySlugAsync(string ns, string slug)
		=> Task.FromResult(Store.Read<Found<WikiPage>>(tx =>
		{
			if (!tx.TryGet(Tables.WikiSlug, WikiSlugKey(ns, slug), out var idBytes)) return new NotFound();

			var pageKey = Keys.ReadDbref(idBytes);
			return TryReadWikiPage(tx, pageKey) is { } record ? MapWikiPage(pageKey, record) : new NotFound();
		}));

	public Task<Found<WikiPage>> GetPageByIdAsync(string id)
		=> Task.FromResult(Store.Read<Found<WikiPage>>(tx =>
			TryReadWikiPage(tx, id) is { } found ? MapWikiPage(found.Key, found.Record) : new NotFound()));

	public Task<IReadOnlyList<WikiPage>> GetRecentPagesAsync(int count, WikiVisibility visibility)
		// Backwards over (UpdatedAt, page): newest first, and within one timestamp tick the newest-created
		// page first, so two pages written together still order deterministically.
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx =>
			WikiPagesFromIndex(tx, tx.RangeReverse(Tables.WikiRecent, []), visibility, 0, count)));

	public Task<IReadOnlyList<WikiPage>> GetPagesAsync(string? ns, int skip, int take, WikiVisibility visibility)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx =>
			WikiPagesFromIndex(tx, WikiNamespaceEntries(tx, ns), visibility, skip, take)));

	// `Published ?? true` rather than `== true`, so a row written before the field existed counts the
	// same way it displays; the index value already carries it that way.
	public Task<int> CountPagesAsync(string? ns, WikiVisibility visibility)
		=> Task.FromResult(Store.Read(tx => WikiAdmitted(tx, WikiNamespaceEntries(tx, ns), visibility).Count()));

	public Task<WikiPageCounts> CountPagesByStateAsync(WikiVisibility visibility)
		=> Task.FromResult(Store.Read(tx =>
		{
			int published = 0, drafts = 0;
			foreach (var (_, value) in tx.Range(Tables.WikiByNamespace, []).Where(entry => WikiEntryAdmitted(tx, entry, visibility)))
			{
				if (value[0] == 1) published++;
				else drafts++;
			}

			return new WikiPageCounts(published, drafts);
		}));

	public Task<IReadOnlyDictionary<string, int>> CountPagesByCategoryAsync(WikiVisibility visibility)
		=> Task.FromResult<IReadOnlyDictionary<string, int>>(Store.Read(tx =>
		{
			// The index key opens with the upper-cased category and a separator; category keys are lower case.
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var (key, _) in tx.Range(Tables.WikiByCategory, []).Where(entry => WikiEntryAdmitted(tx, entry, visibility)))
			{
				var category = Keys.ReadStr(key.AsSpan(0, key.AsSpan().IndexOf(Keys.Sep[0]))).ToLowerInvariant();
				counts[category] = counts.GetValueOrDefault(category) + 1;
			}

			return counts;
		}));

	public Task<IReadOnlyList<WikiPage>> GetPagesByCategoryAsync(string category, int skip, int take, WikiVisibility visibility)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx =>
			WikiPagesFromIndex(tx, tx.Range(Tables.WikiByCategory, WikiLabelPrefix(category)), visibility, skip, take)));

	public async Task<Result<WikiPage>> CreatePageAsync(WikiPage page)
	{
		var record = new WikiPageRecord
		{
			Slug = page.Slug,
			Title = page.Title,
			Namespace = page.Namespace,
			MarkdownSource = page.MarkdownSource,
			RenderedHtml = page.RenderedHtml,
			PlainText = page.PlainText,
			AuthorDbref = page.AuthorDbref,
			LastEditorDbref = page.LastEditorDbref,
			CreatedAt = WikiTimestamp(page.CreatedAt),
			UpdatedAt = WikiTimestamp(page.UpdatedAt),
			RevisionNumber = 1,
			Categories = [.. page.Categories],
			Published = page.Published,
			SourceLocale = page.SourceLocale
		};

		// The duplicate check and the insert share one write job, so two creators of the same
		// (namespace, slug) cannot both pass the check.
		return await Store.WriteAsync<Result<WikiPage>>(tx =>
		{
			var slugKey = WikiSlugKey(record.Namespace, record.Slug);
			if (tx.TryGet(Tables.WikiSlug, slugKey, out _))
			{
				return new Error<string>(
					$"A wiki page with slug '{record.Slug}' already exists in namespace '{record.Namespace}'.");
			}

			var key = AllocateWikiId(tx);
			PutWikiPage(tx, key, null, record);
			tx.Put(Tables.WikiSlug, slugKey, Keys.Dbref(key));

			var stored = MapWikiPage(key, record);
			AppendWikiRevision(tx, stored.Id, string.Empty, 1, record.MarkdownSource, record.AuthorDbref, null, page.CreatedAt);
			return stored;
		});
	}

	public async Task<Found<WikiPage>> UpdatePageBodyAsync(string id, WikiBody body, string editorDbref, string? editSummary,
		DateTimeOffset at)
		=> await Store.WriteAsync<Found<WikiPage>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			var revision = found.Record.RevisionNumber + 1;
			var updated = found.Record with
			{
				MarkdownSource = body.Markdown,
				RenderedHtml = body.Html,
				PlainText = body.PlainText,
				LastEditorDbref = editorDbref,
				UpdatedAt = WikiTimestamp(at),
				RevisionNumber = revision
			};

			PutWikiPage(tx, found.Key, found.Record, updated);

			var page = MapWikiPage(found.Key, updated);
			AppendWikiRevision(tx, page.Id, string.Empty, revision, body.Markdown, editorDbref, editSummary, at);
			return page;
		});

	public async Task<Found<None>> DeletePageAsync(string id)
		=> await Store.WriteAsync<Found<None>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			var pageId = WikiPageId(found.Key);

			// One prefix sweep per child table: every locale's revision stream and every translation of
			// this page share the page id as their first key segment.
			tx.DeletePrefix(Tables.WikiRev, WikiPagePrefix(pageId));
			tx.DeletePrefix(Tables.WikiTr, WikiPagePrefix(pageId));
			tx.Delete(Tables.WikiSlug, WikiSlugKey(found.Record.Namespace, found.Record.Slug));
			WikiListIndexes(tx, found.Key, found.Record, add: false);
			tx.Delete(Tables.WikiPage, WikiPageKey(found.Key));
			tx.Delete(Tables.WikiRequirement, WikiRequirementKey(WikiRuleScope.Page, pageId));
			return new None();
		});

	public async Task<Found<WikiPage>> SetPageMetadataAsync(string id, IReadOnlyList<string> categories, bool published)
		=> await Store.WriteAsync<Found<WikiPage>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			var updated = found.Record with { Categories = [.. categories], Published = published };
			PutWikiPage(tx, found.Key, found.Record, updated);
			return MapWikiPage(found.Key, updated);
		});

	/// <summary>The stream read backwards: its keys end with the big-endian revision number, so key order is
	/// revision order and a page costs <c>skip + take</c> entries with only <c>take</c> decoded.</summary>
	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, string locale, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiRevision>>(Store.Read(tx =>
			tx.RangeReverse(Tables.WikiRev, WikiRevPrefix(CanonicalWikiPageId(pageId), locale))
				.Skip(skip)
				.Take(take)
				.Select(entry => MapWikiRevision(Codec.Deserialize<WikiRevisionRecord>(entry.Value)))
				.ToList()));

	/// <summary>The cursor form: one seek to <paramref name="beforeRevisionNumber"/>, then <paramref name="take"/> entries.</summary>
	public Task<IReadOnlyList<WikiRevision>> GetRevisionsBeforeAsync(string pageId, string locale, int beforeRevisionNumber, int take)
		=> Task.FromResult<IReadOnlyList<WikiRevision>>(beforeRevisionNumber <= 0
			? []
			: Store.Read(tx =>
			{
				var canonical = CanonicalWikiPageId(pageId);
				return tx.RangeReverseBefore(Tables.WikiRev, WikiRevPrefix(canonical, locale),
						WikiRevKey(canonical, locale, beforeRevisionNumber))
					.Take(take)
					.Select(entry => MapWikiRevision(Codec.Deserialize<WikiRevisionRecord>(entry.Value)))
					.ToList();
			}));

	public Task<Found<WikiRevision>> GetRevisionAsync(string pageId, string locale, int revisionNumber)
		=> Task.FromResult(Store.Read<Found<WikiRevision>>(tx =>
			tx.TryGet(Tables.WikiRev, WikiRevKey(CanonicalWikiPageId(pageId), locale, revisionNumber), out var bytes)
				? MapWikiRevision(Codec.Deserialize<WikiRevisionRecord>(bytes))
				: new NotFound()));

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationSummariesAsync(string pageId)
		=> Task.FromResult<IReadOnlyList<WikiTranslationSummary>>(Store.Read(tx =>
			tx.Range(Tables.WikiTr, WikiPagePrefix(CanonicalWikiPageId(pageId)))
				.Select(entry => MapWikiTranslation(Codec.Deserialize<WikiTranslationRecord>(entry.Value)))
				.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
				.ToList()));

	public Task<IReadOnlyList<WikiTranslation>> GetTranslationsAsync(int skip, int take)
		// The key is (pageId, locale), so the natural scan order is already "page then locale".
		=> Task.FromResult<IReadOnlyList<WikiTranslation>>(Store.Read(tx =>
			tx.Range(Tables.WikiTr, [])
				.Skip(skip)
				.Take(take)
				.Select(entry => MapWikiTranslation(Codec.Deserialize<WikiTranslationRecord>(entry.Value)))
				.ToList()));

	public Task<Found<WikiTranslation>> GetTranslationAsync(string pageId, string locale)
		=> Task.FromResult(Store.Read<Found<WikiTranslation>>(tx =>
			tx.TryGet(Tables.WikiTr, WikiTranslationKey(CanonicalWikiPageId(pageId), locale), out var bytes)
				? MapWikiTranslation(Codec.Deserialize<WikiTranslationRecord>(bytes))
				: new NotFound()));

	/// <summary>
	/// Writes a translation as a create or a compare-and-swap on the revision the editor loaded.
	/// </summary>
	public async Task<TranslationWriteResult> WriteTranslationAsync(string pageId, string locale, string title,
		WikiBody body, string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber,
		DateTimeOffset at)
	{
		var stamp = WikiTimestamp(at);

		// The compare-and-swap, the row write and the revision append are one job on the writer thread.
		// Never make this an unconditional write: two translators who both loaded revision 4 would both
		// write 5 and one would lose their prose.
		return await Store.WriteAsync<TranslationWriteResult>(tx =>
		{
			if (TryReadWikiPage(tx, pageId) is not { } found) return new Error<string>($"No wiki page with id '{pageId}'.");

			var canonicalPageId = WikiPageId(found.Key);
			var key = WikiTranslationKey(canonicalPageId, locale);
			var existing = tx.TryGet(Tables.WikiTr, key, out var bytes)
				? Codec.Deserialize<WikiTranslationRecord>(bytes)
				: null;

			int revision;
			string created;
			if (expectedRevisionNumber is null)
			{
				// Create-only: an existing row is a lost write, not a blind overwrite.
				if (existing is not null) return WikiWriteConflict.AlreadyExists;

				revision = 1;
				created = stamp;
			}
			else
			{
				if (existing is null) return WikiWriteConflict.TranslationGone;
				if ((existing.RevisionNumber ?? 1) != expectedRevisionNumber.Value) return WikiWriteConflict.StaleRevision;

				revision = expectedRevisionNumber.Value + 1;
				created = existing.CreatedAt ?? stamp;
			}

			var record = new WikiTranslationRecord
			{
				PageId = canonicalPageId,
				Locale = locale,
				Title = title,
				MarkdownSource = body.Markdown,
				RenderedHtml = body.Html,
				PlainText = body.PlainText,
				LastEditorDbref = editorDbref,
				CreatedAt = created,
				UpdatedAt = stamp,
				Published = published,
				RevisionNumber = revision
			};

			tx.Put(Tables.WikiTr, key, Codec.Serialize(record));
			AppendWikiRevision(tx, canonicalPageId, locale, revision, body.Markdown, editorDbref, editSummary, at);
			return MapWikiTranslation(record);
		});
	}

	public async Task<Found<None>> DeleteTranslationAsync(string pageId, string locale)
		=> await Store.WriteAsync<Found<None>>(tx =>
		{
			var canonicalPageId = CanonicalWikiPageId(pageId);
			var key = WikiTranslationKey(canonicalPageId, locale);
			if (!tx.TryGet(Tables.WikiTr, key, out _)) return new NotFound();

			tx.DeletePrefix(Tables.WikiRev, WikiRevPrefix(canonicalPageId, locale));
			tx.Delete(Tables.WikiTr, key);
			return new None();
		});
}
