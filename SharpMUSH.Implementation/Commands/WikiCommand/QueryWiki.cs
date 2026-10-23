using Mediator;
using SharpMUSH.Library.API;
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
	/// <summary>The most pages <c>@wiki/query</c> lists, as <c>@wiki/list</c>.</summary>
	public const int MaxListed = 100;

	/// <summary>The most pages <c>wikiquery()</c> returns, as <c>wikilist()</c>.</summary>
	public const int MaxReturned = 1000;

	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource"><c>/SOURCE</c>: list source-locale titles, skipping localization entirely.</param>
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		IWikiLocalizationService localization, INotifyService notifyService, string conditions, string? locale, bool forceSource)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var visibility = await WikiCommandHelper.VisibilityAsync(parser, executor);
		return await RunAsync(wikiService, WikiFactConditions.Split(conditions), visibility, MaxListed) switch
		{
			WikiFactQueryListing found => await ToldAsync(WikiCommandHelper.PageListing($"{found.Pages.Count} page(s) where {conditions.Trim()}",
				await RowsAsync(wikiService, localization, found, visibility, locale, forceSource),
				after: found.More ? "… and more. Narrow the query, or see the web portal." : null,
				shown: await LabelsAsync(wikiService, found.Show))),
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

	/// <summary>Each page's listing row, with the values of the query's <c>show=</c> properties after its title in place of the rest, a page by its title.</summary>
	private static async Task<IReadOnlyCollection<string[]>> RowsAsync(IWikiService wikiService, IWikiLocalizationService localization,
		WikiFactQueryListing found, WikiVisibility visibility, string? locale, bool forceSource)
	{
		var rows = await ListWiki.PageRowsAsync(localization, found.Pages, locale, forceSource);
		if (found.Show.Count == 0) return rows;
		var schema = await wikiService.GetFactSchemaAsync();
		var shown = new List<string[]>(rows.Count);
		foreach (var (row, page) in rows.Zip(found.Pages))
		{
			var facts = await WikiFactEntry.ShownAsync(wikiService, schema, page.Facts.Where(f => found.Show.Contains(f.Property)), visibility,
				target => target.Title);
			shown.Add([.. row[..2], .. found.Show.Select(p => string.Join(" · ", facts.Where(f => f.Property == p).Select(f => f.Value)))]);
		}
		return shown;
	}

	private static async Task<IReadOnlyList<string>> LabelsAsync(IWikiService wikiService, IReadOnlyList<string> properties)
	{
		if (properties.Count == 0) return [];
		var schema = await wikiService.GetFactSchemaAsync();
		return [.. properties.Select(p => WikiFactEntry.Label(schema, p))];
	}

	/// <summary>Up to <paramref name="limit"/> of the pages the typed conditions find, following each continuation.</summary>
	public static async Task<WikiFactQueryListingResult> RunAsync(IWikiService wikiService, IEnumerable<string> conditions,
		WikiVisibility visibility, int limit)
		=> await WikiFactQueryText.ParseAsync(wikiService, await wikiService.GetFactSchemaAsync(), conditions, WikiFactQuery.MaxTake, visibility) switch
		{
			WikiFactQuery query => await UpToAsync(wikiService, query, visibility, limit),
			Error<string> error => error
		};

	/// <summary>Reads until one page past <paramref name="limit"/>, which tells whether more match.</summary>
	private static async Task<WikiFactQueryListingResult> UpToAsync(IWikiService wikiService, WikiFactQuery query,
		WikiVisibility visibility, int limit)
	{
		var pages = new List<WikiPage>();
		for (string? after = null; ;)
		{
			switch (await wikiService.QueryFactsAsync(query with { After = after }, visibility))
			{
				case WikiFactIndexBuilding building: return building;
				case Error<string> error: return error;
				case WikiFactQueryPage page:
					pages.AddRange(page.Pages);
					if (pages.Count > limit) return new WikiFactQueryListing(pages[..limit], More: true, query.Show);
					if (page.Next is not { } next) return new WikiFactQueryListing(pages, More: false, query.Show);
					after = next;
					break;
			}
		}
	}
}

/// <summary>
/// Up to a limit of the pages a fact query finds, whether more match than were listed, and the properties whose values
/// it shows beside them.
/// </summary>
public sealed record WikiFactQueryListing(IReadOnlyList<WikiPage> Pages, bool More, IReadOnlyList<string> Show);

public union WikiFactQueryListingResult(WikiFactQueryListing, WikiFactIndexBuilding, Error<string>);
