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

	// ── Read ──────────────────────────────────────────────────────────────────

	public async Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (p:WikiPage {namespace: $ns, slug: $slug}) RETURN p",
			new { ns = nsStr, slug });

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
		var pageId = Guid.NewGuid().ToString("N");
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync("""
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
				revisionNumber: 1
			}) RETURN p
			""",
			new
			{
				pageId, slug, title, ns = nsStr, markdown, html, plain,
				authorDbref, now = now.ToString("O")
			});

		var records = await result.ToListAsync();
		var page = NodeToWikiPage(records[0]["p"].As<INode>());

		await SaveMemgraphRevisionAsync(session, page, authorDbref, null, now);
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
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync("""
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
				id, markdown, html, plain, editorDbref,
				now = now.ToString("O"), rev = newRevision
			});

		var records = await result.ToListAsync();
		var updated = NodeToWikiPage(records[0]["p"].As<INode>());

		await SaveMemgraphRevisionAsync(session, updated, editorDbref, editSummary, now);
		return updated;
	}

	public async Task<OneOf<None, NotFound>> DeleteAsync(string id, string editorDbref)
	{
		var lookupResult = await GetByIdAsync(id);
		if (lookupResult.IsT1)
			return new NotFound();

		await using var session = driver.AsyncSession();
		// Delete revisions first
		await session.RunAsync(
			"MATCH (r:WikiRevision {pageId: $id}) DELETE r",
			new { id });

		await session.RunAsync(
			"MATCH (p:WikiPage {pageId: $id}) DELETE p",
			new { id });

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

	// ── Revisions ─────────────────────────────────────────────────────────────

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (r:WikiRevision {pageId: $pageId}) RETURN r ORDER BY r.revisionNumber DESC SKIP $skip LIMIT $take",
			new { pageId, skip, take });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiRevision(r["r"].As<INode>())).ToList().AsReadOnly();
	}

	public async Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (r:WikiRevision {pageId: $pageId, revisionNumber: $rev}) RETURN r",
			new { pageId, rev = revisionNumber });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiRevision(records[0]["r"].As<INode>());
	}

	// ── Internals ─────────────────────────────────────────────────────────────

	private static async Task SaveMemgraphRevisionAsync(
		IAsyncSession session,
		WikiPage page,
		string editorDbref,
		string? editSummary,
		DateTimeOffset timestamp)
	{
		await session.RunAsync("""
			CREATE (r:WikiRevision {
				revisionId: $revisionId,
				pageId: $pageId,
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
		);
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
		);
	}

	private static string Slugify(string title) =>
		title.ToLowerInvariant().Replace(' ', '_');

	#endregion
}
