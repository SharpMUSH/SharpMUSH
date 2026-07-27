using Neo4j.Driver;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase : IWikiService
{
	#region Wiki

	private static readonly WikiMarkdigPipeline _wikiRenderer = new();

	public async Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var cat = WikiHelpers.NormalizeCategory(category);
		var normalizedSlug = Slugify(slug);
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (p:WikiPage {namespace: $ns, category: $cat, slug: $slug}) RETURN p",
			new { ns = nsStr, cat, slug = normalizedSlug });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiPage(records[0]["p"].As<INode>());
	}

	public async Task<OneOf<WikiPage, NotFound>> GetByIdAsync(string id)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (p:WikiPage {pageId: $id}) RETURN p",
			new { id });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiPage(records[0]["p"].As<INode>());
	}

	public async Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (p:WikiPage) RETURN p ORDER BY p.updatedAt DESC LIMIT $count",
			new { count });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiPage(r["p"].As<INode>())).ToList().AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (p:WikiPage {namespace: $ns}) RETURN p ORDER BY p.slug ASC SKIP $skip LIMIT $take",
			new { ns = nsStr, skip, take });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiPage(r["p"].As<INode>())).ToList().AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null)
	{
		await using var session = driver.AsyncSession();
		IResultCursor result;
		if (ns is not null)
		{
			result = await session.RunAsync(
				"MATCH (p:WikiPage {namespace: $ns}) RETURN p ORDER BY p.namespace ASC, p.slug ASC SKIP $skip LIMIT $take",
				new { ns = ns.Value.ToString().ToLowerInvariant(), skip, take });
		}
		else
		{
			result = await session.RunAsync(
				"MATCH (p:WikiPage) RETURN p ORDER BY p.namespace ASC, p.slug ASC SKIP $skip LIMIT $take",
				new { skip, take });
		}

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiPage(r["p"].As<INode>())).ToList().AsReadOnly();
	}

	public async Task<int> CountPagesAsync(WikiNamespace? ns = null)
	{
		await using var session = driver.AsyncSession();
		var result = ns is not null
			? await session.RunAsync(
				"MATCH (p:WikiPage {namespace: $ns}) RETURN count(p) AS cnt",
				new { ns = ns.Value.ToString().ToLowerInvariant() })
			: await session.RunAsync("MATCH (p:WikiPage) RETURN count(p) AS cnt");

		var records = await result.ToListAsync();
		return records.Count > 0 ? records[0]["cnt"].As<int>() : 0;
	}

	public async Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (p:WikiPage {category: $cat}) RETURN p ORDER BY p.title ASC SKIP $skip LIMIT $take",
			new { cat = WikiHelpers.NormalizeCategory(category) ?? string.Empty, skip, take });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiPage(r["p"].As<INode>())).ToList().AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiPage>> GetByTagAsync(string tag, int skip = 0, int take = 50)
	{
		await using var session = driver.AsyncSession();
		// coalesce guards nodes created before the tags property existed.
		var result = await session.RunAsync(
			"MATCH (p:WikiPage) WHERE $tag IN coalesce(p.tags, []) RETURN p ORDER BY p.title ASC SKIP $skip LIMIT $take",
			new { tag = tag.Trim().ToLowerInvariant(), skip, take });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiPage(r["p"].As<INode>())).ToList().AsReadOnly();
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
		var pageId = Guid.NewGuid().ToString("N");
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		// W-7: ExecuteWriteAsync wraps both queries in a managed transaction that
		// the driver automatically retries on transient Memgraph conflicts, making
		// it safe to use under parallel test load without explicit BeginTransaction.
		await using var session = driver.AsyncSession();
		return await session.ExecuteWriteAsync(async tx =>
		{
			var result = await tx.RunAsync("""
				CREATE (p:WikiPage {
					pageId: $pageId,
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
				}) RETURN p
				""",
				new
				{
					pageId,
					slug,
					title,
					ns = nsStr,
					cat,
					markdown,
					html,
					plain,
					authorDbref,
					now = now.ToString("O"),
					sourceLocale = stampedLocale
				});

			var records = await result.ToListAsync();
			var page = NodeToWikiPage(records[0]["p"].As<INode>());

			await SaveMemgraphRevisionAsync(tx, page, authorDbref, null, now);
			return page;
		});
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
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		await using var session = driver.AsyncSession();
		return await session.ExecuteWriteAsync(async tx =>
		{
			var result = await tx.RunAsync("""
				MATCH (p:WikiPage {pageId: $id})
				SET p.markdownSource = $markdown,
				    p.renderedHtml = $html,
				    p.plainText = $plain,
				    p.lastEditorDbref = $editorDbref,
				    p.updatedAt = $now,
				    p.revisionNumber = $rev
				RETURN p
				""",
				new
				{
					id,
					markdown,
					html,
					plain,
					editorDbref,
					now = now.ToString("O"),
					rev = newRevision
				});

			var records = await result.ToListAsync();
			var updated = NodeToWikiPage(records[0]["p"].As<INode>());

			await SaveMemgraphRevisionAsync(tx, updated, editorDbref, editSummary, now);
			return updated;
		});
	}

	public async Task<OneOf<None, NotFound>> DeleteAsync(string id, string editorDbref)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		await using var session = driver.AsyncSession();
		// W-7: ExecuteWriteAsync retries on transient Memgraph conflicts; safe under parallel load.
		await session.ExecuteWriteAsync(async tx =>
		{
			// The WikiRevision sweep already covers translation revisions, so only the translation
			// nodes themselves need their own statement.
			await tx.RunAsync(
				"MATCH (r:WikiRevision {pageId: $id}) DELETE r",
				new { id });

			await tx.RunAsync(
				"MATCH (t:WikiTranslation {pageId: $id}) DELETE t",
				new { id });

			await tx.RunAsync(
				"MATCH (p:WikiPage {pageId: $id}) DELETE p",
				new { id });
		});

		return new None();
	}

	public async Task<OneOf<None, NotFound>> SetProtectionAsync(string id, bool isProtected)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		await using var session = driver.AsyncSession();
		await session.RunAsync(
			"MATCH (p:WikiPage {pageId: $id}) SET p.isProtected = $isProtected",
			new { id, isProtected });

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

		// Category is part of page identity; reject a recategorization that would collide.
		if (!string.Equals(normalizedCategory, existingPage.Category, StringComparison.OrdinalIgnoreCase)
			&& Enum.TryParse<WikiNamespace>(existingPage.Namespace, ignoreCase: true, out var nsEnum)
			&& (await GetBySlugAsync(existingPage.Slug, normalizedCategory, nsEnum)).IsT0)
		{
			return new NotFound();
		}

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (p:WikiPage {pageId: $id}) SET p.category = $cat, p.tags = $tags, p.published = $pub RETURN p",
			new { id, cat = normalizedCategory, tags = normalizedTags.ToList(), pub = published });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiPage(records[0]["p"].As<INode>());
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync("""
			MATCH (r:WikiRevision {pageId: $pageId})
			WHERE coalesce(r.locale, '') = ''
			RETURN r ORDER BY r.revisionNumber DESC SKIP $skip LIMIT $take
			""",
			new { pageId, skip, take });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiRevision(r["r"].As<INode>())).ToList().AsReadOnly();
	}

	public async Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (r:WikiRevision {pageId: $pageId, revisionNumber: $rev}) WHERE coalesce(r.locale, '') = '' RETURN r",
			new { pageId, rev = revisionNumber });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiRevision(records[0]["r"].As<INode>());
	}

	private static async Task SaveMemgraphRevisionAsync(
		IAsyncQueryRunner runner,
		WikiPage page,
		string editorDbref,
		string? editSummary,
		DateTimeOffset timestamp)
	{
		// locale is written explicitly as the empty source-stream marker, never left absent. Memgraph's
		// uniqueness constraints do not apply to nodes missing any constrained property, so a locale-less
		// revision node would be exempt from (pageId, locale, revisionNumber) altogether — verified by
		// probe: two nodes with the same (pageId, revisionNumber) and no locale are both accepted, while
		// the same pair with locale: '' is rejected on the second. Leaving it absent would mean the source
		// stream, the only stream that existed before this feature, is the one stream with no constraint.
		await runner.RunAsync("""
			CREATE (r:WikiRevision {
				revisionId: $revisionId,
				pageId: $pageId,
				locale: '',
				revisionNumber: $revisionNumber,
				markdownSource: $markdownSource,
				editorDbref: $editorDbref,
				timestamp: $timestamp,
				editSummary: $editSummary
			})
			""",
			new
			{
				revisionId = $"{page.Id}:{page.RevisionNumber}",
				pageId = page.Id,
				revisionNumber = page.RevisionNumber,
				markdownSource = page.MarkdownSource,
				editorDbref,
				timestamp = timestamp.ToString("O"),
				editSummary = editSummary ?? ""
			});
	}

	private static WikiPage NodeToWikiPage(INode node)
	{
		DateTimeOffset createdAt = default, updatedAt = default;
		DateTimeOffset.TryParse(node["createdAt"].As<string>(), out createdAt);
		DateTimeOffset.TryParse(node["updatedAt"].As<string>(), out updatedAt);

		// Metadata props are optional — nodes created before they existed get defaults.
		var category = node.Properties.TryGetValue("category", out var catVal)
			? catVal?.As<string?>()
			: null;
		var tags = node.Properties.TryGetValue("tags", out var tagsVal) && tagsVal is not null
			? tagsVal.As<List<string>>()
			: [];
		var published = !node.Properties.TryGetValue("published", out var pubVal)
			|| pubVal is null || pubVal.As<bool>();

		return new WikiPage(
			Id: node["pageId"].As<string>(),
			Slug: node["slug"].As<string>(),
			Title: node["title"].As<string>(),
			Namespace: node["namespace"].As<string>(),
			MarkdownSource: node["markdownSource"].As<string>(),
			RenderedHtml: node["renderedHtml"].As<string>(),
			PlainText: node["plainText"].As<string>(),
			AuthorDbref: node["authorDbref"].As<string>(),
			LastEditorDbref: node["lastEditorDbref"].As<string>(),
			CreatedAt: createdAt,
			UpdatedAt: updatedAt,
			IsProtected: node["isProtected"].As<bool>(),
			RevisionNumber: node["revisionNumber"].As<int>()
		)
		{
			Category = string.IsNullOrEmpty(category) ? null : category,
			Tags = tags,
			Published = published,
			// Read straight through: a node the backfill has not reached yields empty, which means
			// "not yet stamped". Nothing substitutes the configured default here or anywhere on the read path.
			SourceLocale = node.Properties.TryGetValue("sourceLocale", out var srcLoc) ? srcLoc?.ToString() ?? "" : "",
		};
	}

	private static WikiRevision NodeToWikiRevision(INode node)
	{
		DateTimeOffset timestamp = default;
		DateTimeOffset.TryParse(node["timestamp"].As<string>(), out timestamp);
		var editSummary = node["editSummary"].As<string>();

		return new WikiRevision(
			Id: node["revisionId"].As<string>(),
			PageId: node["pageId"].As<string>(),
			RevisionNumber: node["revisionNumber"].As<int>(),
			MarkdownSource: node["markdownSource"].As<string>(),
			EditorDbref: node["editorDbref"].As<string>(),
			Timestamp: timestamp,
			EditSummary: string.IsNullOrEmpty(editSummary) ? null : editSummary
		)
		{
			Locale = node.Properties.TryGetValue("locale", out var revLoc) ? revLoc?.ToString() ?? "" : "",
		};
	}

	private static string Slugify(string title) =>
		WikiHelpers.Slugify(title);

	// ---- Translations -------------------------------------------------------

	public async Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (t:WikiTranslation {pageId: $pageId}) RETURN t ORDER BY t.locale ASC",
			new { pageId });

		var records = await result.ToListAsync();
		return records
			.Select(r => NodeToWikiTranslation(r["t"].As<INode>()))
			.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
			.ToList()
			.AsReadOnly();
	}

	public async Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (t:WikiTranslation {pageId: $pageId, locale: $locale}) RETURN t",
			new { pageId, locale = normalized });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiTranslation(records[0]["t"].As<INode>());
	}

	public async Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
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

		try
		{
			await using var session = driver.AsyncSession();
			// ExecuteWriteAsync gives one managed transaction, so here the row write and the revision append
			// really are atomic — the spec's preferred shape rather than the conditional-update fallback.
			return await session.ExecuteWriteAsync<OneOf<WikiTranslation, Error<string>>>(async tx =>
			{
				// No MERGE. MERGE + ON MATCH SET revisionNumber = revisionNumber + 1 is an unconditional
				// bump: two translators who both loaded revision 4 both produce a 5 and one loses their
				// prose. The compare-and-swap has to be expressed as a MATCH on the expected value.
				var cypher = expectedRevisionNumber is null
					? """
						CREATE (t:WikiTranslation {
							translationId: $translationId, pageId: $pageId, locale: $locale,
							title: $title, markdownSource: $markdown, renderedHtml: $html, plainText: $plain,
							lastEditorDbref: $editorDbref, createdAt: $now, updatedAt: $now,
							published: $published, revisionNumber: 1
						})
						RETURN t
						"""
					: """
						MATCH (t:WikiTranslation {pageId: $pageId, locale: $locale})
						WHERE t.revisionNumber = $expected
						SET t.title = $title,
						    t.markdownSource = $markdown,
						    t.renderedHtml = $html,
						    t.plainText = $plain,
						    t.lastEditorDbref = $editorDbref,
						    t.updatedAt = $now,
						    t.published = $published,
						    t.revisionNumber = t.revisionNumber + 1
						RETURN t
						""";

				var result = await tx.RunAsync(cypher,
					new
					{
						pageId,
						locale = normalized,
						translationId = Guid.NewGuid().ToString("N"),
						title,
						markdown,
						html,
						plain,
						editorDbref,
						now = now.ToString("O"),
						published,
						expected = expectedRevisionNumber ?? 0
					});

				var records = await result.ToListAsync();
				if (records.Count == 0)
				{
					// Zero rows matched: somebody else already bumped it, or it does not exist. Not retried.
					return new Error<string>(
						$"The '{normalized}' translation changed while you were editing, or does not exist "
						+ $"(expected revision {expectedRevisionNumber}). Reload and re-apply your changes.");
				}

				var translation = NodeToWikiTranslation(records[0]["t"].As<INode>());

				await SaveMemgraphTranslationRevisionAsync(tx, translation, editorDbref, editSummary, now);
				return translation;
			});
		}
		catch (Exception ex)
		{
			// On the create path a lost race against the (pageId, locale) uniqueness constraint lands here,
			// and there is nothing of this caller's to preserve. On the update path a conflict has already
			// returned above, so reaching this handler with a non-null expected revision is a real fault —
			// report it, never re-read and re-apply, which would overwrite the winner with stale markdown.
			if (expectedRevisionNumber is null)
			{
				var existing = await GetTranslationAsync(pageId, normalized);
				if (existing.IsT0)
					return new Error<string>(
						$"A '{normalized}' translation already exists for page '{pageId}'. "
						+ "Pass its current revision number to update it.");
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

		await using var session = driver.AsyncSession();
		await session.ExecuteWriteAsync(async tx =>
		{
			await tx.RunAsync(
				"MATCH (r:WikiRevision {pageId: $pageId, locale: $locale}) DELETE r",
				new { pageId, locale = normalized });

			await tx.RunAsync(
				"MATCH (t:WikiTranslation {pageId: $pageId, locale: $locale}) DELETE t",
				new { pageId, locale = normalized });
		});

		return new None();
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(
		string pageId, string locale, int skip, int take)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync("""
			MATCH (r:WikiRevision {pageId: $pageId})
			WHERE coalesce(r.locale, '') = $locale
			RETURN r ORDER BY r.revisionNumber DESC SKIP $skip LIMIT $take
			""",
			new { pageId, locale = wanted, skip, take });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiRevision(r["r"].As<INode>())).ToList().AsReadOnly();
	}

	public async Task<OneOf<WikiRevision, NotFound>> GetRevisionForLocaleAsync(
		string pageId, string locale, int revisionNumber)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync("""
			MATCH (r:WikiRevision {pageId: $pageId, revisionNumber: $rev})
			WHERE coalesce(r.locale, '') = $locale
			RETURN r
			""",
			new { pageId, rev = revisionNumber, locale = wanted });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiRevision(records[0]["r"].As<INode>());
	}

	/// <summary>
	/// Appends a revision node for a translation. <c>revisionId</c> carries the locale so it cannot
	/// collide with the source page's <c>{pageId}:{revisionNumber}</c> keys.
	/// </summary>
	private static async Task SaveMemgraphTranslationRevisionAsync(
		IAsyncQueryRunner runner,
		WikiTranslation translation,
		string editorDbref,
		string? editSummary,
		DateTimeOffset timestamp)
	{
		await runner.RunAsync("""
			CREATE (r:WikiRevision {
				revisionId: $revisionId,
				pageId: $pageId,
				locale: $locale,
				revisionNumber: $revisionNumber,
				markdownSource: $markdownSource,
				editorDbref: $editorDbref,
				timestamp: $timestamp,
				editSummary: $editSummary
			})
			""",
			new
			{
				revisionId = $"{translation.PageId}:{translation.Locale}:{translation.RevisionNumber}",
				pageId = translation.PageId,
				locale = translation.Locale,
				revisionNumber = translation.RevisionNumber,
				markdownSource = translation.MarkdownSource,
				editorDbref,
				timestamp = timestamp.ToString("O"),
				editSummary = editSummary ?? ""
			});
	}

	private static WikiTranslation NodeToWikiTranslation(INode node) => new(
		Id: node.Properties.TryGetValue("translationId", out var id) ? id?.ToString() ?? "" : "",
		PageId: node.Properties.TryGetValue("pageId", out var pageId) ? pageId?.ToString() ?? "" : "",
		Locale: node.Properties.TryGetValue("locale", out var locale) ? locale?.ToString() ?? "" : "",
		Title: node.Properties.TryGetValue("title", out var title) ? title?.ToString() ?? "" : "",
		MarkdownSource: node.Properties.TryGetValue("markdownSource", out var md) ? md?.ToString() ?? "" : "",
		RenderedHtml: node.Properties.TryGetValue("renderedHtml", out var html) ? html?.ToString() ?? "" : "",
		PlainText: node.Properties.TryGetValue("plainText", out var plain) ? plain?.ToString() ?? "" : "",
		LastEditorDbref: node.Properties.TryGetValue("lastEditorDbref", out var editor) ? editor?.ToString() ?? "" : "",
		CreatedAt: node.Properties.TryGetValue("createdAt", out var created)
			&& DateTimeOffset.TryParse(created?.ToString(), out var createdAt) ? createdAt : DateTimeOffset.MinValue,
		UpdatedAt: node.Properties.TryGetValue("updatedAt", out var updated)
			&& DateTimeOffset.TryParse(updated?.ToString(), out var updatedAt) ? updatedAt : DateTimeOffset.MinValue,
		Published: !node.Properties.TryGetValue("published", out var published) || published is not false,
		RevisionNumber: node.Properties.TryGetValue("revisionNumber", out var rev)
			&& int.TryParse(rev?.ToString(), out var revNum) ? revNum : 1);

	#endregion
}
