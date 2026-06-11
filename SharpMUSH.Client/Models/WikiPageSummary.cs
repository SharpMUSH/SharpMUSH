namespace SharpMUSH.Client.Models;

/// <summary>
/// Lightweight page listing entry used by the recent-changes list, namespace browser
/// and the wiki admin grid. Carries enough to render a link row plus the metadata
/// columns without the full markdown/HTML payload.
/// </summary>
public record WikiPageSummary(
	string Slug,
	string Title,
	string Namespace,
	DateTimeOffset UpdatedAt,
	int RevisionNumber)
{
	/// <summary>Optional category grouping (lower-case), or null when uncategorised.</summary>
	public string? Category { get; init; }

	/// <summary>Searchable tags (lower-case, de-duplicated).</summary>
	public IReadOnlyList<string> Tags { get; init; } = [];

	/// <summary>When false, the page is a draft hidden from anonymous visitors.</summary>
	public bool Published { get; init; } = true;

	/// <summary>When true, only Wizard-level users may edit the page.</summary>
	public bool IsProtected { get; init; }
}
