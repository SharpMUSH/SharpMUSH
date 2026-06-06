namespace SharpMUSH.Library.API.Pagination;

/// <summary>
/// A single page of results returned by cursor-based pagination.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record CursorPage<T>
{
    /// <summary>The items in this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// An opaque cursor pointing to the last item on this page.
    /// Pass this as <c>after</c> on the next request to fetch the subsequent page.
    /// <see langword="null"/> when there are no more pages.
    /// </summary>
    public string? NextCursor { get; init; }

    /// <summary>
    /// An opaque cursor pointing to the first item on this page.
    /// Pass this as <c>before</c> on the next request to fetch the previous page.
    /// <see langword="null"/> when there is no previous page.
    /// </summary>
    public string? PreviousCursor { get; init; }

    /// <summary>Whether there is a subsequent page available.</summary>
    public bool HasNextPage => NextCursor is not null;

    /// <summary>Whether there is a prior page available.</summary>
    public bool HasPreviousPage => PreviousCursor is not null;
}

/// <summary>
/// Helpers for building cursor-based pages from in-memory collections.
/// The cursor is a Base64-encoded opaque token that encodes the sort-key
/// of the boundary item so the implementation can slice the sequence without
/// transmitting raw database identifiers to the client.
/// </summary>
public static class CursorPaginationHelper
{
    /// <summary>
    /// Slices <paramref name="source"/> into a cursor page.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <typeparam name="TKey">The comparable sort key type.</typeparam>
    /// <param name="source">The full ordered sequence to paginate.</param>
    /// <param name="keySelector">Extracts the sort key used to build/decode cursors.</param>
    /// <param name="pageSize">Maximum items per page. Clamped to 1–200.</param>
    /// <param name="after">Opaque cursor returned by a prior <c>NextCursor</c>. When provided, items up to and including the boundary are skipped.</param>
    /// <param name="before">Opaque cursor returned by a prior <c>PreviousCursor</c>. When provided, only items before the boundary are included.</param>
    public static CursorPage<T> Paginate<T, TKey>(
        IEnumerable<T> source,
        Func<T, TKey> keySelector,
        int pageSize = 20,
        string? after = null,
        string? before = null)
        where TKey : IComparable<TKey>
    {
        pageSize = Math.Clamp(pageSize, 1, 200);

        var items = source.ToList();

        if (after is not null)
        {
            var afterKey = DecodeCursor<TKey>(after);
            if (afterKey is not null)
                items = items.SkipWhile(i => keySelector(i).CompareTo(afterKey) <= 0).ToList();
        }

        if (before is not null)
        {
            var beforeKey = DecodeCursor<TKey>(before);
            if (beforeKey is not null)
                items = items.TakeWhile(i => keySelector(i).CompareTo(beforeKey) < 0).ToList();
        }

        // Request one extra item to detect whether a next page exists
        var window = items.Take(pageSize + 1).ToList();
        var hasNext = window.Count > pageSize;
        var page = window.Take(pageSize).ToList();

        return new CursorPage<T>
        {
            Items = page,
            NextCursor = hasNext ? EncodeCursor(keySelector(page[^1])) : null,
            PreviousCursor = after is not null && page.Count > 0
                ? EncodeCursor(keySelector(page[0]))
                : null,
        };
    }

    /// <summary>Encodes a sort key as an opaque Base64 cursor token.</summary>
    public static string EncodeCursor<TKey>(TKey key) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key?.ToString() ?? string.Empty));

    /// <summary>Decodes a cursor token back to its sort key. Returns <see langword="null"/> on failure.</summary>
    public static TKey? DecodeCursor<TKey>(string cursor)
    {
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return (TKey?)Convert.ChangeType(raw, typeof(TKey));
        }
        catch
        {
            return default;
        }
    }
}
