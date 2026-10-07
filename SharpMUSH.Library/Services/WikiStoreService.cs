using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// <see cref="IWikiService"/> over an <see cref="IWikiStore"/>: every rule that is not about storage —
/// slugs, namespace and category normalisation, locale validation, Markdown rendering and the
/// source-locale guard on translations — lives here once, and the store only persists.
/// </summary>
public sealed class WikiStoreService(IWikiStore store, WikiMarkdigPipeline renderer) : IWikiService
{
	// Every requirement set, read once and kept until a write here changes one. Requirements are consulted on
	// every page read, so they are not read from the store each time.
	private readonly System.Threading.Lock _requirementsGate = new();
	private WikiRequirements? _requirements;
	private int _requirementsVersion;

	public Task<Found<WikiPage>> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main)
		=> store.GetPageBySlugAsync(Namespace(ns), WikiHelpers.Slugify(slug));

	public Task<Found<WikiPage>> GetByIdAsync(string id) => store.GetPageByIdAsync(id);

	public Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20, WikiVisibility? visibility = null)
		=> store.GetRecentPagesAsync(count, visibility ?? WikiVisibility.All);

	public Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50, WikiVisibility? visibility = null)
		=> store.GetPagesAsync(Namespace(ns), skip, take, visibility ?? WikiVisibility.All);

	public Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null, WikiVisibility? visibility = null)
		=> store.GetPagesAsync(ns is { } value ? Namespace(value) : null, skip, take, visibility ?? WikiVisibility.All);

	public Task<int> CountPagesAsync(WikiNamespace? ns, WikiVisibility visibility)
		=> store.CountPagesAsync(ns is { } value ? Namespace(value) : null, visibility);

	public Task<WikiPageCounts> CountPagesByStateAsync(WikiVisibility visibility)
		=> store.CountPagesByStateAsync(visibility);

	public Task<IReadOnlyDictionary<string, int>> CountPagesByCategoryAsync(WikiVisibility visibility)
		=> store.CountPagesByCategoryAsync(visibility);

	public async Task<IReadOnlyList<WikiPage>> NameCategoriesAsync(IEnumerable<string> names, IEnumerable<string> alreadyFiled,
		string authorDbref, string sourceLocale)
	{
		var filed = alreadyFiled.Select(WikiHelpers.CategoryKey).ToHashSet(StringComparer.Ordinal);
		var created = new List<WikiPage>();
		foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0).DistinctBy(WikiHelpers.CategoryKey))
		{
			var key = WikiHelpers.CategoryKey(name);
			// A name spelled exactly as its key says nothing its key does not, and a page titled with it would only
			// replace the label the key already gives ("places" shows as "Places").
			if (name == key || filed.Contains(key)
					|| await store.GetPageBySlugAsync(Namespace(WikiNamespace.Category), key) is WikiPage)
				continue;
			// A page created meanwhile by someone else wins; its title is the name.
			if (await store.CreatePageAsync(NewPage(name, Render(string.Empty), authorDbref, WikiNamespace.Category,
						WikiHelpers.NormalizeLocaleOrEmpty(sourceLocale), [])) is WikiPage page)
				created.Add(page);
		}

		return created;
	}

	public Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50, WikiVisibility? visibility = null)
		=> store.GetPagesByCategoryAsync(WikiHelpers.CategoryKey(category), skip, take, visibility ?? WikiVisibility.All);

	public async Task<Result<WikiPage>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? sourceLocale = null,
		IEnumerable<string>? categories = null)
		=> SourceLocaleToStamp(sourceLocale) switch
		{
			string stampedLocale => await store.CreatePageAsync(NewPage(title, Render(markdown), authorDbref, ns, stampedLocale,
				WikiHelpers.NormalizeCategories(categories, ns))),
			Error<string> error => error,
		};

	/// <summary>
	/// The locale a new page is stamped with. SourceLocale is materialised once and never re-derived, so a
	/// junk tag must not reach storage: null or blank is the "not stamped" case, left to the migration
	/// backfill as <see cref="string.Empty"/>; a non-blank tag that is not a locale is an error, because
	/// storing it would corrupt every later read.
	/// </summary>
	private static Result<string> SourceLocaleToStamp(string? sourceLocale)
		=> string.IsNullOrWhiteSpace(sourceLocale) ? string.Empty : WikiHelpers.NormalizeLocale(sourceLocale);

	private static WikiPage NewPage(string title, WikiBody body, string authorDbref, WikiNamespace ns, string sourceLocale,
		IReadOnlyList<string> categories)
	{
		var now = DateTimeOffset.UtcNow;
		return new WikiPage(
			Id: string.Empty,
			Slug: WikiHelpers.Slugify(title),
			Title: title,
			Namespace: Namespace(ns),
			MarkdownSource: body.Markdown,
			RenderedHtml: body.Html,
			PlainText: body.PlainText,
			AuthorDbref: authorDbref,
			LastEditorDbref: authorDbref,
			CreatedAt: now,
			UpdatedAt: now,
			RevisionNumber: 1)
		{
			Categories = categories,
			SourceLocale = sourceLocale,
		};
	}

	public Task<Found<WikiPage>> UpdateAsync(string id, string markdown, string editorDbref, string? editSummary = null)
		=> store.UpdatePageBodyAsync(id, Render(markdown), editorDbref, editSummary, DateTimeOffset.UtcNow);

	public async Task<Found<None>> DeleteAsync(string id, string editorDbref)
	{
		var deleted = await store.DeletePageAsync(id);
		ForgetRequirements();
		return deleted;
	}

	public async Task<WikiRequirements> GetRequirementsAsync()
	{
		int version;
		lock (_requirementsGate)
		{
			if (_requirements is { } cached) return cached;
			version = _requirementsVersion;
		}

		var loaded = new WikiRequirements(await store.GetRequirementsAsync());
		lock (_requirementsGate)
		{
			// A write that landed while this read was out leaves the version moved; keep nothing then.
			if (version == _requirementsVersion) _requirements = loaded;
		}

		return loaded;
	}

	private void ForgetRequirements()
	{
		lock (_requirementsGate)
		{
			_requirementsVersion++;
			_requirements = null;
		}
	}

	public async Task<Found<None>> SetRequirementsAsync(WikiRuleTarget target,
		IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> required, string editorDbref)
	{
		var key = target.Scope switch
		{
			WikiRuleScope.Namespace => target.Key.Trim().ToLowerInvariant(),
			WikiRuleScope.Category => WikiHelpers.CategoryKey(target.Key),
			_ => target.Key,
		};
		var normalized = required
			.Select(pair => (pair.Key, Scopes: (IReadOnlyList<string>)pair.Value
				.Select(scope => scope.Trim().ToLowerInvariant())
				.Where(scope => scope.Length > 0)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.ToList()))
			.Where(pair => pair.Scopes.Count > 0)
			.ToDictionary(pair => pair.Key, pair => pair.Scopes);
		var result = await store.SetRequirementsAsync(new WikiRequirementSet(target with { Key = key }, normalized, editorDbref,
			DateTimeOffset.UtcNow));
		ForgetRequirements();
		return result;
	}

	public async Task<Found<WikiPage>> SetMetadataAsync(string id, IEnumerable<string> categories, bool published)
		=> await store.GetPageByIdAsync(id) switch
		{
			// A page never changes namespace, so the categories its namespace adds are read from the stored page.
			WikiPage page => await store.SetPageMetadataAsync(id,
				WikiHelpers.NormalizeCategories(categories, WikiHelpers.ParseNamespace(page.Namespace)), published),
			NotFound notFound => notFound,
		};

	public Task<IReadOnlyList<string>> GetPinnedCategoriesAsync() => store.GetPinnedCategoriesAsync();

	public async Task<Result<bool>> SetCategoryPinnedAsync(string category, bool pinned)
		=> WikiHelpers.CategoryKey(category) is { Length: > 0 } key
			? await store.SetCategoryPinnedAsync(key, pinned)
			: new Error<string>("A category needs a name.");

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
		=> store.GetRevisionsAsync(pageId, string.Empty, skip, take);

	public Task<Found<WikiRevision>> GetRevisionAsync(string pageId, int revisionNumber)
		=> store.GetRevisionAsync(pageId, string.Empty, revisionNumber);

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
		=> store.GetTranslationSummariesAsync(pageId);

	public Task<IReadOnlyList<WikiTranslation>> GetAllTranslationsAsync(int skip = 0, int take = 50)
		=> store.GetTranslationsAsync(skip, take);

	/// <remarks>This is a read, so an unusable tag is "no such translation" rather than an error.</remarks>
	public async Task<Found<WikiTranslation>> GetTranslationAsync(string pageId, string locale)
		=> WikiHelpers.NormalizeLocaleOrEmpty(locale) is { Length: > 0 } normalized
			? await store.GetTranslationAsync(pageId, normalized)
			: new NotFound();

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
	/// Writes a translation under an already-normalised locale, refusing the page's own source locale: that
	/// body is the page itself. The source locale is stamped at creation, or by the migration backfill before
	/// anything is served, and never changes after, so checking it ahead of the store's compare-and-swap
	/// cannot race a write.
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
		if (await store.GetPageByIdAsync(pageId) is not WikiPage page)
			return new Error<string>($"No wiki page with id '{pageId}'.");

		if (page.SourceLocale.Length > 0 && string.Equals(page.SourceLocale, normalized, StringComparison.OrdinalIgnoreCase))
			return new Error<string>(
				$"'{normalized}' is the page's source locale; edit the page itself rather than adding a translation.");

		return await store.WriteTranslationAsync(pageId, normalized, title, Render(markdown), editorDbref, editSummary,
			published, expectedRevisionNumber, DateTimeOffset.UtcNow);
	}

	public async Task<Found<None>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
		=> WikiHelpers.NormalizeLocaleOrEmpty(locale) is { Length: > 0 } normalized
			? await store.DeleteTranslationAsync(pageId, normalized)
			: new NotFound();

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take)
		=> RevisionStream(locale) is { } stream ? await store.GetRevisionsAsync(pageId, stream, skip, take) : [];

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsBeforeForLocaleAsync(string pageId, string locale,
		int beforeRevisionNumber, int take)
		=> RevisionStream(locale) is { } stream ? await store.GetRevisionsBeforeAsync(pageId, stream, beforeRevisionNumber, take) : [];

	public async Task<Found<WikiRevision>> GetRevisionForLocaleAsync(string pageId, string locale, int revisionNumber)
		=> revisionNumber >= 0 && RevisionStream(locale) is { } stream
			? await store.GetRevisionAsync(pageId, stream, revisionNumber)
			: new NotFound();

	/// <summary>
	/// The revision stream a caller's locale names: empty is the source stream, anything else is matched
	/// after normalisation, and a tag that does not normalise names no stream at all — never the source
	/// one, whose body a rollback would otherwise write back as a "translation".
	/// </summary>
	private static string? RevisionStream(string locale)
		=> locale.Length == 0 ? string.Empty
			: WikiHelpers.NormalizeLocaleOrEmpty(locale) is { Length: > 0 } normalized ? normalized
			: null;

	private WikiBody Render(string markdown)
		=> new(markdown, renderer.RenderToHtml(markdown), renderer.ExtractPlainText(markdown));

	private static string Namespace(WikiNamespace ns) => ns.ToString().ToLowerInvariant();
}
