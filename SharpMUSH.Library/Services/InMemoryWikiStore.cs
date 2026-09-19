using System.Collections.Concurrent;
using System.Globalization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services;

/// <summary>
/// An in-memory <see cref="IWikiStore"/>, for tests that want <see cref="WikiStoreService"/> without a
/// database. Production registers the Lightning provider as the store; nothing registers this one.
/// </summary>
/// <remarks>
/// Orders every listing the way the Lightning store does, ordinally, so a test written against this
/// store describes the provider rather than itself.
/// </remarks>
public sealed class InMemoryWikiStore : IWikiStore
{
	private readonly ConcurrentDictionary<string, WikiPage> _pagesById = new();
	private readonly ConcurrentDictionary<string, string> _slugIndex = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, List<WikiRevision>> _revisions = new();
	private readonly ConcurrentDictionary<(string PageId, string Locale), WikiTranslation> _translations = new();

	private int _idCounter;

	/// <summary>A <see cref="WikiStoreService"/> over a fresh in-memory store.</summary>
	public static WikiStoreService CreateService() => new(new InMemoryWikiStore(), new WikiMarkdigPipeline());

	public Task<Found<WikiPage>> GetPageBySlugAsync(string ns, string category, string slug)
		=> Task.FromResult<Found<WikiPage>>(
			_slugIndex.TryGetValue(WikiHelpers.SlugKey(ns, category, slug), out var id)
			&& _pagesById.TryGetValue(id, out var page)
				? page
				: new NotFound());

	public Task<Found<WikiPage>> GetPageByIdAsync(string id)
		=> Task.FromResult<Found<WikiPage>>(_pagesById.TryGetValue(id, out var page) ? page : new NotFound());

	public Task<IReadOnlyList<WikiPage>> GetRecentPagesAsync(int count)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(_pagesById.Values
			.OrderByDescending(p => p.UpdatedAt)
			.ThenByDescending(p => int.Parse(p.Id, CultureInfo.InvariantCulture))
			.Take(count)
			.ToList());

