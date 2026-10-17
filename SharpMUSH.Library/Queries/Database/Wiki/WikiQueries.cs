using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Queries.Database.Wiki;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetPageBySlugAsync"/>. Uncached.</summary>
public sealed record GetWikiPageBySlugQuery(string Ns, string Slug) : IQuery<Found<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetPageByIdAsync"/>. Uncached.</summary>
public sealed record GetWikiPageByIdQuery(string Id) : IQuery<Found<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetRecentPagesAsync"/>. Uncached.</summary>
public sealed record GetRecentWikiPagesQuery(int Count, WikiVisibility Visibility) : IQuery<IReadOnlyList<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetPagesAsync"/>. Uncached.</summary>
public sealed record GetWikiPagesQuery(string? Ns, int Skip, int Take, WikiVisibility Visibility) : IQuery<IReadOnlyList<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.CountPagesAsync"/>. Uncached.</summary>
public sealed record CountWikiPagesQuery(string? Ns, WikiVisibility Visibility) : IQuery<int>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.CountPagesByStateAsync"/>. Uncached.</summary>
public sealed record CountWikiPagesByStateQuery(WikiVisibility Visibility) : IQuery<WikiPageCounts>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.CountPagesByCategoryAsync"/>. Uncached.</summary>
public sealed record CountWikiPagesByCategoryQuery(WikiVisibility Visibility) : IQuery<IReadOnlyDictionary<string, int>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetPagesByCategoryAsync"/>. Uncached.</summary>
public sealed record GetWikiPagesByCategoryQuery(string Category, int Skip, int Take, WikiVisibility Visibility) : IQuery<IReadOnlyList<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetRequirementsAsync"/>. Requirements are cached by the tagged request policy.</summary>
public sealed record GetWikiRequirementsQuery() : IQuery<IReadOnlyList<WikiRequirementSet>>, ICacheable
{
	public string CacheKey => "wiki:requirements";
	public string[] CacheTags => [Definitions.CacheTags.WikiRequirements];
}

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetPinnedCategoriesAsync"/>. Uncached.</summary>
public sealed record GetPinnedWikiCategoriesQuery() : IQuery<IReadOnlyList<string>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetRevisionsAsync"/>. Uncached.</summary>
public sealed record GetWikiRevisionsQuery(string PageId, string Locale, int Skip, int Take) : IQuery<IReadOnlyList<WikiRevision>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetRevisionsBeforeAsync"/>. Uncached.</summary>
public sealed record GetWikiRevisionsBeforeQuery(string PageId, string Locale, int BeforeRevisionNumber, int Take) : IQuery<IReadOnlyList<WikiRevision>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetRevisionAsync"/>. Uncached.</summary>
public sealed record GetWikiRevisionQuery(string PageId, string Locale, int RevisionNumber) : IQuery<Found<WikiRevision>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetTranslationSummariesAsync"/>. Uncached.</summary>
public sealed record GetWikiTranslationSummariesQuery(string PageId) : IQuery<IReadOnlyList<WikiTranslationSummary>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetTranslationsAsync"/>. Uncached.</summary>
public sealed record GetWikiTranslationsQuery(int Skip, int Take) : IQuery<IReadOnlyList<WikiTranslation>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.GetTranslationAsync"/>. Uncached.</summary>
public sealed record GetWikiTranslationQuery(string PageId, string Locale) : IQuery<Found<WikiTranslation>>;
