using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library;

/// <summary>
/// A page's Markdown together with what <c>WikiMarkdigPipeline</c> made of it, rendered once by
/// <see cref="Services.WikiStoreService"/> before it reaches a store.
/// </summary>
public readonly record struct WikiBody(string Markdown, string Html, string PlainText);

/// <summary>
/// Wiki pages, their revision streams and their translations: the storage half of
/// <see cref="Services.Interfaces.IWikiService"/>.
/// </summary>
/// <remarks>
/// Every argument arrives normalised by <see cref="Services.WikiStoreService"/>, which owns each rule that
/// is not about storage: a slug is already slugified, a namespace lower-cased, a category, tag or locale
/// already canonical, and Markdown already rendered. What stays here is what needs the store's own
/// atomicity — the <c>(namespace, category, slug)</c> uniqueness check, revision numbering, and the
/// translation compare-and-swap — plus the page-id format, which is the store's to choose.
/// <para>
/// A locale of <see cref="string.Empty"/> names the page's source-locale revision stream.
/// </para>
/// </remarks>
public interface IWikiStore
{
	Task<Found<WikiPage>> GetPageBySlugAsync(string ns, string category, string slug);

	Task<Found<WikiPage>> GetPageByIdAsync(string id);

	/// <summary>Pages by <c>UpdatedAt</c> descending, newest-created first within one timestamp.</summary>
	Task<IReadOnlyList<WikiPage>> GetRecentPagesAsync(int count);

	/// <summary>Pages ordered by namespace then slug, ordinally; <paramref name="ns"/> null for every namespace.</summary>
	Task<IReadOnlyList<WikiPage>> GetPagesAsync(string? ns, int skip, int take);

	/// <summary>A page whose stored published flag is absent counts as published.</summary>
	Task<int> CountPagesAsync(string? ns, bool includeDrafts);

	/// <summary>Pages in <paramref name="category"/>, case-insensitively, ordered by title.</summary>
	Task<IReadOnlyList<WikiPage>> GetPagesByCategoryAsync(string category, int skip, int take);

	/// <summary>Pages carrying <paramref name="tag"/>, case-insensitively, ordered by title.</summary>
	Task<IReadOnlyList<WikiPage>> GetPagesByTagAsync(string tag, int skip, int take);

	/// <summary>
	/// Stores <paramref name="page"/> under a newly allocated id, ignoring the one it carries, with its
	/// body as source revision 1. <c>Error</c> when its <c>(namespace, category, slug)</c> is taken.
	/// </summary>
	Task<Result<WikiPage>> CreatePageAsync(WikiPage page);

	/// <summary>Replaces the page's body and appends it as the next source revision.</summary>
	Task<Found<WikiPage>> UpdatePageBodyAsync(string id, WikiBody body, string editorDbref, string? editSummary,
		DateTimeOffset at);

	/// <summary>Removes the page, every revision stream and every translation of it.</summary>
	Task<Found<None>> DeletePageAsync(string id);

	Task<Found<None>> SetPageProtectionAsync(string id, bool isProtected);

	/// <summary>
	/// Sets category, tags and published flag without a revision. Category is part of the page's identity,
	/// so a change that would collide with another page is refused as <c>NotFound</c>.
	/// </summary>
	Task<Found<WikiPage>> SetPageMetadataAsync(string id, string category, IReadOnlyList<string> tags, bool published);

	/// <summary>One locale's revisions, by revision number descending.</summary>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, string locale, int skip, int take);

	Task<Found<WikiRevision>> GetRevisionAsync(string pageId, string locale, int revisionNumber);

	/// <summary>Every translation of one page as a bodyless summary, ordered by locale.</summary>
	Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationSummariesAsync(string pageId);

	/// <summary>Every translation with its body, ordered by page then locale.</summary>
	Task<IReadOnlyList<WikiTranslation>> GetTranslationsAsync(int skip, int take);

	Task<Found<WikiTranslation>> GetTranslationAsync(string pageId, string locale);

	/// <summary>
	/// Creates (<paramref name="expectedRevisionNumber"/> null) or compare-and-swaps a translation, appending
	/// its revision in the same step. <c>Error</c> when the page does not exist; a
	/// <see cref="WikiWriteConflict"/> when another writer got there first.
	/// </summary>
	Task<TranslationWriteResult> WriteTranslationAsync(string pageId, string locale, string title, WikiBody body,
		string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber, DateTimeOffset at);

	/// <summary>Removes the translation and its revision stream.</summary>
	Task<Found<None>> DeleteTranslationAsync(string pageId, string locale);
}