	public Task<IReadOnlyList<WikiPage>> GetPagesAsync(string? ns, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(InNamespace(ns)
			.OrderBy(p => p.Namespace, StringComparer.Ordinal)
			.ThenBy(p => p.Slug, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList());

	public Task<int> CountPagesAsync(string? ns, bool includeDrafts)
		=> Task.FromResult(InNamespace(ns).Count(p => includeDrafts || p.Published));

	public Task<IReadOnlyList<WikiPage>> GetPagesByCategoryAsync(string category, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(_pagesById.Values
			.Where(p => p.Category is not null && p.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p.Title, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList());

	public Task<IReadOnlyList<WikiPage>> GetPagesByTagAsync(string tag, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiPage>>(_pagesById.Values
			.Where(p => p.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
			.OrderBy(p => p.Title, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList());

	private IEnumerable<WikiPage> InNamespace(string? ns)
		=> ns is null
			? _pagesById.Values
			: _pagesById.Values.Where(p => p.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase));

	public Task<Result<WikiPage>> CreatePageAsync(WikiPage page)
	{
		var id = Interlocked.Increment(ref _idCounter).ToString(CultureInfo.InvariantCulture);
		var stored = page with { Id = id };

		// TryAdd is the uniqueness check and the claim in one step, so two creators of the same
		// (namespace, category, slug) cannot both pass.
		if (!_slugIndex.TryAdd(WikiHelpers.SlugKey(stored.Namespace, stored.Category, stored.Slug), id))
			return Task.FromResult<Result<WikiPage>>(new Error<string>(
				$"A wiki page with slug '{stored.Slug}' already exists in namespace '{stored.Namespace}' category '{stored.Category}'."));

		_pagesById[id] = stored;
		AppendRevision(id, string.Empty, 1, stored.MarkdownSource, stored.AuthorDbref, null, stored.CreatedAt);
		return Task.FromResult<Result<WikiPage>>(stored);
	}

	public Task<Found<WikiPage>> UpdatePageBodyAsync(string id, WikiBody body, string editorDbref, string? editSummary,
		DateTimeOffset at)
	{
		if (!_pagesById.TryGetValue(id, out var existing))
			return Task.FromResult<Found<WikiPage>>(new NotFound());

		var updated = existing with
		{
			MarkdownSource = body.Markdown,
			RenderedHtml = body.Html,
			PlainText = body.PlainText,
			LastEditorDbref = editorDbref,
			UpdatedAt = at,
			RevisionNumber = existing.RevisionNumber + 1,
		};

		_pagesById[id] = updated;
		AppendRevision(id, string.Empty, updated.RevisionNumber, body.Markdown, editorDbref, editSummary, at);
		return Task.FromResult<Found<WikiPage>>(updated);
	}

	public Task<Found<None>> DeletePageAsync(string id)
	{
		if (!_pagesById.TryRemove(id, out var page))
			return Task.FromResult<Found<None>>(new NotFound());

		_slugIndex.TryRemove(WikiHelpers.SlugKey(page.Namespace, page.Category, page.Slug), out _);
		_revisions.TryRemove(id, out _);

		// Keys is a snapshot, so removing while walking it is safe.
		foreach (var key in _translations.Keys.Where(k => k.PageId == id))
			_translations.TryRemove(key, out _);

		return Task.FromResult<Found<None>>(new None());
	}

	public Task<Found<None>> SetPageProtectionAsync(string id, bool isProtected)
	{
		if (!_pagesById.TryGetValue(id, out var existing))
			return Task.FromResult<Found<None>>(new NotFound());

		_pagesById[id] = existing with { IsProtected = isProtected };
		return Task.FromResult<Found<None>>(new None());
	}

	public Task<Found<WikiPage>> SetPageMetadataAsync(string id, string category, IReadOnlyList<string> tags,
		bool published)
	{
		if (!_pagesById.TryGetValue(id, out var existing))
			return Task.FromResult<Found<WikiPage>>(new NotFound());

		// Category is part of page identity, so changing it re-keys the page in the slug index: reserve the
		// new key first, refusing a collision, before releasing the old one.
		if (!string.Equals(category, existing.Category, StringComparison.OrdinalIgnoreCase))
		{
			if (!_slugIndex.TryAdd(WikiHelpers.SlugKey(existing.Namespace, category, existing.Slug), id))
				return Task.FromResult<Found<WikiPage>>(new NotFound());
			_slugIndex.TryRemove(WikiHelpers.SlugKey(existing.Namespace, existing.Category, existing.Slug), out _);
		}

		var updated = existing with { Category = category, Tags = tags, Published = published };
		_pagesById[id] = updated;
		return Task.FromResult<Found<WikiPage>>(updated);
	}

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, string locale, int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiRevision>>(RevisionSnapshot(pageId)
			.Where(r => string.Equals(r.Locale, locale, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(r => r.RevisionNumber)
			.Skip(skip)
			.Take(take)
			.ToList());

	public Task<Found<WikiRevision>> GetRevisionAsync(string pageId, string locale, int revisionNumber)
		=> Task.FromResult<Found<WikiRevision>>(RevisionSnapshot(pageId).FirstOrDefault(r =>
			r.RevisionNumber == revisionNumber && string.Equals(r.Locale, locale, StringComparison.OrdinalIgnoreCase))
			is { } revision
				? revision
				: new NotFound());

	/// <summary>A copy of one page's revisions, taken under the list's lock; empty when it has none.</summary>
	private WikiRevision[] RevisionSnapshot(string pageId)
	{
		if (!_revisions.TryGetValue(pageId, out var list)) return [];
		lock (list) return [.. list];
	}

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationSummariesAsync(string pageId)
		=> Task.FromResult<IReadOnlyList<WikiTranslationSummary>>(_translations.Values
			.Where(t => t.PageId == pageId)
			.OrderBy(t => t.Locale, StringComparer.Ordinal)
			.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
			.ToList());

	public Task<IReadOnlyList<WikiTranslation>> GetTranslationsAsync(int skip, int take)
		=> Task.FromResult<IReadOnlyList<WikiTranslation>>(_translations.Values
			.OrderBy(t => t.PageId, StringComparer.Ordinal)
			.ThenBy(t => t.Locale, StringComparer.Ordinal)
			.Skip(skip)
			.Take(take)
			.ToList());

	public Task<Found<WikiTranslation>> GetTranslationAsync(string pageId, string locale)
		=> Task.FromResult<Found<WikiTranslation>>(
			_translations.TryGetValue((pageId, locale), out var translation) ? translation : new NotFound());

	public Task<TranslationWriteResult> WriteTranslationAsync(string pageId, string locale, string title, WikiBody body,
		string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber, DateTimeOffset at)
		=> Task.FromResult(_pagesById.ContainsKey(pageId)
			? WriteTranslation((pageId, locale), title, body, editorDbref, editSummary, published, expectedRevisionNumber, at)
			: new Error<string>($"No wiki page with id '{pageId}'."));

	/// <summary>
	/// The compare-and-swap. Never an <c>AddOrUpdate</c>: that would fold two writers who both loaded
	/// revision 4 into one revision 5 and lose one translator's prose.
	/// </summary>
	private TranslationWriteResult WriteTranslation((string PageId, string Locale) key, string title, WikiBody body,
		string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber, DateTimeOffset at)
	{
		WikiTranslation updated;
		if (expectedRevisionNumber is null)
		{
			updated = new WikiTranslation(
				Id: $"{key.PageId}:{key.Locale}",
				PageId: key.PageId,
				Locale: key.Locale,
				Title: title,
				MarkdownSource: body.Markdown,
				RenderedHtml: body.Html,
				PlainText: body.PlainText,
				LastEditorDbref: editorDbref,
				CreatedAt: at,
				UpdatedAt: at,
				Published: published,
				RevisionNumber: 1);

			// Create-only: an existing row is a conflict, not something to overwrite.
			if (!_translations.TryAdd(key, updated))
				return WikiWriteConflict.AlreadyExists;
		}
		else
		{
			// A translation the caller loaded and somebody then deleted. Still a lost write, not a bad
			// request: re-creating it here would resurrect a row somebody deliberately removed.
			if (!_translations.TryGetValue(key, out var existing))
				return WikiWriteConflict.TranslationGone;

			if (existing.RevisionNumber != expectedRevisionNumber.Value)
				return WikiWriteConflict.StaleRevision;

			updated = existing with
			{
				Title = title,
				MarkdownSource = body.Markdown,
				RenderedHtml = body.Html,
				PlainText = body.PlainText,
				LastEditorDbref = editorDbref,
				UpdatedAt = at,
				Published = published,
				RevisionNumber = existing.RevisionNumber + 1,
			};

			// TryUpdate's comparison value is the CAS: a writer who won the race between TryGetValue and
			// here has already replaced `existing`, so this fails and no revision is appended.
			if (!_translations.TryUpdate(key, updated, existing))
				return WikiWriteConflict.StaleRevision;
		}

		AppendRevision(key.PageId, key.Locale, updated.RevisionNumber, body.Markdown, editorDbref, editSummary, at);
		return updated;
	}

	public Task<Found<None>> DeleteTranslationAsync(string pageId, string locale)
	{
		if (!_translations.TryRemove((pageId, locale), out _))
			return Task.FromResult<Found<None>>(new NotFound());

		if (_revisions.TryGetValue(pageId, out var list))
			lock (list) list.RemoveAll(r => string.Equals(r.Locale, locale, StringComparison.OrdinalIgnoreCase));

		return Task.FromResult<Found<None>>(new None());
	}

	/// <summary>
	/// Appends one revision. The id carries the locale for a translation, so its revisions can never collide
	/// with the source stream's <c>{PageId}:{RevisionNumber}</c>.
	/// </summary>
	private void AppendRevision(string pageId, string locale, int revisionNumber, string markdown, string editorDbref,
		string? editSummary, DateTimeOffset at)
	{
		var revision = new WikiRevision(
			Id: locale.Length == 0 ? $"{pageId}:{revisionNumber}" : $"{pageId}:{locale}:{revisionNumber}",
			PageId: pageId,
			RevisionNumber: revisionNumber,
			MarkdownSource: markdown,
			EditorDbref: editorDbref,
			Timestamp: at,
			EditSummary: editSummary)
		{
			Locale = locale,
		};

		var list = _revisions.GetOrAdd(pageId, _ => []);
		lock (list) list.Add(revision);
	}
}
