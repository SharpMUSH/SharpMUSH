using SharpMUSH.Client.Shared.Dtos;

namespace SharpMUSH.Client.Shared.Services;

/// <summary>
/// Wiki page operations (both REST and real-time).
/// </summary>
public interface IWikiService
{
    /// <summary>
    /// Get a page by slug.
    /// </summary>
    Task<WikiPageDto?> GetPageAsync(string slug);

    /// <summary>
    /// List pages in a category with pagination.
    /// </summary>
    Task<OffsetPage<WikiPageSummaryDto>> ListPagesAsync(string category, int offset = 0, int limit = 20);

    /// <summary>
    /// Create or update a page.
    /// </summary>
    Task<WikiPageDto> EditPageAsync(string slug, WikiEditRequest request);

    /// <summary>
    /// Delete a page.
    /// </summary>
    Task DeletePageAsync(string slug);

    /// <summary>
    /// Search pages by title/content.
    /// </summary>
    Task<IReadOnlyList<WikiPageSummaryDto>> SearchAsync(string query);

    /// <summary>
    /// Get page edit history (for real-time collab, if enabled).
    /// </summary>
    Task<IReadOnlyList<(DateTime Modified, string By)>> GetHistoryAsync(string slug);
}
