namespace SharpMUSH.Client.Models;

/// <summary>
/// A bounded page of summaries and the exact count of authorized matching pages, with the server's tokens for the pages
/// either side (null at the start and at the end).
/// </summary>
public sealed record WikiPageBatch(IReadOnlyList<WikiPageSummary> Pages, int Total, string? Previous = null, string? Next = null);
