using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// CRUD service for wiki pages and their revision history.
/// The in-memory implementation is for testing and development;
/// an ArangoDB implementation will follow in a later phase.
/// </summary>
public interface IWikiService
{
	// ── Read operations ──────────────────────────────────────────────────────

	/// <summary>
	/// Retrieves a wiki page by its slug and namespace.
	/// Returns <c>null</c> if no matching page exists.
	/// </summary>
	Task<WikiPage?> GetBySlugAsync(string slug, WikiNamespace ns = WikiNamespace.Main);

	/// <summary>
	/// Retrieves a wiki page by its storage ID.
	/// Returns <c>null</c> if no matching page exists.
	/// </summary>
	Task<WikiPage?> GetByIdAsync(string id);

	/// <summary>
	/// Returns the most recently updated pages, ordered by <c>UpdatedAt</c> descending.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20);

	/// <summary>
	/// Lists pages within a given namespace, with skip/take pagination.
	/// </summary>
	Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50);

	// ── Write operations ──────────────────────────────────────────────────────

	/// <summary>
	/// Creates a new wiki page.  The slug must be unique within the namespace.
	/// Renders the Markdown to HTML and extracts plain text at creation time.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown when a page with the same slug already exists in the given namespace.
	/// </exception>
	Task<WikiPage> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main);

	/// <summary>
	/// Updates an existing page's Markdown content.  Increments the revision counter,
	/// saves a revision snapshot, and re-renders HTML / plain text.
	/// </summary>
	/// <exception cref="KeyNotFoundException">Thrown when no page with <paramref name="id"/> exists.</exception>
	Task<WikiPage> UpdateAsync(
		string id,
		string markdown,
		string editorDbref,
		string? editSummary = null);

	/// <summary>
	/// Deletes a wiki page and all its revisions.
	/// Returns <c>true</c> if a page was found and deleted; <c>false</c> if not found.
	/// </summary>
	Task<bool> DeleteAsync(string id, string editorDbref);

	/// <summary>
	/// Sets the protection flag on a page.
	/// Protected pages can only be edited by admin-level users.
	/// </summary>
	/// <exception cref="KeyNotFoundException">Thrown when no page with <paramref name="id"/> exists.</exception>
	Task SetProtectionAsync(string id, bool isProtected);

	// ── Revision operations ───────────────────────────────────────────────────

	/// <summary>
	/// Returns the revision history for a page, ordered by revision number descending,
	/// with skip/take pagination.
	/// </summary>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20);

	/// <summary>
	/// Returns a specific revision snapshot for a page.
	/// Returns <c>null</c> if no matching revision exists.
	/// </summary>
	Task<WikiRevision?> GetRevisionAsync(string pageId, int revisionNumber);
}
