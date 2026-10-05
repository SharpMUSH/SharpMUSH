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
	/// <summary>The categories the page is in, as keys.</summary>
	public IReadOnlyList<string> Categories { get; init; } = [];

	/// <summary>When false, the page is a draft hidden from anonymous visitors.</summary>
	public bool Published { get; init; } = true;

	/// <summary>When true, the page carries permission requirements of its own.</summary>
	public bool IsRestricted { get; init; }

	/// <summary>The locale this row's title came from.</summary>
	public string Locale { get; init; } = string.Empty;

	/// <summary>True when this row's title is a fallback rather than the requested language.</summary>
	public bool IsFallback { get; init; }

	/// <summary>The first image in the page's Markdown (a row thumbnail or a category cover); null when there is none.</summary>
	public string? Image { get; init; }

	/// <summary>The name of the player who last edited the page; null when the server could not name one.</summary>
	public string? LastEditedBy { get; init; }
}
