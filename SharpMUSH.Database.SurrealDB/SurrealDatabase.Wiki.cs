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
}

public partial class SurrealDatabase : IWikiService
{
    #region Wiki

    private static readonly WikiMarkdigPipeline _wikiRenderer = new();

    private const string WikiPageFields =
        "id, slug, title, namespace, markdownSource, renderedHtml, plainText, " +
        "authorDbref, lastEditorDbref, createdAt, updatedAt, isProtected, revisionNumber, " +
        "category, tags, published";

    private const string WikiRevisionFields =
        "id, pageId, revisionNumber, markdownSource, editorDbref, timestamp, editSummary";

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main)
    {
        var nsStr = ns.ToString().ToLowerInvariant();
        var cat = WikiHelpers.NormalizeCategory(category);
        var parameters = new Dictionary<string, object?> { ["ns"] = nsStr, ["cat"] = cat, ["slug"] = slug };
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

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<OneOf<WikiPage, Error<string>>> CreateAsync(
        string title,
        string markdown,
        string authorDbref,
        WikiNamespace ns = WikiNamespace.Main,
        string? category = null)
    {
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
            ["now"] = now.ToString("O")
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
            	published: true
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

        // Delete revisions
        await ExecuteAsync("DELETE wiki_revision WHERE pageId = $id", parameters);

        var key = NormalizeSurrealId(id, "wiki_page");
        var delParams = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
        await ExecuteAsync("DELETE $id", delParams);

        return new OkNone();
    }

    // C-7: Rename parameter to match interface contract (bool isProtected).
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

    // ── Revisions ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["pageId"] = pageId, ["skip"] = skip, ["take"] = take
        };
        var response = await ExecuteAsync(
            $"SELECT {WikiRevisionFields} FROM wiki_revision WHERE pageId = $pageId " +
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
            $"SELECT {WikiRevisionFields} FROM wiki_revision WHERE pageId = $pageId AND revisionNumber = $rev",
            parameters);
        var results = response.GetValue<List<WikiRevisionDbRecord>>(0);
        if (results?.Count > 0)
            return MapToWikiRevision(results[0]);
        return new NotFound();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task SaveSurrealRevisionAsync(
        WikiPage page,
        string editorDbref,
        string? editSummary,
        DateTimeOffset timestamp)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["pageId"] = page.Id,
            ["rev"] = page.RevisionNumber,
            ["markdown"] = page.MarkdownSource,
            ["editorDbref"] = editorDbref,
            ["timestamp"] = timestamp.ToString("O"),
            ["editSummary"] = editSummary
        };

        await ExecuteAsync("""
            CREATE wiki_revision CONTENT {
            	pageId: $pageId,
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
        );
    }

    private static string Slugify(string title) =>
        WikiHelpers.Slugify(title);

    #endregion
}
