using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// CRUD service for wiki pages, their revision history and their translations. Implemented once, by
/// <see cref="WikiStoreService"/> over the database provider's <see cref="IWikiStore"/>.
/// </summary>
/// <remarks>
/// All methods that might not find a resource return <see cref="Found{T}"/> rather
/// than <c>null</c>.  Methods that can fail due to a conflict (e.g. duplicate slug) return
/// <see cref="Result{T}"/> where <c>Error.Value</c> is a human-readable message.
/// </remarks>
public interface IWikiService
{
	/// <summary>
	/// Retrieves a wiki page by its (namespace, slug) identity.
	/// <paramref name="slug"/> is normalised the same way <see cref="CreateAsync"/> derives it from
	/// a title (see <c>WikiHelpers.Slugify</c>), so callers may pass a display name such as
	/// <c>"Mannaz Byron"</c> and reach the page stored as <c>mannaz_byron</c>.
	/// Returns <c>NotFound</c> if no matching page exists.
	/// </summary>
	Task<Found<WikiPage>> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main);

	/// <summary>
	/// Retrieves a wiki page by its storage ID.
	/// Returns <c>NotFound</c> if no matching page exists.
	/// </summary>
	Task<Found<WikiPage>> GetByIdAsync(string id);

	/// <summary>
	/// Returns the most recently updated pages, ordered by <c>UpdatedAt</c> descending.
	/// </summary>
	/// <param name="visibility">The pages returned, applied before paging; null returns every page.</param>
	Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20, WikiVisibility? visibility = null);

	/// <summary>
	/// Lists pages within a given namespace, with skip/take pagination.
	/// </summary>
	/// <param name="visibility">The pages returned, applied before paging; null returns every page.</param>
	Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50, WikiVisibility? visibility = null);

	/// <summary>
	/// Lists ALL pages (optionally restricted to one namespace), ordered by
	/// namespace then slug, with skip/take pagination. Includes unpublished pages unless
	/// <paramref name="visibility"/> says otherwise.
	/// </summary>
	/// <param name="visibility">The pages returned, applied before paging; null returns every page.</param>
	Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null, WikiVisibility? visibility = null);

	/// <summary>
	/// Returns the count of pages <paramref name="visibility"/> admits (optionally restricted to one namespace).
	/// </summary>
	/// <param name="visibility">
	/// Which pages count. There is no default, and <paramref name="ns"/> lost its own default so that there
	/// cannot be one: a count rendered beside a filtered listing is a disclosure channel on its own —
	/// differencing "N page(s)" against the visible rows reveals how many hidden pages the window holds — so
	/// every call site has to state which population it is counting.
	/// </param>
	Task<int> CountPagesAsync(WikiNamespace? ns, WikiVisibility visibility);

	/// <summary>
	/// The pages <paramref name="visibility"/> admits, counted by state (published, draft). As with
	/// <see cref="CountPagesAsync"/>, <paramref name="visibility"/> has no default.
	/// </summary>
	Task<WikiPageCounts> CountPagesByStateAsync(WikiVisibility visibility);

	/// <summary>
	/// Lists the pages in a category, ordered by title: every page whose category list names it, in any
	/// namespace. A page in the <c>category</c> namespace is a
	/// subcategory. <paramref name="category"/> is keyed (<c>WikiHelpers.CategoryKey</c>), so a display
	/// name reaches the same list.
	/// </summary>
	/// <param name="visibility">The pages returned, applied before paging; null returns every page.</param>
	Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50, WikiVisibility? visibility = null);

	/// <summary>How many pages <paramref name="visibility"/> admits in each category, keyed by category key.</summary>
	Task<IReadOnlyDictionary<string, int>> CountPagesByCategoryAsync(WikiVisibility visibility);

	/// <summary>
	/// Gives each category in <paramref name="names"/> that has no page a page in the category namespace,
	/// titled with the name exactly as typed, so <c>Places of Note</c> keeps its capitals although its key is
	/// <c>places_of_note</c>. A category in <paramref name="alreadyFiled"/> (keys or names) is skipped: the
	/// page already carried it, and the name sent back for it is its key, not what someone typed. A category
	/// that has a page keeps that page and its title, and a name typed exactly as its key (<c>places</c>) makes
	/// none, since its key already gives it that label. Returns the pages created.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> NameCategoriesAsync(IEnumerable<string> names, IEnumerable<string> alreadyFiled,
		string authorDbref, string sourceLocale);

	/// <summary>
	/// Creates a new wiki page. The (namespace, slug) identity must be unique. Renders the Markdown to
	/// HTML and extracts plain text at creation time. <paramref name="categories"/> are the page's
	/// categories, keyed by <c>WikiHelpers.NormalizeCategories</c>, plus those <paramref name="ns"/> puts every page
	/// in (<c>WikiHelpers.NamespaceCategories</c>); they are not read from the text.
	/// <paramref name="sourceLocale"/> records the locale the body is authored in, canonicalised through
	/// <c>WikiHelpers.NormalizeLocale</c>. It is materialised once here and immutable thereafter — nothing
	/// re-derives it on read. Null or blank stores <see cref="string.Empty"/>, meaning "not yet stamped";
	/// the wiki-translations migration backfills those, and both real create paths supply
	/// <c>IWikiLocalizationService.DefaultLocale</c>.
	/// Returns <c>Error&lt;string&gt;</c> when a page with the same (namespace, slug) already
	/// exists, or when <paramref name="sourceLocale"/> is non-blank and not a recognised locale tag.
	/// </summary>
	Task<Result<WikiPage>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? sourceLocale = null,
		IEnumerable<string>? categories = null);

	/// <summary>
	/// Updates an existing page's Markdown content.  Increments the revision counter,
	/// saves a revision snapshot, and re-renders HTML / plain text. Categories are unchanged.
	/// Returns <c>NotFound</c> when no page with <paramref name="id"/> exists.
	/// </summary>
	Task<Found<WikiPage>> UpdateAsync(
		string id,
		string markdown,
		string editorDbref,
		string? editSummary = null);

	/// <summary>
	/// Deletes a wiki page, all its revisions, all its translations and those translations' revisions.
	/// Returns <c>None</c> if a page was found and deleted; <c>NotFound</c> if not found.
	/// </summary>
	Task<Found<None>> DeleteAsync(string id, string editorDbref);

	/// <summary>Every namespace, category and page requirement set (see <see cref="WikiRequirements"/>).</summary>
	Task<WikiRequirements> GetRequirementsAsync();

	/// <summary>
	/// Replaces what <paramref name="target"/> requires; actions with no permissions require nothing, and a
	/// set requiring nothing is removed. Permission names are lowercased and de-duplicated here; whether they
	/// name a permission is the caller's rule (<c>IWikiAccessService.SetRequirementsAsync</c>).
	/// Returns <c>NotFound</c> for a page target naming no page.
	/// </summary>
	Task<Found<None>> SetRequirementsAsync(WikiRuleTarget target, IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> required,
		string editorDbref);

	/// <summary>
	/// The categories pinned to the wiki home, as keys in key order. Only these get a card there; every
	/// category still has its page and its place in the sidebar. A new world pins <c>character</c>.
	/// </summary>
	Task<IReadOnlyList<string>> GetPinnedCategoriesAsync();

	/// <summary>
	/// Pins <paramref name="category"/> to the wiki home or takes it off. The category need not have a page
	/// or any member yet. Returns whether anything changed, or <c>Error</c> for a blank name. Who may do this
	/// is the caller's rule (wiki.admin).
	/// </summary>
	Task<Result<bool>> SetCategoryPinnedAsync(string category, bool pinned);

	/// <summary>
	/// Sets a page's categories and published flag. Does NOT create a revision — metadata changes are not
	/// content edits. Categories are keyed (<c>WikiHelpers.NormalizeCategories</c>): blanks dropped,
	/// duplicates merged, and the categories the page's namespace puts it in added
	/// (<c>WikiHelpers.NamespaceCategories</c>), so they cannot be removed.
	/// Returns the updated page, or <c>NotFound</c> when no page with <paramref name="id"/> exists.
	/// </summary>
	Task<Found<WikiPage>> SetMetadataAsync(string id, IEnumerable<string> categories, bool published);

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
	Task<Found<WikiRevision>> GetRevisionAsync(string pageId, int revisionNumber);

	/// <summary>
	/// Lists every translation of a page as a bodyless summary, including unpublished drafts.
	/// Visibility filtering is the caller's responsibility — see <c>IWikiLocalizationService</c>.
	/// </summary>
	Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId);

	/// <summary>
	/// Lists ALL translations with their bodies, ordered by page then locale, with skip/take pagination.
	/// Includes unpublished drafts — callers are responsible for visibility filtering, exactly as with
	/// <see cref="GetAllPagesAsync"/>.
	/// </summary>
	/// <remarks>
	/// Deliberately symmetric with <see cref="GetAllPagesAsync"/> and deliberately not a query: in-game
	/// search is an in-process scan, so making the translation stream readable in bulk is the whole of what
	/// each store has to provide. Bodies are included because that is what search matches on —
	/// <see cref="GetTranslationsAsync"/> returns bodyless summaries and cannot serve this.
	/// </remarks>
	Task<IReadOnlyList<WikiTranslation>> GetAllTranslationsAsync(int skip = 0, int take = 50);

	/// <summary>
	/// Retrieves one translation by its <c>(pageId, locale)</c> identity. <paramref name="locale"/> is
	/// matched case-insensitively after normalisation.
	/// Returns <c>NotFound</c> when no translation exists for that locale.
	/// </summary>
	Task<Found<WikiTranslation>> GetTranslationAsync(string pageId, string locale);

	/// <summary>
	/// Creates or updates a translation. Mirrors <see cref="UpdateAsync"/>: bumps the per-locale
	/// <c>RevisionNumber</c>, writes a <see cref="WikiRevision"/> carrying the locale, and re-renders
	/// HTML and plain text through the same <c>WikiMarkdigPipeline</c>.
	/// Returns <c>Error&lt;string&gt;</c> when the request itself is wrong — the page does not exist,
	/// <paramref name="locale"/> is unparseable, or it would shadow the page's own <c>SourceLocale</c> —
	/// and <see cref="WikiWriteConflict"/> when the request was fine but lost a race with another writer.
	/// The two are separate cases because they demand different answers from the caller: fix the request,
	/// versus reload and decide. Do not re-merge them into a phrased string — see the remarks on
	/// <see cref="WikiWriteConflict"/>.
	/// </summary>
	/// <param name="expectedRevisionNumber">
	/// The <c>RevisionNumber</c> the caller loaded, making this a compare-and-swap. The update applies only
	/// if the stored value still matches, and the revision append happens in the same transaction as the row
	/// update (or the update is made conditional and "zero rows affected" is the conflict signal).
	/// <para>
	/// <see langword="null"/> means <em>create-only</em>: an existing translation is
	/// <see cref="WikiWriteConflict.AlreadyExists"/> rather than a blind overwrite.
	/// </para>
	/// <para>
	/// A conflict is <b>never</b> retried automatically. Retrying re-applies the loser's stale markdown on
	/// top of the winner's, which is exactly the data loss this parameter exists to prevent — the editor
	/// reloads and the human decides. The one automatic retry in this contract belongs to the insert race on
	/// <c>(pageId, locale)</c>, where no content can be lost.
	/// </para>
	/// </param>
	Task<TranslationWriteResult> UpsertTranslationAsync(
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
	Task<Found<None>> DeleteTranslationAsync(string pageId, string locale, string editorDbref);

	/// <summary>
	/// Returns the revision history for one <c>(pageId, locale)</c> stream, newest first.
	/// Pass <see cref="string.Empty"/> for the source-locale stream, which is what
	/// <see cref="GetRevisionsAsync"/> returns. A tag that is not a recognised locale names no stream, and
	/// yields an empty list rather than the source stream.
	/// </summary>
	/// <remarks>
	/// A distinct name rather than an overload of <see cref="GetRevisionsAsync"/>: an overload differing
	/// only by an inserted <c>string</c> invites a silent mis-bind at a call site that passes positional
	/// ints, and the compiler would not complain.
	/// </remarks>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take);

	/// <summary>
	/// The cursor form of <see cref="GetRevisionsForLocaleAsync"/>: the revisions of one stream numbered
	/// below <paramref name="beforeRevisionNumber"/>, newest first, at most <paramref name="take"/>. Pass the
	/// last number of the previous page to get the next one.
	/// </summary>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsBeforeForLocaleAsync(string pageId, string locale, int beforeRevisionNumber, int take);

	/// <summary>
	/// Returns one revision snapshot from a single <c>(pageId, locale)</c> stream.
	/// Pass <see cref="string.Empty"/> for the source-locale stream, which is what
	/// <see cref="GetRevisionAsync"/> returns.
	/// Returns <c>NotFound</c> if that stream has no such revision number, or if <paramref name="locale"/> is
	/// not a recognised tag.
	/// </summary>
	/// <remarks>
	/// Revision numbering restarts at 1 in every locale, so <c>revisionNumber</c> alone does not identify a
	/// revision — <c>(locale, revisionNumber)</c> does. Without this method the history route can list a
	/// locale's stream while the per-revision route serves the source stream's row of the same number, and a
	/// diff of French against English renders as a plausible-looking rewrite rather than an error.
	/// <para>
	/// This is deliberately <em>not</em> what the rollback paths call: they must stay pinned to the source
	/// stream, which is why <see cref="GetRevisionAsync"/> keeps its narrower contract.
	/// </para>
	/// </remarks>
	Task<Found<WikiRevision>> GetRevisionForLocaleAsync(string pageId, string locale, int revisionNumber);
}
