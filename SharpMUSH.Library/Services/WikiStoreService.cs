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
	public Task<Found<WikiPage>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main)
		=> store.GetPageBySlugAsync(Namespace(ns), WikiHelpers.NormalizeCategory(category), WikiHelpers.Slugify(slug));

	public Task<Found<WikiPage>> GetByIdAsync(string id) => store.GetPageByIdAsync(id);

	public Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20) => store.GetRecentPagesAsync(count);

	public Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50)
		=> store.GetPagesAsync(Namespace(ns), skip, take);

	public Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null)
		=> store.GetPagesAsync(ns is { } value ? Namespace(value) : null, skip, take);

	public Task<int> CountPagesAsync(WikiNamespace? ns, bool includeDrafts)
		=> store.CountPagesAsync(ns is { } value ? Namespace(value) : null, includeDrafts);

	public Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50)
		=> store.GetPagesByCategoryAsync(WikiHelpers.NormalizeCategory(category), skip, take);

	public Task<IReadOnlyList<WikiPage>> GetByTagAsync(string tag, int skip = 0, int take = 50)
		=> store.GetPagesByTagAsync(tag.Trim().ToLowerInvariant(), skip, take);

	public async Task<Result<WikiPage>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? category = null,
		string? sourceLocale = null)
		=> SourceLocaleToStamp(sourceLocale) switch
		{
			string stampedLocale => await store.CreatePageAsync(NewPage(title, Render(markdown), authorDbref, ns, category,
				stampedLocale)),
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

	private static WikiPage NewPage(string title, WikiBody body, string authorDbref, WikiNamespace ns, string? category,
		string sourceLocale)
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
			IsProtected: false,
			RevisionNumber: 1)
		{
			Category = WikiHelpers.NormalizeCategory(category),
			SourceLocale = sourceLocale,
		};
	}

	public Task<Found<WikiPage>> UpdateAsync(string id, string markdown, string editorDbref, string? editSummary = null)
		=> store.UpdatePageBodyAsync(id, Render(markdown), editorDbref, editSummary, DateTimeOffset.UtcNow);

	public Task<Found<None>> DeleteAsync(string id, string editorDbref) => store.DeletePageAsync(id);

	public Task<Found<None>> SetProtectionAsync(string id, bool isProtected)
		=> store.SetPageProtectionAsync(id, isProtected);

	public Task<Found<WikiPage>> SetMetadataAsync(string id, string? category, IReadOnlyList<string> tags, bool published)
		=> store.SetPageMetadataAsync(id, WikiHelpers.NormalizeCategory(category), WikiHelpers.NormalizeTags(tags),
			published);

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
