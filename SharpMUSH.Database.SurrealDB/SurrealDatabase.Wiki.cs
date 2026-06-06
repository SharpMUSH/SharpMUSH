using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;
using SurrealDb.Net.Models;
using OkNone = OneOf.Types.None;
using System.Text.Json.Serialization;

namespace SharpMUSH.Database.SurrealDB;

internal class WikiPageDbRecord : Record
{
	[JsonPropertyName("slug")] public string Slug { get; set; } = "";
	[JsonPropertyName("title")] public string Title { get; set; } = "";
	[JsonPropertyName("namespace")] public string Namespace { get; set; } = "main";
	[JsonPropertyName("markdown_source")] public string MarkdownSource { get; set; } = "";
	[JsonPropertyName("rendered_html")] public string RenderedHtml { get; set; } = "";
	[JsonPropertyName("plain_text")] public string PlainText { get; set; } = "";
	[JsonPropertyName("author_dbref")] public string AuthorDbref { get; set; } = "";
	[JsonPropertyName("last_editor_dbref")] public string LastEditorDbref { get; set; } = "";
	[JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
	[JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
	[JsonPropertyName("is_protected")] public bool IsProtected { get; set; }
	[JsonPropertyName("revision_number")] public int RevisionNumber { get; set; } = 1;
}

internal class WikiRevisionDbRecord : Record
{
	[JsonPropertyName("page_id")] public string PageId { get; set; } = "";
	[JsonPropertyName("revision_number")] public int RevisionNumber { get; set; }
	[JsonPropertyName("markdown_source")] public string MarkdownSource { get; set; } = "";
	[JsonPropertyName("editor_dbref")] public string EditorDbref { get; set; } = "";
	[JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
	[JsonPropertyName("edit_summary")] public string? EditSummary { get; set; }
}

public partial class SurrealDatabase : IWikiService
{
	#region Wiki

	private static readonly WikiMarkdigPipeline _wikiRenderer = new();

	private const string WikiPageFields =
		"id, slug, title, namespace, markdown_source, rendered_html, plain_text, " +
		"author_dbref, last_editor_dbref, created_at, updated_at, is_protected, revision_number";

	private const string WikiRevisionFields =
		"id, page_id, revision_number, markdown_source, editor_dbref, timestamp, edit_summary";

	// ── Read ──────────────────────────────────────────────────────────────────

	public async Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var parameters = new Dictionary<string, object?> { ["ns"] = nsStr, ["slug"] = slug };
		var response = await ExecuteAsync(
			$"SELECT {WikiPageFields} FROM wiki_page WHERE namespace = $ns AND slug = $slug",
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
			$"SELECT {WikiPageFields} FROM wiki_page ORDER BY updated_at DESC LIMIT $count",
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
			$"SELECT {WikiPageFields} FROM wiki_page WHERE namespace = $ns ORDER BY slug ASC LIMIT $skip, $take",
			parameters);
		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		return (results?.Select(MapToWikiPage).ToList() ?? []).AsReadOnly();
	}

	// ── Write ─────────────────────────────────────────────────────────────────

	public async Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var slug = Slugify(title);

		var existing = await GetBySlugAsync(slug, ns);
		if (existing.IsT0)
			return new Error<string>($"A wiki page with slug '{slug}' already exists in namespace '{nsStr}'.");

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
			["now"] = now.ToString("O")
		};

		var response = await ExecuteAsync("""
			CREATE wiki_page CONTENT {
				slug: $slug,
				title: $title,
				namespace: $ns,
				markdown_source: $markdown,
				rendered_html: $html,
				plain_text: $plain,
				author_dbref: $authorDbref,
				last_editor_dbref: $authorDbref,
				created_at: $now,
				updated_at: $now,
				is_protected: false,
				revision_number: 1
			}
			""",
			parameters);

		var created = response.GetValue<List<WikiPageDbRecord>>(0)!.First();
		var page = MapToWikiPage(created);

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
			$"UPDATE $id MERGE {{ markdown_source: $markdown, rendered_html: $html, plain_text: $plain, " +
			$"last_editor_dbref: $editorDbref, updated_at: $now, revision_number: $rev }}",
			parameters);

		var results = response.GetValue<List<WikiPageDbRecord>>(0);
		var updated = MapToWikiPage(results!.First());

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
		await ExecuteAsync("DELETE wiki_revision WHERE page_id = $id", parameters);

		var key = NormalizeSurrealId(id, "wiki_page");
		var delParams = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
		await ExecuteAsync("DELETE $id", delParams);

		return new OkNone();
	}

	public async Task<OneOf<OkNone, NotFound>> SetProtectionAsync(string id, bool protect)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		var key = NormalizeSurrealId(id, "wiki_page");
		var parameters = new Dictionary<string, object?>
		{
			["id"] = new StringRecordId(key),
			["isProtected"] = protect
		};
		await ExecuteAsync("UPDATE $id MERGE { is_protected: $isProtected }", parameters);

		return new OkNone();
	}

	// ── Revisions ─────────────────────────────────────────────────────────────

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
	{
		var parameters = new Dictionary<string, object?>
		{
			["pageId"] = pageId, ["skip"] = skip, ["take"] = take
		};
		var response = await ExecuteAsync(
			$"SELECT {WikiRevisionFields} FROM wiki_revision WHERE page_id = $pageId " +
			$"ORDER BY revision_number DESC LIMIT $skip, $take",
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
			$"SELECT {WikiRevisionFields} FROM wiki_revision WHERE page_id = $pageId AND revision_number = $rev",
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
				page_id: $pageId,
				revision_number: $rev,
				markdown_source: $markdown,
				editor_dbref: $editorDbref,
				timestamp: $timestamp,
				edit_summary: $editSummary
			}
			""",
			parameters);
	}

	private static WikiPage MapToWikiPage(WikiPageDbRecord r)
	{
		DateTimeOffset createdAt = default, updatedAt = default;
		DateTimeOffset.TryParse(r.CreatedAt, out createdAt);
		DateTimeOffset.TryParse(r.UpdatedAt, out updatedAt);
		return new WikiPage(
			Id: r.Id?.ToString() ?? "",
			Slug: r.Slug,
			Title: r.Title,
			Namespace: r.Namespace,
			MarkdownSource: r.MarkdownSource,
			RenderedHtml: r.RenderedHtml,
			PlainText: r.PlainText,
			AuthorDbref: r.AuthorDbref,
			LastEditorDbref: r.LastEditorDbref,
			CreatedAt: createdAt,
			UpdatedAt: updatedAt,
			IsProtected: r.IsProtected,
			RevisionNumber: r.RevisionNumber
		);
	}

	private static WikiRevision MapToWikiRevision(WikiRevisionDbRecord r)
	{
		DateTimeOffset timestamp = default;
		DateTimeOffset.TryParse(r.Timestamp, out timestamp);
		return new WikiRevision(
			Id: r.Id?.ToString() ?? "",
			PageId: r.PageId,
			RevisionNumber: r.RevisionNumber,
			MarkdownSource: r.MarkdownSource,
			EditorDbref: r.EditorDbref,
			Timestamp: timestamp,
			EditSummary: string.IsNullOrEmpty(r.EditSummary) ? null : r.EditSummary
		);
	}

	private static string Slugify(string title) =>
		title.ToLowerInvariant().Replace(' ', '_');

	#endregion
}
