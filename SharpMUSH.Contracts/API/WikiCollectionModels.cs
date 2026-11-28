namespace SharpMUSH.Library.API;

/// <summary>A visible package collection. IDs, not titles, are the durable browsing identity.</summary>
public sealed record WikiCollectionSummaryDto(string Id, string Name, int Pages);

/// <summary>Complete authorized navigation for one collection; no page bodies or registry secrets.</summary>
public sealed record WikiNavigationDto(string Collection, int Total,
	IReadOnlyList<WikiCollectionSummaryDto> Collections, IReadOnlyList<WikiCategorySummaryDto> Categories)
{
	/// <summary>The currently authorized localized entrance, or null without exposing why it is unavailable.</summary>
	public WikiLandingPageDto? LandingPage { get; init; }

	/// <summary>The keys of the pinned categories in the order the wiki home shows their cards.</summary>
	public IReadOnlyList<string> PinnedOrder { get; init; } = [];
}

/// <summary>Compact authorized entrance metadata; contains no article body or private requirement details.</summary>
public sealed record WikiLandingPageDto(string PageId, string Namespace, string Slug, string Title, string Locale, bool IsFallback);
