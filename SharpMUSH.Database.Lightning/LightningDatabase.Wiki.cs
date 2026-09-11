using System.Globalization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IWikiService"/>: wiki pages, their revision streams and their translations. Ported from
/// <c>SurrealDatabase.Wiki.cs</c>.
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
/// Every mutation is one write job on the single writer thread, so <c>UpsertTranslationAsync</c>'s
/// compare-and-swap reads the stored revision number and appends its revision inside the same
/// transaction: two writers holding the same <c>expectedRevisionNumber</c> cannot both win, and the
/// loser leaves no revision behind.
/// </para>
/// </remarks>
public partial class LightningDatabase
{
	private static readonly WikiMarkdigPipeline WikiRenderer = new();

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

	public Task<Found<WikiPage>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var key = WikiSlugKey(nsStr, category, WikiHelpers.Slugify(slug));

		return Task.FromResult(Store.Read<Found<WikiPage>>(tx =>
		{
			if (!tx.TryGet(Tables.WikiSlug, key, out var idBytes)) return new NotFound();

			var pageKey = Keys.ReadDbref(idBytes);
			return TryReadWikiPage(tx, pageKey) is { } record ? MapWikiPage(pageKey, record) : new NotFound();
		}));
	}

	public Task<Found<WikiPage>> GetByIdAsync(string id)
		=> Task.FromResult(Store.Read<Found<WikiPage>>(tx =>
			TryReadWikiPage(tx, id) is { } found ? MapWikiPage(found.Key, found.Record) : new NotFound()));

	public Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.OrderByDescending(p => p.UpdatedAt)
			// Id descending as the tie-break so two pages written inside one timestamp tick still order
			// newest-first rather than by whatever the key scan happened to yield.
			.ThenByDescending(p => TryParseWikiPageId(p.Id, out var key) ? key : 0)
			.Take(count)
			.ToList()));

	public Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		return Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.Where(p => p.Namespace.Equals(nsStr, StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p.Slug, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList()));
	}

	public Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null)
	{
		var nsStr = ns?.ToString().ToLowerInvariant();
		return Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.Where(p => nsStr is null || p.Namespace.Equals(nsStr, StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p.Namespace, StringComparer.Ordinal)
			.ThenBy(p => p.Slug, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList()));
	}

	public Task<int> CountPagesAsync(WikiNamespace? ns, bool includeDrafts)
	{
		var nsStr = ns?.ToString().ToLowerInvariant();
		// `Published ?? true` rather than `== true`, so a row written before the field existed counts the
		// same way it displays. Defence in depth: every write path here sets it.
		return Task.FromResult(Store.Read(tx => AllWikiPages(tx)
			.Count(p => (nsStr is null || p.Record.Namespace.Equals(nsStr, StringComparison.OrdinalIgnoreCase))
				&& (includeDrafts || (p.Record.Published ?? true)))));
	}

	public Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50)
	{
		var normalized = WikiHelpers.NormalizeCategory(category);
		return Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.Where(p => p.Category is not null && p.Category.Equals(normalized, StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p.Title, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList()));
	}

	public Task<IReadOnlyList<WikiPage>> GetByTagAsync(string tag, int skip = 0, int take = 50)
	{
		var normalized = tag.Trim().ToLowerInvariant();
		return Task.FromResult<IReadOnlyList<WikiPage>>(Store.Read(tx => AllWikiPagesMapped(tx)
			.Where(p => p.Tags.Contains(normalized, StringComparer.OrdinalIgnoreCase))
			.OrderBy(p => p.Title, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList()));
	}

	public async Task<Result<WikiPage>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? category = null,
		string? sourceLocale = null)
	{
		// SourceLocale is materialised once and never re-derived, so a junk tag must not reach storage.
		// Null or blank is the "not stamped" case, left to the migration backfill rather than an error;
		// a non-blank tag that is not a locale is an error, because storing it would corrupt every later read.
		var stampedLocale = string.Empty;
		if (!string.IsNullOrWhiteSpace(sourceLocale))
		{
			switch (WikiHelpers.NormalizeLocale(sourceLocale))
			{
				case Error<string> error:
					return error;
				case string normalizedSource:
					stampedLocale = normalizedSource;
					break;
			}
		}

		var nsStr = ns.ToString().ToLowerInvariant();
		var slug = WikiHelpers.Slugify(title);
		var cat = WikiHelpers.NormalizeCategory(category);
		var now = DateTimeOffset.UtcNow;
		var stamp = WikiTimestamp(now);

		var record = new WikiPageRecord
		{
			Slug = slug,
			Title = title,
			Namespace = nsStr,
			MarkdownSource = markdown,
			RenderedHtml = WikiRenderer.RenderToHtml(markdown),
			PlainText = WikiRenderer.ExtractPlainText(markdown),
			AuthorDbref = authorDbref,
			LastEditorDbref = authorDbref,
			CreatedAt = stamp,
			UpdatedAt = stamp,
			IsProtected = false,
			RevisionNumber = 1,
			Category = cat,
			Tags = [],
			Published = true,
			SourceLocale = stampedLocale
		};

		// The duplicate check and the insert share one write job, so two creators of the same
		// (namespace, category, slug) cannot both pass the check.
		return await Store.WriteAsync<Result<WikiPage>>(tx =>
		{
			var slugKey = WikiSlugKey(nsStr, cat, slug);
			if (tx.TryGet(Tables.WikiSlug, slugKey, out _))
			{
				return new Error<string>(
					$"A wiki page with slug '{slug}' already exists in namespace '{nsStr}' category '{cat}'.");
			}

			var key = AllocateWikiId(tx);
			tx.Put(Tables.WikiPage, WikiPageKey(key), Codec.Serialize(record));
			tx.Put(Tables.WikiSlug, slugKey, Keys.Dbref(key));

			var page = MapWikiPage(key, record);
			AppendWikiRevision(tx, page.Id, string.Empty, 1, markdown, authorDbref, null, now);
			return page;
		});
	}

	public async Task<Found<WikiPage>> UpdateAsync(
		string id,
		string markdown,
		string editorDbref,
		string? editSummary = null)
	{
		var now = DateTimeOffset.UtcNow;
		var html = WikiRenderer.RenderToHtml(markdown);
		var plain = WikiRenderer.ExtractPlainText(markdown);

		return await Store.WriteAsync<Found<WikiPage>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			var revision = found.Record.RevisionNumber + 1;
			var updated = found.Record with
			{
				MarkdownSource = markdown,
				RenderedHtml = html,
				PlainText = plain,
				LastEditorDbref = editorDbref,
				UpdatedAt = WikiTimestamp(now),
				RevisionNumber = revision
			};

			tx.Put(Tables.WikiPage, WikiPageKey(found.Key), Codec.Serialize(updated));

			var page = MapWikiPage(found.Key, updated);
			AppendWikiRevision(tx, page.Id, string.Empty, revision, markdown, editorDbref, editSummary, now);
			return page;
		});
	}

	public async Task<Found<None>> DeleteAsync(string id, string editorDbref)
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

	public async Task<Found<None>> SetProtectionAsync(string id, bool isProtected)
		=> await Store.WriteAsync<Found<None>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			tx.Put(Tables.WikiPage, WikiPageKey(found.Key),
				Codec.Serialize(found.Record with { IsProtected = isProtected }));
			return new None();
		});

	public async Task<Found<WikiPage>> SetMetadataAsync(
		string id,
		string? category,
		IReadOnlyList<string> tags,
		bool published)
	{
		var normalizedCategory = WikiHelpers.NormalizeCategory(category);
		var normalizedTags = WikiHelpers.NormalizeTags(tags).ToArray();

		return await Store.WriteAsync<Found<WikiPage>>(tx =>
		{
			if (TryReadWikiPage(tx, id) is not { } found) return new NotFound();

			var existingCategory = WikiHelpers.NormalizeCategory(found.Record.Category);
			var recategorized = !string.Equals(normalizedCategory, existingCategory, StringComparison.OrdinalIgnoreCase);
			var oldSlugKey = WikiSlugKey(found.Record.Namespace, existingCategory, found.Record.Slug);
			var newSlugKey = WikiSlugKey(found.Record.Namespace, normalizedCategory, found.Record.Slug);

			// Category is part of page identity, so a recategorization that would collide is refused —
			// the same NotFound the SurrealDB partial answers with.
			if (recategorized && tx.TryGet(Tables.WikiSlug, newSlugKey, out _)) return new NotFound();

			var updated = found.Record with
			{
				Category = normalizedCategory,
				Tags = normalizedTags,
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
	}

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
		=> GetRevisionsForLocaleAsync(pageId, string.Empty, skip, take);

	public Task<Found<WikiRevision>> GetRevisionAsync(string pageId, int revisionNumber)
		=> GetRevisionForLocaleAsync(pageId, string.Empty, revisionNumber);

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
		=> Task.FromResult<IReadOnlyList<WikiTranslationSummary>>(Store.Read(tx =>
			tx.Range(Tables.WikiTr, WikiPagePrefix(CanonicalWikiPageId(pageId)))
				.Select(entry => MapWikiTranslation(Codec.Deserialize<WikiTranslationRecord>(entry.Value)))
				.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
				.ToList()));

	public Task<IReadOnlyList<WikiTranslation>> GetAllTranslationsAsync(int skip = 0, int take = 50)
		// The key is (pageId, locale), so the natural scan order is already "page then locale".
		=> Task.FromResult<IReadOnlyList<WikiTranslation>>(Store.Read(tx =>
			tx.Range(Tables.WikiTr, [])
				.Skip(skip)
				.Take(take)
				.Select(entry => MapWikiTranslation(Codec.Deserialize<WikiTranslationRecord>(entry.Value)))
				.ToList()));

	public Task<Found<WikiTranslation>> GetTranslationAsync(string pageId, string locale)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return Task.FromResult<Found<WikiTranslation>>(new NotFound());

		return Task.FromResult(Store.Read<Found<WikiTranslation>>(tx =>
			tx.TryGet(Tables.WikiTr, WikiTranslationKey(CanonicalWikiPageId(pageId), normalized), out var bytes)
				? MapWikiTranslation(Codec.Deserialize<WikiTranslationRecord>(bytes))
				: new NotFound()));
	}

	public async Task<TranslationWriteResult> UpsertTranslationAsync(
		string pageId,
		string locale,
		string title,
		string markdown,
		string editorDbref,
		string? editSummary,
		bool published,
		int? expectedRevisionNumber)
		=> WikiHelpers.NormalizeLocale(locale) switch
		{
			string normalized => await WriteTranslationAsync(
				pageId, normalized, title, markdown, editorDbref, editSummary, published, expectedRevisionNumber),
			Error<string> error => error,
		};

	/// <summary>
	/// Writes a translation under an already-normalized locale, as a create or a compare-and-swap on the
	/// revision the editor loaded.
	/// </summary>
	private async Task<TranslationWriteResult> WriteTranslationAsync(
		string pageId,
		string normalized,
		string title,
		string markdown,
		string editorDbref,
		string? editSummary,
		bool published,
		int? expectedRevisionNumber)
	{
		var now = DateTimeOffset.UtcNow;
		var stamp = WikiTimestamp(now);
		var html = WikiRenderer.RenderToHtml(markdown);
		var plain = WikiRenderer.ExtractPlainText(markdown);

		// The compare-and-swap, the row write and the revision append are one job on the writer thread.
		// Never make this an unconditional write: two translators who both loaded revision 4 would both
		// write 5 and one would lose their prose.
		return await Store.WriteAsync<TranslationWriteResult>(tx =>
		{
			if (TryReadWikiPage(tx, pageId) is not { } found) return new Error<string>($"No wiki page with id '{pageId}'.");

			var canonicalPageId = WikiPageId(found.Key);
			var sourceLocale = found.Record.SourceLocale ?? string.Empty;
			if (sourceLocale.Length > 0 && string.Equals(sourceLocale, normalized, StringComparison.OrdinalIgnoreCase))
			{
				return new Error<string>(
					$"'{normalized}' is the page's source locale; edit the page itself rather than adding a translation.");
			}

			var key = WikiTranslationKey(canonicalPageId, normalized);
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
				Locale = normalized,
				Title = title,
				MarkdownSource = markdown,
				RenderedHtml = html,
				PlainText = plain,
				LastEditorDbref = editorDbref,
				CreatedAt = created,
				UpdatedAt = stamp,
				Published = published,
				RevisionNumber = revision
			};

			tx.Put(Tables.WikiTr, key, Codec.Serialize(record));
			AppendWikiRevision(tx, canonicalPageId, normalized, revision, markdown, editorDbref, editSummary, now);
			return MapWikiTranslation(record);
		});
	}

	public async Task<Found<None>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		return await Store.WriteAsync<Found<None>>(tx =>
		{
			var canonicalPageId = CanonicalWikiPageId(pageId);
			var key = WikiTranslationKey(canonicalPageId, normalized);
			if (!tx.TryGet(Tables.WikiTr, key, out _)) return new NotFound();

			tx.DeletePrefix(Tables.WikiRev, WikiRevPrefix(canonicalPageId, normalized));
			tx.Delete(Tables.WikiTr, key);
			return new None();
		});
	}

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);
		return Task.FromResult<IReadOnlyList<WikiRevision>>(Store.Read(tx =>
			RangeWikiRevisions(tx, CanonicalWikiPageId(pageId), wanted)
				.OrderByDescending(r => r.RevisionNumber)
				.Skip(skip)
				.Take(take)
				.ToList()));
	}

	public Task<Found<WikiRevision>> GetRevisionForLocaleAsync(string pageId, string locale, int revisionNumber)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (revisionNumber < 0) return Task.FromResult<Found<WikiRevision>>(new NotFound());

		return Task.FromResult(Store.Read<Found<WikiRevision>>(tx =>
			tx.TryGet(Tables.WikiRev, WikiRevKey(CanonicalWikiPageId(pageId), wanted, revisionNumber), out var bytes)
				? MapWikiRevision(Codec.Deserialize<WikiRevisionRecord>(bytes))
				: new NotFound()));
	}
}
