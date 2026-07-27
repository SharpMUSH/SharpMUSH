using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// CRUD service for wiki pages and their revision history.
/// The in-memory implementation is for testing and development;
/// database implementations follow in a later phase.
/// </summary>
/// <remarks>
/// All methods that might not find a resource return <c>OneOf&lt;T, NotFound&gt;</c> rather
/// than <c>null</c>.  Methods that can fail due to a conflict (e.g. duplicate slug) return
/// <c>OneOf&lt;T, Error&lt;string&gt;&gt;</c> where <c>Error.Value</c> is a human-readable message.
/// </remarks>
public interface IWikiService
{
	/// <summary>
	/// Retrieves a wiki page by its (namespace, category, slug) identity.
	/// <paramref name="category"/> is normalised (null/blank → <c>general</c>) and
	/// <paramref name="slug"/> is normalised the same way <see cref="CreateAsync"/> derives it from
	/// a title (see <c>WikiHelpers.Slugify</c>), so callers may pass a display name such as
	/// <c>"Mannaz Byron"</c> and reach the page stored as <c>mannaz_byron</c>.
	/// Returns <c>NotFound</c> if no matching page exists.
	/// </summary>
	Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main);

	/// <summary>
	/// Retrieves a wiki page by its storage ID.
	/// Returns <c>NotFound</c> if no matching page exists.
	/// </summary>
	Task<OneOf<WikiPage, NotFound>> GetByIdAsync(string id);

	/// <summary>
	/// Returns the most recently updated pages, ordered by <c>UpdatedAt</c> descending.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20);

	/// <summary>
	/// Lists pages within a given namespace, with skip/take pagination.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50);

	/// <summary>
	/// Lists ALL pages (optionally restricted to one namespace), ordered by
	/// namespace then slug, with skip/take pagination. Includes unpublished pages —
	/// callers are responsible for visibility filtering.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null);

	/// <summary>
	/// Returns the total page count (optionally restricted to one namespace).
	/// </summary>
	Task<int> CountPagesAsync(WikiNamespace? ns = null);

	/// <summary>
	/// Lists pages with the given category (case-insensitive), ordered by title.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50);

	/// <summary>
	/// Lists pages carrying the given tag (case-insensitive), ordered by title.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> GetByTagAsync(string tag, int skip = 0, int take = 50);

	/// <summary>
	/// Creates a new wiki page. The (namespace, category, slug) identity must be unique.
	/// <paramref name="category"/> is normalised (null/blank → <c>general</c>) and is part of
	/// the page's identity, so it is fixed at creation. Renders the Markdown to HTML and extracts
	/// plain text at creation time.
	/// <paramref name="sourceLocale"/> records the locale the body is authored in, canonicalised through
	/// <c>WikiHelpers.NormalizeLocale</c>. It is materialised once here and immutable thereafter — nothing
	/// re-derives it on read. Null or blank stores <see cref="string.Empty"/>, meaning "not yet stamped";
	/// the wiki-translations migration backfills those, and both real create paths supply
	/// <c>IWikiLocalizationService.DefaultLocale</c>.
	/// Returns <c>Error&lt;string&gt;</c> when a page with the same (namespace, category, slug) already
	/// exists, or when <paramref name="sourceLocale"/> is non-blank and not a recognised locale tag.
	/// </summary>
	Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? category = null,
		string? sourceLocale = null);

	/// <summary>
	/// Updates an existing page's Markdown content.  Increments the revision counter,
	/// saves a revision snapshot, and re-renders HTML / plain text.
	/// Returns <c>NotFound</c> when no page with <paramref name="id"/> exists.
	/// </summary>
	Task<OneOf<WikiPage, NotFound>> UpdateAsync(
		string id,
		string markdown,
		string editorDbref,
		string? editSummary = null);

	/// <summary>
	/// Deletes a wiki page, all its revisions, all its translations and those translations' revisions.
	/// Returns <c>None</c> if a page was found and deleted; <c>NotFound</c> if not found.
	/// </summary>
	Task<OneOf<None, NotFound>> DeleteAsync(string id, string editorDbref);

	/// <summary>
	/// Sets the protection flag on a page.
	/// Protected pages can only be edited by admin-level users.
	/// Returns <c>NotFound</c> when no page with <paramref name="id"/> exists.
	/// </summary>
	Task<OneOf<None, NotFound>> SetProtectionAsync(string id, bool isProtected);

	/// <summary>
	/// Sets the metadata fields (category, tags, published flag) on a page.
	/// Does NOT create a revision — metadata changes are not content edits.
	/// Category and tags are normalised to lower-case; tags are de-duplicated.
	/// Returns the updated page, or <c>NotFound</c> when no page with <paramref name="id"/> exists.
	/// </summary>
	Task<OneOf<WikiPage, NotFound>> SetMetadataAsync(
		string id,
		string? category,
		IReadOnlyList<string> tags,
		bool published);

	/// <summary>
	/// Returns the <em>source-locale</em> revision history for a page, ordered by revision number
	/// descending, with skip/take pagination. Translation revisions are a separate stream — see
	/// <see cref="GetRevisionsForLocaleAsync"/>.
	/// </summary>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20);

	/// <summary>
	/// Returns a specific <em>source-locale</em> revision snapshot for a page.
	/// Returns <c>NotFound</c> if no matching revision exists.
	/// </summary>
	/// <remarks>
	/// The source-stream filter is not cosmetic. Translation revisions restart numbering at 1 and share
	/// <c>PageId</c>, so without it <c>GetRevisionAsync(pageId, 1)</c> could return a translation's body —
	/// and its callers are the two rollback paths, which write the returned Markdown straight back onto the
	/// source page. That would restore French prose over an English page.
	/// </remarks>
	Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber);

	/// <summary>
	/// Lists every translation of a page as a bodyless summary, including unpublished drafts.
	/// Visibility filtering is the caller's responsibility — see <c>IWikiLocalizationService</c>.
	/// </summary>
	Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId);

	/// <summary>
	/// Retrieves one translation by its <c>(pageId, locale)</c> identity. <paramref name="locale"/> is
	/// matched case-insensitively after normalisation.
	/// Returns <c>NotFound</c> when no translation exists for that locale.
	/// </summary>
	Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale);

	/// <summary>
	/// Creates or updates a translation. Mirrors <see cref="UpdateAsync"/>: bumps the per-locale
	/// <c>RevisionNumber</c>, writes a <see cref="WikiRevision"/> carrying the locale, and re-renders
	/// HTML and plain text through the same <c>WikiMarkdigPipeline</c>.
	/// Returns <c>Error&lt;string&gt;</c> when the page does not exist, when
	/// <paramref name="locale"/> is unparseable, when it would shadow the page's own
	/// <c>SourceLocale</c>, when a concurrent write loses the unique-index race, or when
	/// <paramref name="expectedRevisionNumber"/> does not match what is stored.
	/// </summary>
	/// <param name="expectedRevisionNumber">
	/// The <c>RevisionNumber</c> the caller loaded, making this a compare-and-swap. The update applies only
	/// if the stored value still matches, and the revision append happens in the same transaction as the row
	/// update (or the update is made conditional and "zero rows affected" is the conflict signal).
	/// <para>
	/// <see langword="null"/> means <em>create-only</em>: an existing translation is an
	/// <c>Error&lt;string&gt;</c> rather than a blind overwrite.
	/// </para>
	/// <para>
	/// A conflict is <b>never</b> retried automatically. Retrying re-applies the loser's stale markdown on
	/// top of the winner's, which is exactly the data loss this parameter exists to prevent — the editor
	/// reloads and the human decides. The one automatic retry in this contract belongs to the insert race on
	/// <c>(pageId, locale)</c>, where no content can be lost.
	/// </para>
	/// </param>
	Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
		string pageId,
		string locale,
		string title,
		string markdown,
		string editorDbref,
		string? editSummary,
		bool published,
		int? expectedRevisionNumber);

	/// <summary>
	/// Deletes one translation and its revision stream, leaving the page and every other translation
	/// alone. Deleting the last translation is allowed.
	/// Returns <c>None</c> on success; <c>NotFound</c> when that locale has no translation.
	/// </summary>
	Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref);

	/// <summary>
	/// Returns the revision history for one <c>(pageId, locale)</c> stream, newest first.
	/// Pass <see cref="string.Empty"/> for the source-locale stream, which is what
	/// <see cref="GetRevisionsAsync"/> returns.
	/// </summary>
	/// <remarks>
	/// A distinct name rather than an overload of <see cref="GetRevisionsAsync"/>: an overload differing
	/// only by an inserted <c>string</c> invites a silent mis-bind at a call site that passes positional
	/// ints, and the compiler would not complain.
	/// </remarks>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take);
}
