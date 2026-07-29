using Core.Arango;
using Core.Arango.Protocol;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Net;
using System.Text.Json;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase : IWikiService
{
	#region Wiki

	private static readonly WikiMarkdigPipeline _wikiRenderer = new();

	public async Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var cat = WikiHelpers.NormalizeCategory(category);
		var normalizedSlug = Slugify(slug);
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR p IN @@c FILTER p.Namespace == @ns AND p.Category == @cat AND p.Slug == @slug RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "ns", nsStr },
				{ "cat", cat },
				{ "slug", normalizedSlug }
			});

		return result.FirstOrDefault() is { ValueKind: not JsonValueKind.Undefined } elem
			? OneOf<WikiPage, NotFound>.FromT0(WikiPageFromJson(elem))
			: new NotFound();
	}

	public async Task<OneOf<WikiPage, NotFound>> GetByIdAsync(string id)
	{
		var key = ExtractKey(id);
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR p IN @@c FILTER p._key == @key RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "key", key }
			});

		return result.FirstOrDefault() is { ValueKind: not JsonValueKind.Undefined } elem
			? OneOf<WikiPage, NotFound>.FromT0(WikiPageFromJson(elem))
			: new NotFound();
	}

	public async Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR p IN @@c SORT p.UpdatedAt DESC LIMIT @count RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "count", count }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiPageFromJson)
			.ToList()
			.AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR p IN @@c FILTER p.Namespace == @ns SORT p.Slug ASC LIMIT @skip, @take RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "ns", nsStr },
				{ "skip", skip },
				{ "take", take }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiPageFromJson)
			.ToList()
			.AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null)
	{
		var bindVars = new Dictionary<string, object>
		{
			{ "@c", DatabaseConstants.WikiPages },
			{ "skip", skip },
			{ "take", take }
		};
		var filter = string.Empty;
		if (ns is not null)
		{
			filter = "FILTER p.Namespace == @ns ";
			bindVars["ns"] = ns.Value.ToString().ToLowerInvariant();
		}

		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			$"FOR p IN @@c {filter}SORT p.Namespace ASC, p.Slug ASC LIMIT @skip, @take RETURN p",
			bindVars: bindVars);

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiPageFromJson)
			.ToList()
			.AsReadOnly();
	}

	public async Task<int> CountPagesAsync(WikiNamespace? ns, bool includeDrafts)
	{
		var bindVars = new Dictionary<string, object> { { "@c", DatabaseConstants.WikiPages } };
		var conditions = new List<string>();
		if (ns is not null)
		{
			conditions.Add("p.Namespace == @ns");
			bindVars["ns"] = ns.Value.ToString().ToLowerInvariant();
		}

		// `!= false` rather than `== true`, so a row written before the metadata feature — which has no
		// Published attribute at all — counts as published, exactly as WikiPageFromJson reads it back.
		if (!includeDrafts)
			conditions.Add("p.Published != false");

		var filter = conditions.Count > 0 ? $"FILTER {string.Join(" AND ", conditions)} " : string.Empty;

		var result = await arangoDb.Query.ExecuteAsync<int>(handle,
			$"FOR p IN @@c {filter}COLLECT WITH COUNT INTO cnt RETURN cnt",
			bindVars: bindVars);

		return result.FirstOrDefault();
	}

	public async Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR p IN @@c FILTER p.Category == @cat SORT p.Title ASC LIMIT @skip, @take RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "cat", WikiHelpers.NormalizeCategory(category) ?? string.Empty },
				{ "skip", skip },
				{ "take", take }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiPageFromJson)
			.ToList()
			.AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetByTagAsync(string tag, int skip = 0, int take = 50)
	{
		// NOT_NULL guards documents created before the Tags field existed.
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR p IN @@c FILTER @tag IN NOT_NULL(p.Tags, []) SORT p.Title ASC LIMIT @skip, @take RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "tag", tag.Trim().ToLowerInvariant() },
				{ "skip", skip },
				{ "take", take }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiPageFromJson)
			.ToList()
			.AsReadOnly();
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

		var doc = new
		{
			Slug = slug,
			Title = title,
			Namespace = nsStr,
			MarkdownSource = markdown,
			RenderedHtml = html,
			PlainText = plain,
			AuthorDbref = authorDbref,
			LastEditorDbref = authorDbref,
			CreatedAt = now,
			UpdatedAt = now,
			IsProtected = false,
			RevisionNumber = 1,
			Category = cat,
			Tags = Array.Empty<string>(),
			Published = true,
			SourceLocale = stampedLocale
		};

		var created = await arangoDb.Document.CreateAsync<object, JsonElement>(
			handle, DatabaseConstants.WikiPages, doc, returnNew: true);

		var page = WikiPageFromJson(created.New);

		await SaveWikiRevisionAsync(page, authorDbref, null);

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
		var key = ExtractKey(id);
		var now = DateTimeOffset.UtcNow;
		var newRevision = existing.RevisionNumber + 1;
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.WikiPages,
			new
			{
				_key = key,
				MarkdownSource = markdown,
				RenderedHtml = html,
				PlainText = plain,
				LastEditorDbref = editorDbref,
				UpdatedAt = now,
				RevisionNumber = newRevision
			},
			mergeObjects: true);

		var updated = existing with
		{
			MarkdownSource = markdown,
			RenderedHtml = html,
			PlainText = plain,
			LastEditorDbref = editorDbref,
			UpdatedAt = now,
			RevisionNumber = newRevision
		};

		await SaveWikiRevisionAsync(updated, editorDbref, editSummary);

		return updated;
	}

	public async Task<OneOf<None, NotFound>> DeleteAsync(string id, string editorDbref)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		var key = ExtractKey(id);

		// The revision sweep below already removes every row for the page, translation revisions
		// included, so only the translation documents themselves need their own sweep.
		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR t IN @@c FILTER t.PageId == @pageId REMOVE t IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiTranslations },
				{ "pageId", id }
			});

		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR r IN @@c FILTER r.PageId == @pageId REMOVE r IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", id }
			});

		await arangoDb.Document.DeleteAsync<JsonElement>(handle, DatabaseConstants.WikiPages, key);

		return new None();
	}

	public async Task<OneOf<None, NotFound>> SetProtectionAsync(string id, bool isProtected)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		var key = ExtractKey(id);
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.WikiPages,
			new { _key = key, IsProtected = isProtected },
			mergeObjects: true);

		return new None();
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

		// Category is part of page identity; changing it re-keys the page. Reject a move that
		// would collide with an existing (namespace, category, slug).
		if (!string.Equals(normalizedCategory, existingPage.Category, StringComparison.OrdinalIgnoreCase)
			&& Enum.TryParse<WikiNamespace>(existingPage.Namespace, ignoreCase: true, out var nsEnum)
			&& (await GetBySlugAsync(existingPage.Slug, normalizedCategory, nsEnum)).IsT0)
		{
			return new NotFound();
		}

		var key = ExtractKey(id);
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.WikiPages,
			new
			{
				_key = key,
				Category = normalizedCategory,
				Tags = normalizedTags,
				Published = published
			},
			mergeObjects: true);

		return lookupResult.AsT0 with
		{
			Category = normalizedCategory,
			Tags = normalizedTags,
			Published = published
		};
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR r IN @@c FILTER r.PageId == @pageId AND (r.Locale == null OR r.Locale == \"\") SORT r.RevisionNumber DESC LIMIT @skip, @take RETURN r",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", pageId },
				{ "skip", skip },
				{ "take", take }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiRevisionFromJson)
			.ToList()
			.AsReadOnly();
	}

	public async Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR r IN @@c FILTER r.PageId == @pageId AND r.RevisionNumber == @rev AND (r.Locale == null OR r.Locale == \"\") RETURN r",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", pageId },
				{ "rev", revisionNumber }
			});

		return result.FirstOrDefault() is { ValueKind: not JsonValueKind.Undefined } elem
			? OneOf<WikiRevision, NotFound>.FromT0(WikiRevisionFromJson(elem))
			: new NotFound();
	}

	private async Task SaveWikiRevisionAsync(WikiPage page, string editorDbref, string? editSummary)
	{
		// Locale is written explicitly as the empty source-stream marker rather than left absent, matching
		// what the migration backfill stamps on pre-existing rows. Storing it keeps every row's shape
		// identical across the two writers and keeps the (PageId, Locale, RevisionNumber) unique index
		// covering the source stream on the same terms as translation streams.
		var doc = new
		{
			PageId = page.Id,
			Locale = string.Empty,
			RevisionNumber = page.RevisionNumber,
			MarkdownSource = page.MarkdownSource,
			EditorDbref = editorDbref,
			Timestamp = page.UpdatedAt,
			EditSummary = editSummary
		};

		await arangoDb.Document.CreateAsync(handle, DatabaseConstants.WikiRevisions, doc);
	}

	private static WikiPage WikiPageFromJson(JsonElement elem)
	{
		var id = elem.TryGetProperty("_id", out var idProp) ? idProp.GetString() ?? "" : "";
		var ns = elem.TryGetProperty("Namespace", out var nsProp)
			? nsProp.GetString() ?? "main"
			: "main";

		DateTimeOffset createdAt = default, updatedAt = default;
		if (elem.TryGetProperty("CreatedAt", out var caProp))
			DateTimeOffset.TryParse(caProp.GetString(), out createdAt);
		if (elem.TryGetProperty("UpdatedAt", out var uaProp))
			DateTimeOffset.TryParse(uaProp.GetString(), out updatedAt);

		// Metadata fields are optional — documents created before they existed get defaults.
		var category = elem.TryGetProperty("Category", out var catProp) && catProp.ValueKind == JsonValueKind.String
			? catProp.GetString()
			: null;
		var tags = elem.TryGetProperty("Tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array
			? tagsProp.EnumerateArray()
				.Where(t => t.ValueKind == JsonValueKind.String)
				.Select(t => t.GetString()!)
				.ToList()
			: (IReadOnlyList<string>)[];
		var published = !elem.TryGetProperty("Published", out var pubProp)
			|| pubProp.ValueKind != JsonValueKind.False;

		return new WikiPage(
			Id: id,
			Slug: elem.TryGetProperty("Slug", out var slugProp) ? slugProp.GetString() ?? "" : "",
			Title: elem.TryGetProperty("Title", out var titleProp) ? titleProp.GetString() ?? "" : "",
			Namespace: ns,
			MarkdownSource: elem.TryGetProperty("MarkdownSource", out var mdProp) ? mdProp.GetString() ?? "" : "",
			RenderedHtml: elem.TryGetProperty("RenderedHtml", out var htmlProp) ? htmlProp.GetString() ?? "" : "",
			PlainText: elem.TryGetProperty("PlainText", out var ptProp) ? ptProp.GetString() ?? "" : "",
			AuthorDbref: elem.TryGetProperty("AuthorDbref", out var authProp) ? authProp.GetString() ?? "" : "",
			LastEditorDbref: elem.TryGetProperty("LastEditorDbref", out var edProp) ? edProp.GetString() ?? "" : "",
			CreatedAt: createdAt,
			UpdatedAt: updatedAt,
			IsProtected: elem.TryGetProperty("IsProtected", out var protProp) && protProp.GetBoolean(),
			RevisionNumber: elem.TryGetProperty("RevisionNumber", out var revProp) ? revProp.GetInt32() : 1
		)
		{
			Category = category,
			Tags = tags,
			Published = published,
			// Read straight through: a document the backfill has not reached yields empty, which means
			// "not yet stamped". Nothing substitutes the configured default here or anywhere on the read path.
			SourceLocale = elem.TryGetProperty("SourceLocale", out var srcLoc) ? srcLoc.GetString() ?? "" : "",
		};
	}

	private static WikiRevision WikiRevisionFromJson(JsonElement elem)
	{
		var id = elem.TryGetProperty("_id", out var idProp) ? idProp.GetString() ?? "" : "";
		var editSummary = elem.TryGetProperty("EditSummary", out var esProp) && esProp.ValueKind != JsonValueKind.Null
			? esProp.GetString()
			: null;

		DateTimeOffset timestamp = default;
		if (elem.TryGetProperty("Timestamp", out var tsProp))
			DateTimeOffset.TryParse(tsProp.GetString(), out timestamp);

		return new WikiRevision(
			Id: id,
			PageId: elem.TryGetProperty("PageId", out var pidProp) ? pidProp.GetString() ?? "" : "",
			RevisionNumber: elem.TryGetProperty("RevisionNumber", out var revProp) ? revProp.GetInt32() : 0,
			MarkdownSource: elem.TryGetProperty("MarkdownSource", out var mdProp) ? mdProp.GetString() ?? "" : "",
			EditorDbref: elem.TryGetProperty("EditorDbref", out var edProp) ? edProp.GetString() ?? "" : "",
			Timestamp: timestamp,
			EditSummary: editSummary
		)
		{
			Locale = elem.TryGetProperty("Locale", out var revLoc) ? revLoc.GetString() ?? "" : "",
		};
	}

	private static string Slugify(string title) =>
		WikiHelpers.Slugify(title);

	// ---- Translations -------------------------------------------------------

	public async Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR t IN @@c FILTER t.PageId == @pageId SORT t.Locale ASC RETURN t",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiTranslations },
				{ "pageId", pageId }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiTranslationFromJson)
			.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
			.ToList()
			.AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiTranslation>> GetAllTranslationsAsync(int skip = 0, int take = 50)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR t IN @@c SORT t.PageId ASC, t.Locale ASC LIMIT @skip, @take RETURN t",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiTranslations },
				{ "skip", skip },
				{ "take", take }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiTranslationFromJson)
			.ToList()
			.AsReadOnly();
	}

	public async Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR t IN @@c FILTER t.PageId == @pageId AND t.Locale == @locale RETURN t",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiTranslations },
				{ "pageId", pageId },
				{ "locale", normalized }
			});

		return result.FirstOrDefault() is { ValueKind: not JsonValueKind.Undefined } elem
			? OneOf<WikiTranslation, NotFound>.FromT0(WikiTranslationFromJson(elem))
			: new NotFound();
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

		var bindVars = new Dictionary<string, object>
		{
			{ "@c", DatabaseConstants.WikiTranslations },
			{ "pageId", pageId },
			{ "locale", normalized },
			{ "title", title },
			{ "markdown", markdown },
			{ "html", html },
			{ "plain", plain },
			{ "editor", editorDbref },
			{ "now", now },
			{ "published", published }
		};

		try
		{
			if (expectedRevisionNumber is null)
			{
				// Create-only. A plain INSERT so the (PageId, Locale) unique index — not a read-then-write
				// race in C# — arbitrates two writers who both believe they are creating the translation.
				var inserted = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
					"""
					INSERT {
						PageId: @pageId, Locale: @locale, Title: @title,
						MarkdownSource: @markdown, RenderedHtml: @html, PlainText: @plain,
						LastEditorDbref: @editor, CreatedAt: @now, UpdatedAt: @now,
						Published: @published, RevisionNumber: 1
					}
					IN @@c
					RETURN NEW
					""",
					bindVars: bindVars);

				if (inserted.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } created)
					return new Error<string>($"Insert of translation '{normalized}' returned no document.");

				var newTranslation = WikiTranslationFromJson(created);
				await SaveWikiTranslationRevisionAsync(newTranslation, editorDbref, editSummary);
				return newTranslation;
			}

			// Compare-and-swap: the FILTER on RevisionNumber is the condition, and "no document returned"
			// is the conflict signal. Never fold this back into an UPSERT — an unconditional UPDATE lets two
			// translators who both loaded revision 4 both write 5, and one loses their prose silently.
			bindVars["expected"] = expectedRevisionNumber.Value;
			var updatedRows = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
				"""
				FOR t IN @@c
					FILTER t.PageId == @pageId AND t.Locale == @locale AND t.RevisionNumber == @expected
					UPDATE t WITH {
						Title: @title, MarkdownSource: @markdown, RenderedHtml: @html, PlainText: @plain,
						LastEditorDbref: @editor, UpdatedAt: @now, Published: @published,
						RevisionNumber: t.RevisionNumber + 1
					}
					IN @@c
					RETURN NEW
				""",
				bindVars: bindVars);

			if (updatedRows.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } row)
			{
				// Zero rows affected. Either the translation is gone or somebody else already bumped it.
				// Do NOT retry: re-reading and re-applying would overwrite the winner with stale markdown,
				// which is precisely the loss expectedRevisionNumber exists to prevent.
				var current = await GetTranslationAsync(pageId, normalized);
				return current.IsT0 ? WikiWriteConflict.StaleRevision : WikiWriteConflict.TranslationGone;
			}

			var translation = WikiTranslationFromJson(row);
			await SaveWikiTranslationRevisionAsync(translation, editorDbref, editSummary);
			return translation;
		}
		catch (Exception ex)
		{
			// A lost unique-index race on the create path surfaces as a driver conflict. Reading the winner
			// back is safe there because nothing of this caller's content was meant to land.
			if (expectedRevisionNumber is null)
			{
				var retry = await GetTranslationAsync(pageId, normalized);
				if (retry.IsT0) return WikiWriteConflict.AlreadyExists;
			}
			else if (ex is ArangoException { Code: HttpStatusCode.Conflict })
			{
				// A genuinely simultaneous compare-and-swap does not always reach the zero-rows branch: when
				// both writers reach the UPDATE, ArangoDB aborts the loser with a write-write conflict (HTTP
				// 409) instead. Nothing of this caller's landed, so it is the same lost write the zero-rows
				// branch reports — and it must be reported as one, or the loser of a real race is told its
				// request was malformed. Still never re-read and re-applied: that overwrites the winner.
				//
				// Reporting a transaction abort as a lost write is deliberately conservative, and the project
				// owner has accepted the false positive it admits: a writer whose expectedRevisionNumber was
				// perfectly valid and who merely lost a scheduler race is still told the row changed. The
				// remedy, if that ever costs anything, is a retry on ABORT ONLY — never on a stale revision,
				// which must always surface to the human. Widening it to both is the data loss.
				var current = await GetTranslationAsync(pageId, normalized);
				return current.IsT0 ? WikiWriteConflict.StaleRevision : WikiWriteConflict.TranslationGone;
			}

			return new Error<string>($"Could not write translation '{normalized}': {ex.Message}");
		}
	}

	public async Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		var lookup = await GetTranslationAsync(pageId, normalized);
		if (lookup.IsT1) return new NotFound();

		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR r IN @@c FILTER r.PageId == @pageId AND r.Locale == @locale REMOVE r IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", pageId },
				{ "locale", normalized }
			});

		await arangoDb.Document.DeleteAsync<JsonElement>(
			handle, DatabaseConstants.WikiTranslations, ExtractKey(lookup.AsT0.Id));

		return new None();
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(
		string pageId, string locale, int skip, int take)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"""
			FOR r IN @@c
				FILTER r.PageId == @pageId AND (r.Locale == @locale OR (@locale == "" AND r.Locale == null))
				SORT r.RevisionNumber DESC
				LIMIT @skip, @take
				RETURN r
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", pageId },
				{ "locale", wanted },
				{ "skip", skip },
				{ "take", take }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiRevisionFromJson)
			.ToList()
			.AsReadOnly();
	}

	public async Task<OneOf<WikiRevision, NotFound>> GetRevisionForLocaleAsync(
		string pageId, string locale, int revisionNumber)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"""
			FOR r IN @@c
				FILTER r.PageId == @pageId AND r.RevisionNumber == @rev
					AND (r.Locale == @locale OR (@locale == "" AND r.Locale == null))
				RETURN r
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", pageId },
				{ "rev", revisionNumber },
				{ "locale", wanted }
			});

		return result.FirstOrDefault() is { ValueKind: not JsonValueKind.Undefined } elem
			? OneOf<WikiRevision, NotFound>.FromT0(WikiRevisionFromJson(elem))
			: new NotFound();
	}

	/// <summary>
	/// Appends a revision snapshot for a translation. The document carries <c>Locale</c>, which is what
	/// splits history into a stream per (PageId, Locale).
	/// </summary>
	private async Task SaveWikiTranslationRevisionAsync(
		WikiTranslation translation, string editorDbref, string? editSummary)
	{
		var doc = new
		{
			PageId = translation.PageId,
			Locale = translation.Locale,
			RevisionNumber = translation.RevisionNumber,
			MarkdownSource = translation.MarkdownSource,
			EditorDbref = editorDbref,
			Timestamp = translation.UpdatedAt,
			EditSummary = editSummary
		};

		await arangoDb.Document.CreateAsync(handle, DatabaseConstants.WikiRevisions, doc);
	}

	private static WikiTranslation WikiTranslationFromJson(JsonElement elem) => new(
		Id: elem.TryGetProperty("_id", out var id) ? id.GetString() ?? "" : "",
		PageId: elem.TryGetProperty("PageId", out var pageId) ? pageId.GetString() ?? "" : "",
		Locale: elem.TryGetProperty("Locale", out var locale) ? locale.GetString() ?? "" : "",
		Title: elem.TryGetProperty("Title", out var title) ? title.GetString() ?? "" : "",
		MarkdownSource: elem.TryGetProperty("MarkdownSource", out var md) ? md.GetString() ?? "" : "",
		RenderedHtml: elem.TryGetProperty("RenderedHtml", out var html) ? html.GetString() ?? "" : "",
		PlainText: elem.TryGetProperty("PlainText", out var plain) ? plain.GetString() ?? "" : "",
		LastEditorDbref: elem.TryGetProperty("LastEditorDbref", out var editor) ? editor.GetString() ?? "" : "",
		CreatedAt: elem.TryGetProperty("CreatedAt", out var created) && created.ValueKind != JsonValueKind.Null
			? created.GetDateTimeOffset() : DateTimeOffset.MinValue,
		UpdatedAt: elem.TryGetProperty("UpdatedAt", out var updated) && updated.ValueKind != JsonValueKind.Null
			? updated.GetDateTimeOffset() : DateTimeOffset.MinValue,
		Published: !elem.TryGetProperty("Published", out var published)
			|| published.ValueKind != JsonValueKind.False,
		RevisionNumber: elem.TryGetProperty("RevisionNumber", out var rev) && rev.TryGetInt32(out var revNum)
			? revNum : 1);

	#endregion
}
