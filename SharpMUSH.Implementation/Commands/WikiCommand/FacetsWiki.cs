using MarkupString.Layout;
using Mediator;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// <c>@wiki/facets &lt;category&gt;[=&lt;condition&gt;[, &lt;condition&gt;...]]</c>: the values the category's pages
/// state for each property, with how many pages state each (<see cref="WikiFactFacets"/>). A value narrows the facets
/// to the pages stating it, and the conditions chosen so far list those pages.
/// </summary>
public static class FacetsWiki
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		IWikiLocalizationService localization, INotifyService notifyService, MString categoryArg, MString? conditionsArg, string? locale)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var name = categoryArg.ToPlainText().Trim();
		if (name.StartsWith("category:", StringComparison.OrdinalIgnoreCase)) name = name["category:".Length..];
		var key = WikiHelpers.CategoryKey(name);
		var conditions = WikiFactConditions.Split(conditionsArg?.ToPlainText() ?? string.Empty)
			.Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
		var visibility = await WikiCommandHelper.VisibilityAsync(parser, executor);

		return await WikiFactFacets.CountAsync(wikiService, key, conditions, visibility) switch
		{
			WikiFacets facets => await ToldAsync(Shown(parser.HouseLook(), WikiHelpers.CategoryLabel(key, await localization.GetCategoryNamesAsync(locale, visibility)),
				key, conditions, facets)),
			Error<string> error => await ToldAsync(MarkupText.Plain($"WIKI: {error.Value}"), ErrorMessages.Returns.InvalidWikiQuery),
		};

		async ValueTask<MString> ToldAsync(MString told, string? returned = null)
		{
			await notifyService.Notify(executor, told, executor);
			return returned is null ? told : MarkupText.Plain(returned);
		}
	}

	private static MString Shown(LayoutTheme look, string label, string key, IReadOnlyList<string> conditions, WikiFacets facets)
	{
		var separator = MarkupText.Plain(" · ");
		var where = conditions.Count == 0 ? string.Empty : $" where {string.Join(", ", conditions)}";
		List<MString> summary = [MarkupText.Plain($"{facets.Pages} page(s){where}.")];
		if (conditions.Count > 0)
			summary.Add(ServerLayout.CommandLink("List them", $"@wiki/query/noeval {string.Join(", ", [$"category={key}", .. conditions])}"));
		if (facets.Partial)
			summary.Add(MarkupText.Plain($"Only the first {WikiFactFacets.MaxPages} pages were counted."));

		Block values = facets.Facets.Count == 0
			? new TextBlock(MarkupText.Plain("No facts."))
			: ServerLayout.KeyValues(facets.Facets.Select(facet => (facet.Label, MarkupText.Join(separator, [
				.. facet.Values.Select(value => ServerLayout.CommandLink($"{value.Label} ({value.Count})",
					$"@wiki/facets/noeval {key}={string.Join(", ", [.. conditions, $"{facet.Property}={value.Value}"])}")),
				.. facet.More > 0 ? [MarkupText.Plain($"{facet.More} more")] : Array.Empty<MString>(),
			]))));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Facets: {label}"),
			new TextBlock(MarkupText.Join(MarkupText.Plain(" "), summary)), values), 78, look);
	}
}
