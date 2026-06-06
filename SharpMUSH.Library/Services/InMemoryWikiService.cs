using System.Collections.Concurrent;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-memory implementation of <see cref="IWikiService"/>.
/// Intended for unit tests and development use.
/// An ArangoDB-backed implementation will replace the persistence layer in a later phase.
/// </summary>
/// <remarks>
/// Thread-safe: all state is stored in <see cref="ConcurrentDictionary{TKey,TValue}"/> instances.
/// Slug uniqueness is enforced per-namespace.
/// </remarks>
public sealed class InMemoryWikiService : IWikiService
{
	// ── Storage ───────────────────────────────────────────────────────────────

	private readonly ConcurrentDictionary<string, WikiPage> _pagesById = new();
	// Composite key: $"{namespace}:{slug}" → page ID
	private readonly ConcurrentDictionary<string, string> _slugIndex = new(StringComparer.OrdinalIgnoreCase);
	// pageId → ordered list of revisions
	private readonly ConcurrentDictionary<string, List<WikiRevision>> _revisions = new();

	private readonly WikiMarkdigPipeline _renderer;
	private int _idCounter;

	// ── Construction ──────────────────────────────────────────────────────────

	public InMemoryWikiService() : this(new WikiMarkdigPipeline()) { }

	public InMemoryWikiService(WikiMarkdigPipeline renderer)
	{
		_renderer = renderer;
	}

	// ── IWikiService: Read ────────────────────────────────────────────────────

