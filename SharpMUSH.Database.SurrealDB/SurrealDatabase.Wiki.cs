using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;
using SurrealDb.Net.Models;
using OkNone = OneOf.Types.None;

namespace SharpMUSH.Database.SurrealDB;

// IMPORTANT: SurrealDb.Net 0.9.0 embedded CBOR serializer ignores [JsonPropertyName].
// Property names MUST exactly match the SurrealDB field names stored in the DB.
// All camelCase fields (e.g. markdownSource) require a camelCase C# property name.
internal class WikiPageDbRecord : Record
{
	public string slug { get; set; } = "";
	public string title { get; set; } = "";
	public string @namespace { get; set; } = "main";
	public string markdownSource { get; set; } = "";
	public string renderedHtml { get; set; } = "";
	public string plainText { get; set; } = "";
	public string authorDbref { get; set; } = "";
	public string lastEditorDbref { get; set; } = "";
	public string createdAt { get; set; } = "";
	public string updatedAt { get; set; } = "";
	public bool isProtected { get; set; }
	public int revisionNumber { get; set; } = 1;
	public string? category { get; set; }
	public List<string>? tags { get; set; }
	// Nullable so records created before the field existed deserialize as null → default true.
	public bool? published { get; set; }
	// Nullable so records predating the wiki-translations migration deserialize as null → empty,
	// i.e. "not yet stamped". Never re-derived from the configured default on read.
	public string? sourceLocale { get; set; }
}

internal class WikiCountRecord : Record
{
	public int count { get; set; }
}

internal class WikiRevisionDbRecord : Record
{
	public string pageId { get; set; } = "";
	public int revisionNumber { get; set; }
	public string markdownSource { get; set; } = "";
	public string editorDbref { get; set; } = "";
	public string timestamp { get; set; } = "";
	public string? editSummary { get; set; }
	// Nullable so rows predating the wiki-translations migration deserialize as null → empty, the
	// canonical marker for the source-locale stream.
	public string? locale { get; set; }
}

internal class WikiTranslationDbRecord : Record
{
	public string? pageId { get; set; }
	public string? locale { get; set; }
	public string? title { get; set; }
	public string? markdownSource { get; set; }
	public string? renderedHtml { get; set; }
	public string? plainText { get; set; }
	public string? lastEditorDbref { get; set; }
	public string? createdAt { get; set; }
	public string? updatedAt { get; set; }
	public bool? published { get; set; }
	public int? revisionNumber { get; set; }
}

public partial class SurrealDatabase : IWikiService
{
	#region Wiki

	private static readonly WikiMarkdigPipeline _wikiRenderer = new();

	private const string WikiPageFields =
			"id, slug, title, namespace, markdownSource, renderedHtml, plainText, " +
			"authorDbref, lastEditorDbref, createdAt, updatedAt, isProtected, revisionNumber, " +
			"category, tags, published, sourceLocale";

	private const string WikiRevisionFields =
			"id, pageId, revisionNumber, markdownSource, editorDbref, timestamp, editSummary, locale";

	private const string WikiTranslationFields =
			"id, pageId, locale, title, markdownSource, renderedHtml, plainText, " +
			"lastEditorDbref, createdAt, updatedAt, published, revisionNumber";

	public async Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var cat = WikiHelpers.NormalizeCategory(category);
		var parameters = new Dictionary<string, object?> { ["ns"] = nsStr, ["cat"] = cat, ["slug"] = Slugify(slug) };
		var response = await ExecuteAsync(
				$"SELECT {WikiPageFields} FROM wiki_page WHERE namespace = $ns AND category = $cat AND slug = $slug",
				parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		if (results?.Count > 0)
			return MapToWikiPage(results[0]);
		return new NotFound();
	}

