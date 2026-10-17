namespace SharpMUSH.Client.Models;

/// <summary>A bounded page of summaries and the exact count of authorized matching pages.</summary>
public sealed record WikiPageBatch(IReadOnlyList<WikiPageSummary> Pages, int Total);
