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
	// ── Read operations ──────────────────────────────────────────────────────

	/// <summary>
	/// Retrieves a wiki page by its slug and namespace.
	/// Returns <c>NotFound</c> if no matching page exists.
	/// </summary>
	Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main);

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

	// ── Write operations ──────────────────────────────────────────────────────

	/// <summary>
	/// Creates a new wiki page.  The slug must be unique within the namespace.
	/// Renders the Markdown to HTML and extracts plain text at creation time.
	/// Returns <c>Error&lt;string&gt;</c> when a page with the same slug already exists in the given namespace.
	/// </summary>
	Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main);

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
	/// Deletes a wiki page and all its revisions.
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

	// ── Revision operations ───────────────────────────────────────────────────

	/// <summary>
	/// Returns the revision history for a page, ordered by revision number descending,
	/// with skip/take pagination.
	/// </summary>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20);

	/// <summary>
	/// Returns a specific revision snapshot for a page.
	/// Returns <c>NotFound</c> if no matching revision exists.
	/// </summary>
	Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber);
}
