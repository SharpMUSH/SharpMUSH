using System.Globalization;
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
/// <c>(namespace, category, slug)</c>, keyed by <see cref="WikiHelpers.SlugKey"/>'s string so the
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

	private static byte[] WikiSlugKey(string nsStr, string? category, string slug)
		=> Keys.Lower(WikiHelpers.SlugKey(nsStr, category, slug));

	private static byte[] WikiRevKey(string pageId, string locale, int revisionNumber)
		=> Keys.Composite(pageId, locale, (uint)revisionNumber);

	/// <summary>The key range of one <c>(pageId, locale)</c> revision stream.</summary>
	private static byte[] WikiRevPrefix(string pageId, string locale)
		=> Keys.Concat(Keys.Str(pageId), Keys.Sep, Keys.Str(locale), Keys.Sep);

	/// <summary>The key range of everything belonging to one page — its translations, or every one of its
	/// revision streams whatever the locale.</summary>
	private static byte[] WikiPagePrefix(string pageId) => Keys.Concat(Keys.Str(pageId), Keys.Sep);

	private static byte[] WikiTranslationKey(string pageId, string locale) => Keys.Composite(pageId, locale);

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

	private static IEnumerable<(long Key, WikiPageRecord Record)> AllWikiPages(ITx tx)
		=> tx.Range(Tables.WikiPage, [])
			.Select(entry => (Keys.ReadDbref(entry.Key), Codec.Deserialize<WikiPageRecord>(entry.Value)));

	private static IEnumerable<WikiPage> AllWikiPagesMapped(ITx tx)
		=> AllWikiPages(tx).Select(page => MapWikiPage(page.Key, page.Record));

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
		IsProtected: r.IsProtected,
		RevisionNumber: r.RevisionNumber)
	{
		Category = string.IsNullOrEmpty(r.Category) ? null : r.Category,
		Tags = r.Tags ?? [],
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

	private static IEnumerable<WikiRevision> RangeWikiRevisions(ITx tx, string pageId, string locale)
		=> tx.Range(Tables.WikiRev, WikiRevPrefix(pageId, locale))
			.Select(entry => MapWikiRevision(Codec.Deserialize<WikiRevisionRecord>(entry.Value)));

	public Task<Found<WikiPage>> GetPageBySlugAsync(string ns, string category, string slug)
		=> Task.FromResult(Store.Read<Found<WikiPage>>(tx =>
		{
			if (!tx.TryGet(Tables.WikiSlug, WikiSlugKey(ns, category, slug), out var idBytes)) return new NotFound();

			var pageKey = Keys.ReadDbref(idBytes);
			return TryReadWikiPage(tx, pageKey) is { } record ? MapWikiPage(pageKey, record) : new NotFound();
		}));

	public Task<Found<WikiPage>> GetPageByIdAsync(string id)
		=> Task.FromResult(Store.Read<Found<WikiPage>>(tx =>
			TryReadWikiPage(tx, id) is { } found ? MapWikiPage(found.Key, found.Record) : new NotFound()));

	public Task<IReadOnlyList<WikiPage>> GetRecentPagesAsync(int count)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.OrderByDescending(p => p.UpdatedAt)
			// Id descending as the tie-break so two pages written inside one timestamp tick still order
			// newest-first rather than by whatever the key scan happened to yield.
			.ThenByDescending(p => TryParseWikiPageId(p.Id, out var key) ? key : 0)
			.Take(count)
			.ToList()));

	public Task<IReadOnlyList<WikiPage>> GetPagesAsync(string? ns, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.Where(p => ns is null || p.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p.Namespace, StringComparer.Ordinal)
			.ThenBy(p => p.Slug, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList()));

	// `Published ?? true` rather than `== true`, so a row written before the field existed counts the
	// same way it displays. Defence in depth: every write path here sets it.
	public Task<int> CountPagesAsync(string? ns, bool includeDrafts)
		=> Task.FromResult(Store.Read(tx => AllWikiPages(tx)
			.Count(p => (ns is null || p.Record.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase))
				&& (includeDrafts || (p.Record.Published ?? true)))));

	public Task<IReadOnlyList<WikiPage>> GetPagesByCategoryAsync(string category, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.Where(p => p.Category is not null && p.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p.Title, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList()));

	public Task<IReadOnlyList<WikiPage>> GetPagesByTagAsync(string tag, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.Where(p => p.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
			.OrderBy(p => p.Title, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList()));

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
			IsProtected = page.IsProtected,
			RevisionNumber = 1,
			Category = page.Category,
			Tags = [.. page.Tags],
			Published = page.Published,
			SourceLocale = page.SourceLocale
		};

		// The duplicate check and the insert share one write job, so two creators of the same
		// (namespace, category, slug) cannot both pass the check.
		return await Store.WriteAsync<Result<WikiPage>>(tx =>
		{
			var slugKey = WikiSlugKey(record.Namespace, record.Category, record.Slug);
			if (tx.TryGet(Tables.WikiSlug, slugKey, out _))
			{
				return new Error<string>(
					$"A wiki page with slug '{record.Slug}' already exists in namespace '{record.Namespace}' category '{record.Category}'.");
			}

			var key = AllocateWikiId(tx);
			tx.Put(Tables.WikiPage, WikiPageKey(key), Codec.Serialize(record));
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

			tx.Put(Tables.WikiPage, WikiPageKey(found.Key), Codec.Serialize(updated));

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
			tx.Delete(Tables.WikiSlug, WikiSlugKey(found.Record.Namespace, found.Record.Category, found.Record.Slug));
			tx.Delete(Tables.WikiPage, WikiPageKey(found.Key));
			return new None();
		});

	public async Task<Found<None>> SetPageProtectionAsync(string id, bool isProtected)
		=> await Store.WriteAsync<Found<None>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			tx.Put(Tables.WikiPage, WikiPageKey(found.Key),
				Codec.Serialize(found.Record with { IsProtected = isProtected }));
			return new None();
		});

	public async Task<Found<WikiPage>> SetPageMetadataAsync(string id, string category, IReadOnlyList<string> tags,
		bool published)
		=> await Store.WriteAsync<Found<WikiPage>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			// A row written before categories were stamped reads as the default category, which is the key its
			// slug index entry was written under.
			var existingCategory = WikiHelpers.NormalizeCategory(found.Record.Category);
			var recategorized = !string.Equals(category, existingCategory, StringComparison.OrdinalIgnoreCase);
			var oldSlugKey = WikiSlugKey(found.Record.Namespace, existingCategory, found.Record.Slug);
			var newSlugKey = WikiSlugKey(found.Record.Namespace, category, found.Record.Slug);

			// Category is part of page identity, so a recategorization that would collide is refused.
			if (recategorized && tx.TryGet(Tables.WikiSlug, newSlugKey, out _)) return new NotFound();

			var updated = found.Record with
			{
				Category = category,
				Tags = [.. tags],
				Published = published
			};

			if (recategorized)
			{
				tx.Delete(Tables.WikiSlug, oldSlugKey);
				tx.Put(Tables.WikiSlug, newSlugKey, Keys.Dbref(found.Key));
			}

			tx.Put(Tables.WikiPage, WikiPageKey(found.Key), Codec.Serialize(updated));
			return MapWikiPage(found.Key, updated);
		});

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, string locale, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiRevision>>(Store.Read(tx =>
			RangeWikiRevisions(tx, CanonicalWikiPageId(pageId), locale)
				.OrderByDescending(r => r.RevisionNumber)
				.Skip(skip)
				.Take(take)
				.ToList()));

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
