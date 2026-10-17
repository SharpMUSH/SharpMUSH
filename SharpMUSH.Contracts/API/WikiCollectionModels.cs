namespace SharpMUSH.Library.API;

/// <summary>A visible package collection. IDs, not titles, are the durable browsing identity.</summary>
public sealed record WikiCollectionSummaryDto(string Id, string Name, int Pages);

/// <summary>Complete authorized navigation for one collection; no page bodies or registry secrets.</summary>
public sealed record WikiNavigationDto(string Collection, int Total,
	IReadOnlyList<WikiCollectionSummaryDto> Collections, IReadOnlyList<WikiCategorySummaryDto> Categories);
