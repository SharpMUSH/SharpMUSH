namespace SharpMUSH.Library.API.Pagination;

/// <summary>
/// A single page of results from an offset/limit-based query.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>Items in this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Zero-based index of this page.</summary>
    public int Page { get; init; }

    /// <summary>Maximum number of items per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of items across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    /// <summary>Whether there is a subsequent page.</summary>
    public bool HasNextPage => Page < TotalPages - 1;

    /// <summary>Whether there is a prior page.</summary>
    public bool HasPreviousPage => Page > 0;
}

/// <summary>
/// Helpers for offset/limit-based pagination over in-memory sequences.
/// </summary>
public static class OffsetPaginationHelper
{
    /// <summary>
    /// Slices <paramref name="source"/> into an offset page.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The full ordered sequence to paginate. Enumerated once.</param>
    /// <param name="page">Zero-based page index. Clamped to non-negative.</param>
    /// <param name="pageSize">Items per page. Clamped to 1–200.</param>
    public static PagedResult<T> Paginate<T>(
        IEnumerable<T> source,
        int page = 0,
        int pageSize = 20)
    {
        page = Math.Max(0, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var list = source.ToList();
        var total = list.Count;
        var items = list.Skip(page * pageSize).Take(pageSize).ToList();

        return new PagedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    /// <summary>
    /// Convenience overload for pre-counted queries (e.g. when total is obtained from a COUNT(*) query).
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The already-sliced items for this page.</param>
    /// <param name="totalCount">Total number of items across all pages.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Items per page.</param>
    public static PagedResult<T> FromSlice<T>(
        IReadOnlyList<T> items,
        int totalCount,
        int page,
        int pageSize) =>
        new()
        {
            Items = items,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 200),
            TotalCount = totalCount,
        };
}
