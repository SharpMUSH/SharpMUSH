namespace SharpMUSH.Client.Shared.Dtos;

/// <summary>
/// Cursor-based pagination result.
/// </summary>
public record CursorPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore
);

/// <summary>
/// Offset-based pagination result.
/// </summary>
public record OffsetPage<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Offset,
    int Limit
);
