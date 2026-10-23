using SharpMUSH.Library.Models.Wiki.Semantics;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Queries.Database.Wiki;
using SharpMUSH.Library;

namespace SharpMUSH.Implementation.Handlers.Database.Wiki;

public sealed class GetWikiPageBySlugQueryHandler(IWikiStore store) : IQueryHandler<GetWikiPageBySlugQuery, Found<WikiPage>>
{
	public ValueTask<Found<WikiPage>> Handle(GetWikiPageBySlugQuery query, CancellationToken cancellationToken)
		=> new(store.GetPageBySlugAsync(query.Ns, query.Slug));
}

public sealed class GetWikiPageByIdQueryHandler(IWikiStore store) : IQueryHandler<GetWikiPageByIdQuery, Found<WikiPage>>
{
	public ValueTask<Found<WikiPage>> Handle(GetWikiPageByIdQuery query, CancellationToken cancellationToken)
		=> new(store.GetPageByIdAsync(query.Id));
}

public sealed class GetRecentWikiPagesQueryHandler(IWikiStore store) : IQueryHandler<GetRecentWikiPagesQuery, IReadOnlyList<WikiPage>>
{
	public ValueTask<IReadOnlyList<WikiPage>> Handle(GetRecentWikiPagesQuery query, CancellationToken cancellationToken)
		=> new(store.GetRecentPagesAsync(query.Count, query.Visibility));
}

public sealed class GetWikiPagesQueryHandler(IWikiStore store) : IQueryHandler<GetWikiPagesQuery, IReadOnlyList<WikiPage>>
{
	public ValueTask<IReadOnlyList<WikiPage>> Handle(GetWikiPagesQuery query, CancellationToken cancellationToken)
		=> new(store.GetPagesAsync(query.Ns, query.Skip, query.Take, query.Visibility));
}

public sealed class CountWikiPagesQueryHandler(IWikiStore store) : IQueryHandler<CountWikiPagesQuery, int>
{
	public ValueTask<int> Handle(CountWikiPagesQuery query, CancellationToken cancellationToken)
		=> new(store.CountPagesAsync(query.Ns, query.Visibility));
}

public sealed class CountWikiPagesByStateQueryHandler(IWikiStore store) : IQueryHandler<CountWikiPagesByStateQuery, WikiPageCounts>
{
	public ValueTask<WikiPageCounts> Handle(CountWikiPagesByStateQuery query, CancellationToken cancellationToken)
		=> new(store.CountPagesByStateAsync(query.Visibility));
}

public sealed class CountWikiPagesByCategoryQueryHandler(IWikiStore store) : IQueryHandler<CountWikiPagesByCategoryQuery, IReadOnlyDictionary<string, int>>
{
	public ValueTask<IReadOnlyDictionary<string, int>> Handle(CountWikiPagesByCategoryQuery query, CancellationToken cancellationToken)
		=> new(store.CountPagesByCategoryAsync(query.Visibility));
}

public sealed class CountWikiPagesInCategoryQueryHandler(IWikiStore store) : IQueryHandler<CountWikiPagesInCategoryQuery, int>
{
	public ValueTask<int> Handle(CountWikiPagesInCategoryQuery query, CancellationToken cancellationToken)
		=> new(store.CountPagesInCategoryAsync(query.Category, query.Visibility));
}

public sealed class ScanWikiPagesQueryHandler(IWikiStore store) : IQueryHandler<ScanWikiPagesQuery, WikiPageScanBatch>
{
	public ValueTask<WikiPageScanBatch> Handle(ScanWikiPagesQuery query, CancellationToken cancellationToken)
		=> new(store.ScanPagesAsync(query.Visibility, query.Position, query.Take));
}

public sealed class GetWikiPagesByCategoryQueryHandler(IWikiStore store) : IQueryHandler<GetWikiPagesByCategoryQuery, IReadOnlyList<WikiPage>>
{
	public ValueTask<IReadOnlyList<WikiPage>> Handle(GetWikiPagesByCategoryQuery query, CancellationToken cancellationToken)
		=> new(store.GetPagesByCategoryAsync(query.Category, query.Skip, query.Take, query.Visibility));
}

