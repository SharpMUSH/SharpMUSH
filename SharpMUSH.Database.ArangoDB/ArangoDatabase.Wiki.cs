using Core.Arango;
using Core.Arango.Protocol;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase : IWikiService
{
	#region Wiki

	private static readonly WikiMarkdigPipeline _wikiRenderer = new();

	// ── Read ──────────────────────────────────────────────────────────────────

	public async Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR p IN @@c FILTER p.Namespace == @ns AND p.Slug == @slug RETURN p",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "ns", nsStr },
				{ "slug", slug }
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

	// ── Write ─────────────────────────────────────────────────────────────────

	public async Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		var slug = Slugify(title);

		// Enforce slug uniqueness within namespace
		var existing = await GetBySlugAsync(slug, ns);
		if (existing.IsT0)
			return new Error<string>($"A wiki page with slug '{slug}' already exists in namespace '{nsStr}'.");

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
			RevisionNumber = 1
		};

		var created = await arangoDb.Document.CreateAsync<object, JsonElement>(
			handle, DatabaseConstants.WikiPages, doc, returnNew: true);

		var page = WikiPageFromJson(created.New);

		// Save initial revision snapshot
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

		// Remove all revisions first
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

	// ── Revisions ─────────────────────────────────────────────────────────────

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR r IN @@c FILTER r.PageId == @pageId SORT r.RevisionNumber DESC LIMIT @skip, @take RETURN r",
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
			"FOR r IN @@c FILTER r.PageId == @pageId AND r.RevisionNumber == @rev RETURN r",
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

	// ── Internals ─────────────────────────────────────────────────────────────

	private async Task SaveWikiRevisionAsync(WikiPage page, string editorDbref, string? editSummary)
	{
		var doc = new
		{
			PageId = page.Id,
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
		);
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
		);
	}

	private static string Slugify(string title) =>
		WikiHelpers.Slugify(title);

	#endregion
}
