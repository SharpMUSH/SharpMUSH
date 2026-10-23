using SharpMUSH.Library.Models.Wiki.Semantics;
using Mediator;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Queries.Database.Wiki;
using SharpMUSH.Library;

namespace SharpMUSH.Implementation.Services;

/// <summary>Transports normalized wiki storage requests through the engine data trunk.</summary>
/// <remarks>Register by concrete type only; handlers receive the provider-backed IWikiStore.</remarks>
public sealed class MediatorWikiStore(IMediator mediator) : IWikiStore
{
	public Task<Found<WikiPage>> GetPageBySlugAsync(string ns, string slug)
		=> mediator.Send(new GetWikiPageBySlugQuery(ns, slug)).AsTask();

	public Task<Found<WikiPage>> GetPageByIdAsync(string id)
		=> mediator.Send(new GetWikiPageByIdQuery(id)).AsTask();

	public Task<IReadOnlyList<WikiPage>> GetRecentPagesAsync(int count, WikiVisibility visibility)
		=> mediator.Send(new GetRecentWikiPagesQuery(count, visibility)).AsTask();

	public Task<IReadOnlyList<WikiPage>> GetPagesAsync(string? ns, int skip, int take, WikiVisibility visibility)
		=> mediator.Send(new GetWikiPagesQuery(ns, skip, take, visibility)).AsTask();

	public Task<int> CountPagesAsync(string? ns, WikiVisibility visibility)
		=> mediator.Send(new CountWikiPagesQuery(ns, visibility)).AsTask();

	public Task<WikiPageCounts> CountPagesByStateAsync(WikiVisibility visibility)
		=> mediator.Send(new CountWikiPagesByStateQuery(visibility)).AsTask();

	public Task<IReadOnlyDictionary<string, int>> CountPagesByCategoryAsync(WikiVisibility visibility)
		=> mediator.Send(new CountWikiPagesByCategoryQuery(visibility)).AsTask();

	public Task<int> CountPagesInCategoryAsync(string category, WikiVisibility visibility)
		=> mediator.Send(new CountWikiPagesInCategoryQuery(category, visibility)).AsTask();

	public Task<WikiPageScanBatch> ScanPagesAsync(WikiVisibility visibility, WikiPageScanPosition position, int take)
		=> mediator.Send(new ScanWikiPagesQuery(visibility, position, take)).AsTask();

	public Task<IReadOnlyList<WikiPage>> GetPagesByCategoryAsync(string category, int skip, int take, WikiVisibility visibility)
		=> mediator.Send(new GetWikiPagesByCategoryQuery(category, skip, take, visibility)).AsTask();

	public Task<Result<WikiPage>> CreatePageAsync(WikiPage page, string? editSummary = null)
		=> mediator.Send(new CreateWikiPageCommand(page, editSummary)).AsTask();

	public Task<Found<WikiPage>> UpdatePageBodyAsync(string id, WikiBody body, string editorDbref, string? editSummary, DateTimeOffset at)
		=> mediator.Send(new UpdateWikiPageBodyCommand(id, body, editorDbref, editSummary, at)).AsTask();

	public Task<Found<None>> DeletePageAsync(string id)
		=> mediator.Send(new DeleteWikiPageCommand(id)).AsTask();

	public Task<IReadOnlyList<WikiRequirementSet>> GetRequirementsAsync()
		=> mediator.Send(new GetWikiRequirementsQuery()).AsTask();

	public Task<Found<None>> SetRequirementsAsync(WikiRequirementSet set)
		=> mediator.Send(new SetWikiRequirementsCommand(set)).AsTask();

	public Task<Found<WikiPage>> SetPageTitleAsync(string id, string title)
		=> mediator.Send(new SetWikiPageTitleCommand(id, title)).AsTask();

	public Task<Found<WikiPage>> SetPageMetadataAsync(string id, IReadOnlyList<string> categories, bool published)
		=> mediator.Send(new SetWikiPageMetadataCommand(id, categories, published)).AsTask();

	public Task<FoundResult<WikiPage>> SetPageFactsAsync(string id, IReadOnlyList<WikiPageFact> facts)
		=> mediator.Send(new SetWikiPageFactsCommand(id, facts)).AsTask();

	public Task<SemanticSchema> GetFactSchemaAsync()
		=> mediator.Send(new GetWikiFactSchemaQuery()).AsTask();

	public Task<Result<PropertyDefinition>> SetFactPropertyAsync(PropertyDefinition property)
		=> mediator.Send(new SetWikiFactPropertyCommand(property)).AsTask();

	public Task<Found<None>> DeleteFactPropertyAsync(string id)
		=> mediator.Send(new DeleteWikiFactPropertyCommand(id)).AsTask();

	public Task<WikiFactIndexStatus> GetFactIndexStatusAsync()
		=> mediator.Send(new GetWikiFactIndexStatusQuery()).AsTask();

	public Task<WikiFactIndexStatus> BuildFactIndexBatchAsync(int batchSize)
		=> mediator.Send(new BuildWikiFactIndexBatchCommand(batchSize)).AsTask();

	public Task<WikiFactIndexStatus> RebuildFactIndexAsync()
		=> mediator.Send(new RebuildWikiFactIndexCommand()).AsTask();

	public Task<WikiFactQueryResult> QueryFactsAsync(WikiFactQuery query, WikiVisibility visibility)
		=> mediator.Send(new QueryWikiFactsQuery(query, visibility)).AsTask();

	public Task<IReadOnlyList<string>> GetPinnedCategoriesAsync()
		=> mediator.Send(new GetPinnedWikiCategoriesQuery()).AsTask();

	public Task<bool> SetCategoryPinnedAsync(string category, bool pinned)
		=> mediator.Send(new SetWikiCategoryPinnedCommand(category, pinned)).AsTask();

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, string locale, int skip, int take)
		=> mediator.Send(new GetWikiRevisionsQuery(pageId, locale, skip, take)).AsTask();

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsBeforeAsync(string pageId, string locale, int beforeRevisionNumber, int take)
		=> mediator.Send(new GetWikiRevisionsBeforeQuery(pageId, locale, beforeRevisionNumber, take)).AsTask();

	public Task<Found<WikiRevision>> GetRevisionAsync(string pageId, string locale, int revisionNumber)
		=> mediator.Send(new GetWikiRevisionQuery(pageId, locale, revisionNumber)).AsTask();

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationSummariesAsync(string pageId)
		=> mediator.Send(new GetWikiTranslationSummariesQuery(pageId)).AsTask();

	public Task<IReadOnlyList<WikiTranslation>> GetTranslationsAsync(int skip, int take)
		=> mediator.Send(new GetWikiTranslationsQuery(skip, take)).AsTask();

	public Task<Found<WikiTranslation>> GetTranslationAsync(string pageId, string locale)
		=> mediator.Send(new GetWikiTranslationQuery(pageId, locale)).AsTask();

	public Task<TranslationWriteResult> WriteTranslationAsync(string pageId, string locale, string title, WikiBody body, string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber, DateTimeOffset at)
		=> mediator.Send(new WriteWikiTranslationCommand(pageId, locale, title, body, editorDbref, editSummary, published, expectedRevisionNumber, at)).AsTask();

	public Task<Found<None>> DeleteTranslationAsync(string pageId, string locale)
		=> mediator.Send(new DeleteWikiTranslationCommand(pageId, locale)).AsTask();
}