public sealed class GetWikiRequirementsQueryHandler(IWikiStore store) : IQueryHandler<GetWikiRequirementsQuery, IReadOnlyList<WikiRequirementSet>>
{
	public ValueTask<IReadOnlyList<WikiRequirementSet>> Handle(GetWikiRequirementsQuery query, CancellationToken cancellationToken)
		=> new(store.GetRequirementsAsync());
}

public sealed class GetPinnedWikiCategoriesQueryHandler(IWikiStore store) : IQueryHandler<GetPinnedWikiCategoriesQuery, IReadOnlyList<string>>
{
	public ValueTask<IReadOnlyList<string>> Handle(GetPinnedWikiCategoriesQuery query, CancellationToken cancellationToken)
		=> new(store.GetPinnedCategoriesAsync());
}

public sealed class GetWikiFactSchemaQueryHandler(IWikiStore store) : IQueryHandler<GetWikiFactSchemaQuery, SemanticSchema>
{
	public ValueTask<SemanticSchema> Handle(GetWikiFactSchemaQuery query, CancellationToken cancellationToken)
		=> new(store.GetFactSchemaAsync());
}

public sealed class GetWikiFactIndexStatusQueryHandler(IWikiStore store) : IQueryHandler<GetWikiFactIndexStatusQuery, WikiFactIndexStatus>
{
	public ValueTask<WikiFactIndexStatus> Handle(GetWikiFactIndexStatusQuery query, CancellationToken cancellationToken)
		=> new(store.GetFactIndexStatusAsync());
}

public sealed class QueryWikiFactsQueryHandler(IWikiStore store) : IQueryHandler<QueryWikiFactsQuery, WikiFactQueryResult>
{
	public ValueTask<WikiFactQueryResult> Handle(QueryWikiFactsQuery query, CancellationToken cancellationToken)
		=> new(store.QueryFactsAsync(query.Query, query.Visibility));
}

public sealed class GetWikiRevisionsQueryHandler(IWikiStore store) : IQueryHandler<GetWikiRevisionsQuery, IReadOnlyList<WikiRevision>>
{
	public ValueTask<IReadOnlyList<WikiRevision>> Handle(GetWikiRevisionsQuery query, CancellationToken cancellationToken)
		=> new(store.GetRevisionsAsync(query.PageId, query.Locale, query.Skip, query.Take));
}

public sealed class GetWikiRevisionsBeforeQueryHandler(IWikiStore store) : IQueryHandler<GetWikiRevisionsBeforeQuery, IReadOnlyList<WikiRevision>>
{
	public ValueTask<IReadOnlyList<WikiRevision>> Handle(GetWikiRevisionsBeforeQuery query, CancellationToken cancellationToken)
		=> new(store.GetRevisionsBeforeAsync(query.PageId, query.Locale, query.BeforeRevisionNumber, query.Take));
}

public sealed class GetWikiRevisionQueryHandler(IWikiStore store) : IQueryHandler<GetWikiRevisionQuery, Found<WikiRevision>>
{
	public ValueTask<Found<WikiRevision>> Handle(GetWikiRevisionQuery query, CancellationToken cancellationToken)
		=> new(store.GetRevisionAsync(query.PageId, query.Locale, query.RevisionNumber));
}

public sealed class GetWikiTranslationSummariesQueryHandler(IWikiStore store) : IQueryHandler<GetWikiTranslationSummariesQuery, IReadOnlyList<WikiTranslationSummary>>
{
	public ValueTask<IReadOnlyList<WikiTranslationSummary>> Handle(GetWikiTranslationSummariesQuery query, CancellationToken cancellationToken)
		=> new(store.GetTranslationSummariesAsync(query.PageId));
}

public sealed class GetWikiTranslationsQueryHandler(IWikiStore store) : IQueryHandler<GetWikiTranslationsQuery, IReadOnlyList<WikiTranslation>>
{
	public ValueTask<IReadOnlyList<WikiTranslation>> Handle(GetWikiTranslationsQuery query, CancellationToken cancellationToken)
		=> new(store.GetTranslationsAsync(query.Skip, query.Take));
}

public sealed class GetWikiTranslationQueryHandler(IWikiStore store) : IQueryHandler<GetWikiTranslationQuery, Found<WikiTranslation>>
{
	public ValueTask<Found<WikiTranslation>> Handle(GetWikiTranslationQuery query, CancellationToken cancellationToken)
		=> new(store.GetTranslationAsync(query.PageId, query.Locale));
}