	public async Task<OneOf<WikiPage, NotFound>> GetByIdAsync(string id)
	{
		var key = NormalizeSurrealId(id, "wiki_page");
		var parameters = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
		var response = await ExecuteAsync(
				$"SELECT {WikiPageFields} FROM $id",
				parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		if (results?.Count > 0)
			return MapToWikiPage(results[0]);
		return new NotFound();
	}

	public async Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20)
	{
		var parameters = new Dictionary<string, object?> { ["count"] = count };
		var response = await ExecuteAsync(
				$"SELECT {WikiPageFields} FROM wiki_page ORDER BY updatedAt DESC LIMIT $count",
				parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		return (results?.Select(MapToWikiPage).ToList() ?? []).AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var parameters = new Dictionary<string, object?>
		{
			["ns"] = nsStr, ["skip"] = skip, ["take"] = take
		};
		var response = await ExecuteAsync(
				$"SELECT {WikiPageFields} FROM wiki_page WHERE namespace = $ns ORDER BY slug ASC LIMIT $take START $skip",
				parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		return (results?.Select(MapToWikiPage).ToList() ?? []).AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null)
	{
		var parameters = new Dictionary<string, object?> { ["skip"] = skip, ["take"] = take };
		var where = string.Empty;
		if (ns is not null)
		{
			where = "WHERE namespace = $ns ";
			parameters["ns"] = ns.Value.ToString().ToLowerInvariant();
		}

		var response = await ExecuteAsync(
				$"SELECT {WikiPageFields} FROM wiki_page {where}ORDER BY namespace ASC, slug ASC LIMIT $take START $skip",
				parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		return (results?.Select(MapToWikiPage).ToList() ?? []).AsReadOnly();
	}

	public async Task<int> CountPagesAsync(WikiNamespace? ns = null)
	{
		var parameters = new Dictionary<string, object?>();
		var where = string.Empty;
		if (ns is not null)
		{
			where = "WHERE namespace = $ns ";
			parameters["ns"] = ns.Value.ToString().ToLowerInvariant();
		}

		var response = await ExecuteAsync(
				$"SELECT count() FROM wiki_page {where}GROUP ALL",
				parameters);
		var results = response.GetValue<List<WikiCountRecord>>(0);
		return results?.FirstOrDefault()?.count ?? 0;
	}

	public async Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50)
	{
		var parameters = new Dictionary<string, object?>
		{
			["cat"] = WikiHelpers.NormalizeCategory(category) ?? string.Empty,
			["skip"] = skip,
			["take"] = take
		};
		var response = await ExecuteAsync(
				$"SELECT {WikiPageFields} FROM wiki_page WHERE category = $cat ORDER BY title ASC LIMIT $take START $skip",
				parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		return (results?.Select(MapToWikiPage).ToList() ?? []).AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetByTagAsync(string tag, int skip = 0, int take = 50)
	{
		var parameters = new Dictionary<string, object?>
		{
			["tag"] = tag.Trim().ToLowerInvariant(),
			["skip"] = skip,
			["take"] = take
		};
		// The ?? [] default guards records created before the tags field existed.
		var response = await ExecuteAsync(
				$"SELECT {WikiPageFields} FROM wiki_page WHERE $tag IN (tags ?? []) ORDER BY title ASC LIMIT $take START $skip",
				parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		return (results?.Select(MapToWikiPage).ToList() ?? []).AsReadOnly();
	}

	public async Task<OneOf<WikiPage, Error<string>>> CreateAsync(
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
			var normalizedSource = WikiHelpers.NormalizeLocale(sourceLocale);
			if (normalizedSource.IsT1)
				return normalizedSource.AsT1;

			stampedLocale = normalizedSource.AsT0;
		}

		var nsStr = ns.ToString().ToLowerInvariant();
		var slug = Slugify(title);
		var cat = WikiHelpers.NormalizeCategory(category);

		var existing = await GetBySlugAsync(slug, cat, ns);
		if (existing.IsT0)
			return new Error<string>($"A wiki page with slug '{slug}' already exists in namespace '{nsStr}' category '{cat}'.");

		var now = DateTimeOffset.UtcNow;
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		var parameters = new Dictionary<string, object?>
		{
			["slug"] = slug,
			["title"] = title,
			["ns"] = nsStr,
			["markdown"] = markdown,
			["html"] = html,
			["plain"] = plain,
			["authorDbref"] = authorDbref,
			["cat"] = cat,
			["now"] = now.ToString("O"),
			["sourceLocale"] = stampedLocale
		};

		var response = await ExecuteAsync("""
            CREATE wiki_page CONTENT {
            	slug: $slug,
            	title: $title,
            	namespace: $ns,
            	markdownSource: $markdown,
            	renderedHtml: $html,
            	plainText: $plain,
            	authorDbref: $authorDbref,
            	lastEditorDbref: $authorDbref,
            	createdAt: $now,
            	updatedAt: $now,
            	isProtected: false,
            	revisionNumber: 1,
            	category: $cat,
            	tags: [],
            	published: true,
            	sourceLocale: $sourceLocale
            }
            """,
				parameters);

		// C-8: Guard against empty DB result set to avoid InvalidOperationException.
		var createList = response.GetValue<List<WikiPageDbRecord>>(0);
		if (createList is null or { Count: 0 })
			return new Error<string>("Database returned empty result after insert.");
		var page = MapToWikiPage(createList[0]);

		await SaveSurrealRevisionAsync(page, authorDbref, null, now);
		return page;
	}

	public async Task<OneOf<WikiPage, NotFound>> UpdateAsync(
			string id,
			string markdown,
			string editorDbref,
			string? editSummary = null)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		var existing = lookupResult.AsT0;
		var now = DateTimeOffset.UtcNow;
		var newRevision = existing.RevisionNumber + 1;
		var key = NormalizeSurrealId(id, "wiki_page");
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		var parameters = new Dictionary<string, object?>
		{
			["id"] = new StringRecordId(key),
			["markdown"] = markdown,
			["html"] = html,
			["plain"] = plain,
			["editorDbref"] = editorDbref,
			["now"] = now.ToString("O"),
			["rev"] = newRevision
		};

		var response = await ExecuteAsync(
				"UPDATE $id MERGE { markdownSource: $markdown, renderedHtml: $html, plainText: $plain, " +
				"lastEditorDbref: $editorDbref, updatedAt: $now, revisionNumber: $rev }",
				parameters);

		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		// C-8: Guard against empty DB result set to avoid InvalidOperationException.
		if (results is null or { Count: 0 })
			return new NotFound();
		var updated = MapToWikiPage(results[0]);

		await SaveSurrealRevisionAsync(updated, editorDbref, editSummary, now);
		return updated;
	}

	public async Task<OneOf<OkNone, NotFound>> DeleteAsync(string id, string editorDbref)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		var parameters = new Dictionary<string, object?> { ["id"] = id };

		// The revision sweep above already covers translation revisions, so only the translation
		// rows themselves need their own statement.
		await ExecuteAsync("DELETE wiki_revision WHERE pageId = $id", parameters);
		await ExecuteAsync("DELETE wiki_translation WHERE pageId = $id", parameters);

		var key = NormalizeSurrealId(id, "wiki_page");
		var delParams = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
		await ExecuteAsync("DELETE $id", delParams);

		return new OkNone();
	}

	public async Task<OneOf<OkNone, NotFound>> SetProtectionAsync(string id, bool isProtected)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		var key = NormalizeSurrealId(id, "wiki_page");
		var parameters = new Dictionary<string, object?>
		{
			["id"] = new StringRecordId(key),
			["isProtected"] = isProtected
		};
		await ExecuteAsync("UPDATE $id MERGE { isProtected: $isProtected }", parameters);

		return new OkNone();
	}

	public async Task<OneOf<WikiPage, NotFound>> SetMetadataAsync(
			string id,
			string? category,
			IReadOnlyList<string> tags,
			bool published)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		var existingPage = lookupResult.AsT0;
		var normalizedCategory = WikiHelpers.NormalizeCategory(category);
		var normalizedTags = WikiHelpers.NormalizeTags(tags);

		// Category is part of page identity; reject a recategorization that would collide.
		if (!string.Equals(normalizedCategory, existingPage.Category, StringComparison.OrdinalIgnoreCase)
				&& Enum.TryParse<WikiNamespace>(existingPage.Namespace, ignoreCase: true, out var nsEnum)
				&& (await GetBySlugAsync(existingPage.Slug, normalizedCategory, nsEnum)).IsT0)
		{
			return new NotFound();
		}

		var key = NormalizeSurrealId(id, "wiki_page");
		var parameters = new Dictionary<string, object?>
		{
			["id"] = new StringRecordId(key),
			["cat"] = normalizedCategory,
			["tags"] = normalizedTags.ToList(),
			["pub"] = published
		};
		var response = await ExecuteAsync(
				"UPDATE $id MERGE { category: $cat, tags: $tags, published: $pub }",
				parameters);

		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		if (results is null or { Count: 0 })
			return new NotFound();
		return MapToWikiPage(results[0]);
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
	{
		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = pageId, ["skip"] = skip, ["take"] = take
		};
		var response = await ExecuteAsync(
				$"SELECT {WikiRevisionFields} FROM wiki_revision WHERE pageId = $pageId AND (locale ?? '') = '' " +
				$"ORDER BY revisionNumber DESC LIMIT $take START $skip",
				parameters);
		var results = response.GetValue<List<WikiRevisionDbRecord>>(0);
		return (results?.Select(MapToWikiRevision).ToList() ?? []).AsReadOnly();
	}

