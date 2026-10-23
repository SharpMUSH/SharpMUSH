using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// <c>@wiki/query &lt;condition&gt;[, &lt;condition&gt;...]</c> and <c>wikiquery()</c>: the pages whose facts meet every
/// condition (<see cref="WikiFactQueryText"/>).
/// </summary>
public static class QueryWiki
{
	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource"><c>/SOURCE</c>: list source-locale titles, skipping localization entirely.</param>
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		IWikiLocalizationService localization, INotifyService notifyService, string conditions, string? locale, bool forceSource)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var visibility = await WikiCommandHelper.VisibilityAsync(parser, executor);
		return await RunAsync(wikiService, WikiFactQueryText.Split(conditions), visibility) switch
		{
			WikiFactQueryPage found => await ToldAsync(WikiCommandHelper.PageListing($"{found.Pages.Count} page(s) where {conditions.Trim()}",
				await ListWiki.PageRowsAsync(localization, found.Pages, locale, forceSource))),
			WikiFactIndexBuilding building => await ToldAsync(MarkupText.Plain(
				$"WIKI: The fact index is building ({building.Status.Indexed} of {building.Status.Pages} pages); ask again when it is ready."),
				ErrorMessages.Returns.WikiIndexBuilding),
			Error<string> error => await ToldAsync(MarkupText.Plain($"WIKI: {error.Value}"), ErrorMessages.Returns.InvalidWikiQuery)
		};

		async ValueTask<MString> ToldAsync(MString told, string? returned = null)
		{
			await notifyService.Notify(executor, told, executor);
			return returned is null ? told : MarkupText.Plain(returned);
		}
	}

	/// <summary>Every page the typed conditions find, following each continuation, as one page with no next.</summary>
	public static async Task<WikiFactQueryResult> RunAsync(IWikiService wikiService, IEnumerable<string> conditions, WikiVisibility visibility)
		=> await WikiFactQueryText.ParseAsync(wikiService, await wikiService.GetFactSchemaAsync(), conditions, WikiFactQuery.MaxTake, visibility) switch
		{
			WikiFactQuery query => await AllAsync(wikiService, query, visibility),
			Error<string> error => error
		};

	private static async Task<WikiFactQueryResult> AllAsync(IWikiService wikiService, WikiFactQuery query, WikiVisibility visibility)
	{
		var pages = new List<WikiPage>();
		for (string? after = null; ;)
		{
			var result = await wikiService.QueryFactsAsync(query with { After = after }, visibility);
			if (result is not WikiFactQueryPage page) return result;
			pages.AddRange(page.Pages);
			if (page.Next is not { } next) return new WikiFactQueryPage(pages, null);
			after = next;
		}
	}
}
