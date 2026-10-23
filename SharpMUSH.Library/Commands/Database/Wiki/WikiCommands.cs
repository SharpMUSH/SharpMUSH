using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Commands.Database.Wiki;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.CreatePageAsync"/>.</summary>
public sealed record CreateWikiPageCommand(WikiPage Page, string? EditSummary) : ICommand<Result<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.UpdatePageBodyAsync"/>.</summary>
public sealed record UpdateWikiPageBodyCommand(string Id, WikiBody Body, string EditorDbref, string? EditSummary, DateTimeOffset At) : ICommand<Found<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.DeletePageAsync"/>.</summary>
public sealed record DeleteWikiPageCommand(string Id) : ICommand<Found<None>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.WikiRequirements];
}

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.SetRequirementsAsync"/>.</summary>
public sealed record SetWikiRequirementsCommand(WikiRequirementSet Set) : ICommand<Found<None>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.WikiRequirements];
}

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.SetPageTitleAsync"/>.</summary>
public sealed record SetWikiPageTitleCommand(string Id, string Title) : ICommand<Found<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.SetPageMetadataAsync"/>.</summary>
public sealed record SetWikiPageMetadataCommand(string Id, IReadOnlyList<string> Categories, bool Published) : ICommand<Found<WikiPage>>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.SetPageFactsAsync"/>.</summary>
public sealed record SetWikiPageFactsCommand(string Id, IReadOnlyList<WikiPageFact> Facts) : ICommand<FoundResult<WikiPage>>;

/// <summary>Forwards to <see cref="IWikiStore.BuildFactIndexBatchAsync"/>.</summary>
public sealed record BuildWikiFactIndexBatchCommand(int BatchSize) : ICommand<WikiFactIndexStatus>;

/// <summary>Forwards to <see cref="IWikiStore.RebuildFactIndexAsync"/>.</summary>
public sealed record RebuildWikiFactIndexCommand() : ICommand<WikiFactIndexStatus>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.SetCategoryPinnedAsync"/>.</summary>
public sealed record SetWikiCategoryPinnedCommand(string Category, bool Pinned) : ICommand<bool>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.WriteTranslationAsync"/>.</summary>
public sealed record WriteWikiTranslationCommand(string PageId, string Locale, string Title, WikiBody Body, string EditorDbref, string? EditSummary, bool Published, int? ExpectedRevisionNumber, DateTimeOffset At) : ICommand<TranslationWriteResult>;

/// <summary>Forwards normalized storage data to <see cref="IWikiStore.DeleteTranslationAsync"/>.</summary>
public sealed record DeleteWikiTranslationCommand(string PageId, string Locale) : ICommand<Found<None>>;