	public async Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber)
	{
		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = pageId, ["rev"] = revisionNumber
		};
		var response = await ExecuteAsync(
				$"SELECT {WikiRevisionFields} FROM wiki_revision WHERE pageId = $pageId AND revisionNumber = $rev " +
				"AND (locale ?? '') = ''",
				parameters);
		var results = response.GetValue<List<WikiRevisionDbRecord>>(0);
		if (results?.Count > 0)
			return MapToWikiRevision(results[0]);
		return new NotFound();
	}

	private async Task SaveSurrealRevisionAsync(
			WikiPage page,
			string editorDbref,
			string? editSummary,
			DateTimeOffset timestamp)
	{
		// locale is written explicitly as the empty source-stream marker rather than left absent, matching
		// what the migration backfill stamps on pre-existing rows. Storing it keeps every row's shape
		// identical across the two writers and keeps the (pageId, locale, revisionNumber) unique index
		// covering the source stream on the same terms as translation streams.
		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = page.Id,
			["locale"] = string.Empty,
			["rev"] = page.RevisionNumber,
			["markdown"] = page.MarkdownSource,
			["editorDbref"] = editorDbref,
			["timestamp"] = timestamp.ToString("O"),
			["editSummary"] = editSummary
		};

		await ExecuteAsync("""
            CREATE wiki_revision CONTENT {
            	pageId: $pageId,
            	locale: $locale,
            	revisionNumber: $rev,
            	markdownSource: $markdown,
            	editorDbref: $editorDbref,
            	timestamp: $timestamp,
            	editSummary: $editSummary
            }
            """,
				parameters);
	}

	private static string NormalizeWikiPageId(RecordId? id)
	{
		ArgumentNullException.ThrowIfNull(id);
		if (id.TryDeserializeId<string>(out var stringId))
			return $"wiki_page/{stringId}";
		if (id.TryDeserializeId<long>(out var longId))
			return $"wiki_page/{longId}";
		if (id.TryDeserializeId<int>(out var intId))
			return $"wiki_page/{intId}";
		throw new InvalidOperationException($"Unsupported SurrealDB wiki_page record ID type for table '{id.Table}'.");
	}

	private static string NormalizeWikiRevisionId(RecordId? id)
	{
		ArgumentNullException.ThrowIfNull(id);
		if (id.TryDeserializeId<string>(out var stringId))
			return $"wiki_revision/{stringId}";
		if (id.TryDeserializeId<long>(out var longId))
			return $"wiki_revision/{longId}";
		if (id.TryDeserializeId<int>(out var intId))
			return $"wiki_revision/{intId}";
		throw new InvalidOperationException($"Unsupported SurrealDB wiki_revision record ID type for table '{id.Table}'.");
	}

	private static WikiPage MapToWikiPage(WikiPageDbRecord r)
	{
		DateTimeOffset createdAt = default, updatedAt = default;
		DateTimeOffset.TryParse(r.createdAt, out createdAt);
		DateTimeOffset.TryParse(r.updatedAt, out updatedAt);
		return new WikiPage(
				Id: NormalizeWikiPageId(r.Id),
				Slug: r.slug,
				Title: r.title,
				Namespace: r.@namespace,
				MarkdownSource: r.markdownSource,
				RenderedHtml: r.renderedHtml,
				PlainText: r.plainText,
				AuthorDbref: r.authorDbref,
				LastEditorDbref: r.lastEditorDbref,
				CreatedAt: createdAt,
				UpdatedAt: updatedAt,
				IsProtected: r.isProtected,
				RevisionNumber: r.revisionNumber
		)
		{
			Category = string.IsNullOrEmpty(r.category) ? null : r.category,
			Tags = r.tags ?? [],
			Published = r.published ?? true,
			// Read straight through: a record the backfill has not reached yields empty, which means
			// "not yet stamped". Nothing substitutes the configured default here or anywhere on the read path.
			SourceLocale = r.sourceLocale ?? "",
		};
	}

	private static WikiRevision MapToWikiRevision(WikiRevisionDbRecord r)
	{
		DateTimeOffset timestamp = default;
		DateTimeOffset.TryParse(r.timestamp, out timestamp);
		return new WikiRevision(
				Id: NormalizeWikiRevisionId(r.Id),
				PageId: r.pageId,
				RevisionNumber: r.revisionNumber,
				MarkdownSource: r.markdownSource,
				EditorDbref: r.editorDbref,
				Timestamp: timestamp,
				EditSummary: string.IsNullOrEmpty(r.editSummary) ? null : r.editSummary
		)
		{
			Locale = r.locale ?? "",
		};
	}

	private static string Slugify(string title) =>
			WikiHelpers.Slugify(title);

	// ---- Translations -------------------------------------------------------

	public async Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
	{
		var parameters = new Dictionary<string, object?> { ["pageId"] = pageId };
		var response = await ExecuteAsync(
				$"SELECT {WikiTranslationFields} FROM wiki_translation WHERE pageId = $pageId ORDER BY locale ASC",
				parameters);
		var results = response.GetValue<List<WikiTranslationDbRecord>>(0);
		return (results?
				.Select(MapToWikiTranslation)
				.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
				.ToList() ?? [])
			.AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiTranslation>> GetAllTranslationsAsync(int skip = 0, int take = 50)
	{
		var parameters = new Dictionary<string, object?> { ["skip"] = skip, ["take"] = take };
		var response = await ExecuteAsync(
				$"SELECT {WikiTranslationFields} FROM wiki_translation ORDER BY pageId ASC, locale ASC LIMIT $take START $skip",
				parameters);
		var results = response.GetValue<List<WikiTranslationDbRecord>>(0);
		return (results?.Select(MapToWikiTranslation).ToList() ?? []).AsReadOnly();
	}

	public async Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		var parameters = new Dictionary<string, object?> { ["pageId"] = pageId, ["locale"] = normalized };
		var response = await ExecuteAsync(
				$"SELECT {WikiTranslationFields} FROM wiki_translation WHERE pageId = $pageId AND locale = $locale",
				parameters);
		var results = response.GetValue<List<WikiTranslationDbRecord>>(0);
		if (results is null or { Count: 0 }) return new NotFound();
		return MapToWikiTranslation(results[0]);
	}

	public async Task<OneOf<WikiTranslation, WikiWriteConflict, Error<string>>> UpsertTranslationAsync(
			string pageId, string locale, string title, string markdown,
			string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber)
	{
		var normalizedLocale = WikiHelpers.NormalizeLocale(locale);
		if (normalizedLocale.IsT1) return normalizedLocale.AsT1;

		var normalized = normalizedLocale.AsT0;

		var pageLookup = await GetByIdAsync(pageId);
		if (pageLookup.IsT1)
			return new Error<string>($"No wiki page with id '{pageId}'.");

		var page = pageLookup.AsT0;
		if (page.SourceLocale.Length > 0
				&& string.Equals(page.SourceLocale, normalized, StringComparison.OrdinalIgnoreCase))
			return new Error<string>(
					$"'{normalized}' is the page's source locale; edit the page itself rather than adding a translation.");

		var now = DateTimeOffset.UtcNow;
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = pageId,
			["locale"] = normalized,
			["title"] = title,
			["markdown"] = markdown,
			["html"] = html,
			["plain"] = plain,
			["editorDbref"] = editorDbref,
			["now"] = now.ToString("O"),
			["published"] = published
		};

		// The row this call itself wrote. Building the result and the revision entry from the write's
		// own return is what keeps the revision log honest: a read-back after a successful write can
		// pick up a *later* writer's content and file it under this caller's dbref and edit summary.
		// Arango reads its RETURN NEW and Memgraph reads its own returned node for the same reason.
		WikiTranslation? saved = null;

		try
		{
			if (expectedRevisionNumber is null)
			{
				// Create-only. A bare CREATE so the (pageId, locale) unique index arbitrates two writers who
				// both believe they are creating the translation, rather than a read-then-write race here.
				parameters["created"] = now.ToString("O");
				parameters["rev"] = 1;
				var createResponse = await ExecuteAsync("""
                    CREATE wiki_translation CONTENT {
                    	pageId: $pageId,
                    	locale: $locale,
                    	title: $title,
                    	markdownSource: $markdown,
                    	renderedHtml: $html,
                    	plainText: $plain,
                    	lastEditorDbref: $editorDbref,
                    	createdAt: $created,
                    	updatedAt: $now,
                    	published: $published,
                    	revisionNumber: $rev
                    }
                    """,
						parameters);

				// ExecuteAsync *logs* SurrealQL-level failures and returns the response; it does not throw
				// (SurrealDatabase.cs:168-174). The unique-index rejection this create path depends on is
				// exactly such a failure, so it has to be read off the response — the catch block below
				// never sees it. Without this check the method would fall through to the read-back at the
				// end, find the winner's row and report the loser's create as a success.
				if (createResponse.HasErrors)
				{
					var winner = await GetTranslationAsync(pageId, normalized);
					if (winner.IsT0) return WikiWriteConflict.AlreadyExists;

					return new Error<string>($"Could not create translation '{normalized}' for page '{pageId}'.");
				}

				var created = createResponse.GetValue<List<WikiTranslationDbRecord>>(0);
				if (created is { Count: > 0 }) saved = MapToWikiTranslation(created[0]);
			}
			else
			{
				// Compare-and-swap. The WHERE clause on revisionNumber is the condition and "no rows
				// returned" is the conflict signal — this provider has no ambient transaction spanning the
				// row update and the revision append, which is the fallback the spec permits.
				//
				// Never make this an unconditional UPDATE: two translators who both loaded revision 4 would
				// both write 5 and one would lose their prose with the index none the wiser.
				parameters["expected"] = expectedRevisionNumber.Value;
				parameters["rev"] = expectedRevisionNumber.Value + 1;
				var updateResponse = await ExecuteAsync(
						"UPDATE wiki_translation MERGE { title: $title, markdownSource: $markdown, " +
						"renderedHtml: $html, plainText: $plain, lastEditorDbref: $editorDbref, " +
						"updatedAt: $now, published: $published, revisionNumber: $rev } " +
						"WHERE pageId = $pageId AND locale = $locale AND revisionNumber = $expected " +
						"RETURN AFTER",
						parameters);

				// HasErrors covers a write conflict, which this provider also only logs; either way no row
				// was updated and the caller must reload rather than have its markdown re-applied.
				var updated = updateResponse.HasErrors
						? null
						: updateResponse.GetValue<List<WikiTranslationDbRecord>>(0);
				if (updated is null or { Count: 0 })
				{
					// Zero rows affected. Do NOT re-read and re-apply: that overwrites the winner with this
					// caller's stale markdown, which is the loss expectedRevisionNumber exists to prevent.
					var current = await GetTranslationAsync(pageId, normalized);
					return current.IsT0 ? WikiWriteConflict.StaleRevision : WikiWriteConflict.TranslationGone;
				}

				saved = MapToWikiTranslation(updated[0]);
			}
		}
		catch (Exception ex)
		{
			// Only .NET-level faults reach here — SurrealQL errors, including the unique-index rejection,
			// are handled off the response above because ExecuteAsync does not throw them.
			if (expectedRevisionNumber is null)
			{
				var existing = await GetTranslationAsync(pageId, normalized);
				if (existing.IsT0) return WikiWriteConflict.AlreadyExists;
			}

			return new Error<string>($"Could not write translation '{normalized}': {ex.Message}");
		}

		// Only if the driver returned no row at all — not an expected path, and the read-back it falls
		// to carries the misattribution risk described above, so it stays the exception rather than
		// the rule.
		if (saved is null)
		{
			var written = await GetTranslationAsync(pageId, normalized);
			if (written.IsT1)
				return new Error<string>($"Upsert of translation '{normalized}' returned no document.");

			saved = written.AsT0;
		}

		await SaveSurrealTranslationRevisionAsync(saved, editorDbref, editSummary, now);
		return saved;
	}

	public async Task<OneOf<OkNone, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		var lookup = await GetTranslationAsync(pageId, normalized);
		if (lookup.IsT1) return new NotFound();

		var revParams = new Dictionary<string, object?> { ["pageId"] = pageId, ["locale"] = normalized };

		// `(locale ?? '')` rather than a bare `locale = $locale`, and that is load-bearing rather than
		// defensive symmetry with the reads. `WHERE pageId = .. AND locale = ..` is a two-field *prefix* of
		// the three-field UNIQUE index wiki_revision_page_locale_rev (pageId, locale, revisionNumber), and
		// SurrealDB plans it against that index and matches nothing at all — verified directly: the same
		// filter as a SELECT returns 0 rows while `(locale ?? '') = $locale` and a bare `locale = $locale`
		// each return 1. It reports no error, so as a DELETE it silently deletes nothing. Wrapping the
		// column in an expression makes it index-ineligible and forces the scan that actually matches.
		await ExecuteAsync("DELETE wiki_revision WHERE pageId = $pageId AND (locale ?? '') = $locale", revParams);

		var key = NormalizeSurrealId(lookup.AsT0.Id, "wiki_translation");
		var delParams = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
		await ExecuteAsync("DELETE $id", delParams);

		return new OkNone();
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(
			string pageId, string locale, int skip, int take)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = pageId, ["locale"] = wanted, ["skip"] = skip, ["take"] = take
		};
		var response = await ExecuteAsync(
				$"SELECT {WikiRevisionFields} FROM wiki_revision " +
				"WHERE pageId = $pageId AND (locale ?? '') = $locale " +
				"ORDER BY revisionNumber DESC LIMIT $take START $skip",
				parameters);
		var results = response.GetValue<List<WikiRevisionDbRecord>>(0);
		return (results?.Select(MapToWikiRevision).ToList() ?? []).AsReadOnly();
	}

	public async Task<OneOf<WikiRevision, NotFound>> GetRevisionForLocaleAsync(
			string pageId, string locale, int revisionNumber)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = pageId, ["rev"] = revisionNumber, ["locale"] = wanted
		};
		// (locale ?? '') rather than a bare locale comparison: rows predating the empty-marker backfill
		// carry no locale field at all, and SurrealDB matches nothing rather than treating it as NONE = ''.
		var response = await ExecuteAsync(
				$"SELECT {WikiRevisionFields} FROM wiki_revision " +
				"WHERE pageId = $pageId AND revisionNumber = $rev AND (locale ?? '') = $locale",
				parameters);
		var results = response.GetValue<List<WikiRevisionDbRecord>>(0);
		if (results?.Count > 0)
			return MapToWikiRevision(results[0]);
		return new NotFound();
	}

	/// <summary>
	/// Appends a revision row for a translation, carrying <c>locale</c> so the per-locale stream and the
	/// (pageId, locale, revisionNumber) unique index both work.
	/// </summary>
	private async Task SaveSurrealTranslationRevisionAsync(
			WikiTranslation translation,
			string editorDbref,
			string? editSummary,
			DateTimeOffset timestamp)
	{
		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = translation.PageId,
			["locale"] = translation.Locale,
			["rev"] = translation.RevisionNumber,
			["markdown"] = translation.MarkdownSource,
			["editorDbref"] = editorDbref,
			["timestamp"] = timestamp.ToString("O"),
			["editSummary"] = editSummary
		};

		await ExecuteAsync("""
            CREATE wiki_revision CONTENT {
            	pageId: $pageId,
            	locale: $locale,
            	revisionNumber: $rev,
            	markdownSource: $markdown,
            	editorDbref: $editorDbref,
            	timestamp: $timestamp,
            	editSummary: $editSummary
            }
            """,
				parameters);
	}

	private static WikiTranslation MapToWikiTranslation(WikiTranslationDbRecord record) => new(
			Id: NormalizeWikiTranslationId(record.Id),
			PageId: record.pageId ?? "",
			Locale: record.locale ?? "",
			Title: record.title ?? "",
			MarkdownSource: record.markdownSource ?? "",
			RenderedHtml: record.renderedHtml ?? "",
			PlainText: record.plainText ?? "",
			LastEditorDbref: record.lastEditorDbref ?? "",
			CreatedAt: DateTimeOffset.TryParse(record.createdAt, out var created) ? created : DateTimeOffset.MinValue,
			UpdatedAt: DateTimeOffset.TryParse(record.updatedAt, out var updated) ? updated : DateTimeOffset.MinValue,
			Published: record.published ?? true,
			RevisionNumber: record.revisionNumber ?? 1);

	private static string NormalizeWikiTranslationId(RecordId? id)
	{
		ArgumentNullException.ThrowIfNull(id);
		if (id.TryDeserializeId<string>(out var stringId))
			return $"wiki_translation/{stringId}";
		if (id.TryDeserializeId<long>(out var longId))
			return $"wiki_translation/{longId}";
		if (id.TryDeserializeId<int>(out var intId))
			return $"wiki_translation/{intId}";
		throw new InvalidOperationException($"Unsupported SurrealDB wiki_translation record ID type for table '{id.Table}'.");
	}

	#endregion
}