	public Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main)
	{
		var key = SlugKey(ns, slug);
		if (_slugIndex.TryGetValue(key, out var id) && _pagesById.TryGetValue(id, out var page))
			return Task.FromResult<OneOf<WikiPage, NotFound>>(page);
		return Task.FromResult<OneOf<WikiPage, NotFound>>(new NotFound());
	}

	public Task<OneOf<WikiPage, NotFound>> GetByIdAsync(string id)
	{
		if (_pagesById.TryGetValue(id, out var page))
			return Task.FromResult<OneOf<WikiPage, NotFound>>(page);
		return Task.FromResult<OneOf<WikiPage, NotFound>>(new NotFound());
	}

	public Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20)
	{
		IReadOnlyList<WikiPage> result = _pagesById.Values
			.OrderByDescending(p => p.UpdatedAt)
			.Take(count)
			.ToList();
		return Task.FromResult(result);
	}

	public Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50)
	{
		var nsStr = ns.ToString().ToLowerInvariant();
		IReadOnlyList<WikiPage> result = _pagesById.Values
			.Where(p => p.Namespace.Equals(nsStr, StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p.Slug)
			.Skip(skip)
			.Take(take)
			.ToList();
		return Task.FromResult(result);
	}

	// ── IWikiService: Write ───────────────────────────────────────────────────

	public Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main)
	{
		var slug = Slugify(title);
		var slugKey = SlugKey(ns, slug);

		// Enforce slug uniqueness within namespace
		if (_slugIndex.ContainsKey(slugKey))
			return Task.FromResult<OneOf<WikiPage, Error<string>>>(
				new Error<string>($"A wiki page with slug '{slug}' already exists in namespace '{ns}'."));

		var id = NextId();
		var now = DateTimeOffset.UtcNow;
		var html = _renderer.RenderToHtml(markdown);
		var plain = _renderer.ExtractPlainText(markdown);
		var nsStr = ns.ToString().ToLowerInvariant();

		var page = new WikiPage(
			Id: id,
			Slug: slug,
			Title: title,
			Namespace: nsStr,
			MarkdownSource: markdown,
			RenderedHtml: html,
			PlainText: plain,
			AuthorDbref: authorDbref,
			LastEditorDbref: authorDbref,
			CreatedAt: now,
			UpdatedAt: now,
			IsProtected: false,
			RevisionNumber: 1);

		_pagesById[id] = page;
		_slugIndex[slugKey] = id;
		_revisions[id] = [];

		// Save initial revision snapshot
		SaveRevisionSnapshot(page, authorDbref, editSummary: null);

		return Task.FromResult<OneOf<WikiPage, Error<string>>>(page);
	}

	public Task<OneOf<WikiPage, NotFound>> UpdateAsync(
		string id,
		string markdown,
		string editorDbref,
		string? editSummary = null)
	{
		if (!_pagesById.TryGetValue(id, out var existing))
			return Task.FromResult<OneOf<WikiPage, NotFound>>(new NotFound());

		var now = DateTimeOffset.UtcNow;
		var html = _renderer.RenderToHtml(markdown);
		var plain = _renderer.ExtractPlainText(markdown);

		var updated = existing with
		{
			MarkdownSource = markdown,
			RenderedHtml = html,
			PlainText = plain,
			LastEditorDbref = editorDbref,
			UpdatedAt = now,
			RevisionNumber = existing.RevisionNumber + 1,
		};

		_pagesById[id] = updated;
		SaveRevisionSnapshot(updated, editorDbref, editSummary);

		return Task.FromResult<OneOf<WikiPage, NotFound>>(updated);
	}

	public Task<OneOf<None, NotFound>> DeleteAsync(string id, string editorDbref)
	{
		if (!_pagesById.TryRemove(id, out var page))
			return Task.FromResult<OneOf<None, NotFound>>(new NotFound());

		var slugKey = SlugKey(page.Namespace, page.Slug);
		_slugIndex.TryRemove(slugKey, out _);
		_revisions.TryRemove(id, out _);

		return Task.FromResult<OneOf<None, NotFound>>(new None());
	}

	public Task<OneOf<None, NotFound>> SetProtectionAsync(string id, bool isProtected)
	{
		if (!_pagesById.TryGetValue(id, out var existing))
			return Task.FromResult<OneOf<None, NotFound>>(new NotFound());

		_pagesById[id] = existing with { IsProtected = isProtected };
		return Task.FromResult<OneOf<None, NotFound>>(new None());
	}

	// ── IWikiService: Revisions ───────────────────────────────────────────────

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
	{
		if (!_revisions.TryGetValue(pageId, out var list))
			return Task.FromResult<IReadOnlyList<WikiRevision>>([]);

		IReadOnlyList<WikiRevision> result;
		lock (list)
		{
			result = list
				.OrderByDescending(r => r.RevisionNumber)
				.Skip(skip)
				.Take(take)
				.ToList();
		}
		return Task.FromResult(result);
	}

	public Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber)
	{
		if (!_revisions.TryGetValue(pageId, out var list))
			return Task.FromResult<OneOf<WikiRevision, NotFound>>(new NotFound());

		WikiRevision? result;
		lock (list)
		{
			result = list.FirstOrDefault(r => r.RevisionNumber == revisionNumber);
		}

		if (result is null)
			return Task.FromResult<OneOf<WikiRevision, NotFound>>(new NotFound());

		return Task.FromResult<OneOf<WikiRevision, NotFound>>(result);
	}

	// ── Internals ─────────────────────────────────────────────────────────────

	/// <summary>Generates a string slug from a title.</summary>
	private static string Slugify(string title) =>
		title.ToLowerInvariant().Replace(' ', '_');

	/// <summary>Composite dict key for the slug index.</summary>
	private static string SlugKey(WikiNamespace ns, string slug) =>
		$"{ns.ToString().ToLowerInvariant()}:{slug}";

	private static string SlugKey(string nsStr, string slug) =>
		$"{nsStr.ToLowerInvariant()}:{slug}";

	/// <summary>Returns a new, unique string ID.</summary>
	private string NextId() => Interlocked.Increment(ref _idCounter).ToString();

	/// <summary>Appends a full snapshot revision for the given page state.</summary>
	private void SaveRevisionSnapshot(WikiPage page, string editorDbref, string? editSummary)
	{
		var revList = _revisions.GetOrAdd(page.Id, _ => []);
		var rev = new WikiRevision(
			Id: $"{page.Id}:{page.RevisionNumber}",
			PageId: page.Id,
			RevisionNumber: page.RevisionNumber,
			MarkdownSource: page.MarkdownSource,
			EditorDbref: editorDbref,
			Timestamp: page.UpdatedAt,
			EditSummary: editSummary);

		lock (revList)
		{
			revList.Add(rev);
		}
	}
}
