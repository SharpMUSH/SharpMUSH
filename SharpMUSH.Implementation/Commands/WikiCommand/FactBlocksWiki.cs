using System.Collections.Immutable;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using SharpMUSH.Library.API;
using Block = MarkupString.Layout.Block;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// The <c>::: facts &lt;conditions&gt;</c> blocks of a page's text as <c>@wiki</c> shows them: the pages the query finds
/// when the page is read, as a table with a column for each property its <c>show=</c> names. The conditions are a
/// query, never softcode.
/// </summary>
public static class FactBlocksWiki
{
	/// <summary>The most blocks on one page that are answered; the rest show as the other live listings do.</summary>
	public const int MaxBlocks = 8;

	/// <summary>The most pages one block lists; past them, a link runs the query for the rest.</summary>
	public const int MaxPages = 20;

	/// <summary>
	/// What each of <paramref name="document"/>'s first <see cref="MaxBlocks"/> facts blocks shows, by its conditions: the
	/// pages under their titles in <paramref name="locale"/>, or as written when <paramref name="forceSource"/>.
	/// </summary>
	public static async Task<IReadOnlyDictionary<string, MString>> AnswerAsync(IWikiService wikiService,
		IWikiLocalizationService localization, MarkdownDocument document, WikiVisibility visibility, int width, string? locale,
		bool forceSource)
	{
		var blocks = new Dictionary<string, MString>(StringComparer.Ordinal);
		var written = document.Descendants<CustomContainer>()
			.Select(RecursiveMarkdownRenderer.Fence)
			.Where(fence => fence.Name.Equals(RecursiveMarkdownRenderer.FactsContainerName, StringComparison.OrdinalIgnoreCase)
				&& fence.Arg.Length > 0)
			.Select(fence => fence.Arg)
			.Distinct(StringComparer.Ordinal)
			.Take(MaxBlocks);
		foreach (var conditions in written)
			blocks[conditions] = await QueryWiki.RunAsync(wikiService, WikiFactConditions.Split(conditions), visibility, MaxPages) switch
			{
				WikiFactQueryListing found => await ListedAsync(wikiService, found, conditions, visibility, width,
					await TitlesAsync(localization, found.Pages, locale, forceSource)),
				WikiFactIndexBuilding => MarkupText.Plain("[The fact index is building; read the page again when it is ready.]"),
				Error<string> error => MarkupText.Plain($"[Query {conditions}: {error.Value}]"),
			};
		return blocks;
	}

	private static async Task<IReadOnlyList<string>> TitlesAsync(IWikiLocalizationService localization, IReadOnlyList<WikiPage> pages,
		string? locale, bool forceSource)
		=> forceSource
			? [.. pages.Select(p => p.Title)]
			: [.. (await localization.LocalizeAllAsync(pages, locale, includeDrafts: false)).Select(p => p.Title)];

	private static async Task<MString> ListedAsync(IWikiService wikiService, WikiFactQueryListing found, string conditions,
		WikiVisibility visibility, int width, IReadOnlyList<string> titles)
	{
		if (found.Pages.Count == 0) return MarkupText.Plain("No pages match.");
		var schema = await wikiService.GetFactSchemaAsync();
		var rows = new List<ImmutableArray<Block>>(found.Pages.Count);
		foreach (var (page, title) in found.Pages.Zip(titles))
		{
			var facts = await WikiFactEntry.ShownAsync(wikiService, schema, page.Facts.Where(f => found.Show.Contains(f.Property)), visibility,
				target => target.Title);
			rows.Add([
				ServerLayout.Body(ServerLayout.CommandLink(title, $"@wiki {WikiCommandHelper.DisplayReference(page)}")),
				.. found.Show.Select(p => ServerLayout.Body(MarkupText.Plain(string.Join(" · ", facts.Where(f => f.Property == p).Select(f => f.Value))))),
			]);
		}
		Block table = ServerLayout.Listing([
			new TableColumn(MarkupText.Plain("Page")) { Min = 10 },
			.. found.Show.Select(p => WikiFactEntry.Label(schema, p))
				.Select(heading => new TableColumn(MarkupText.Plain(heading)) { Min = Math.Min(heading.Length, 16) }),
		], rows);
		return ServerLayout.Build(found.More
			? new Stack([table, new TextBlock(ServerLayout.CommandLink("… and more", $"@wiki/query/noeval {conditions}"))])
			: table, width);
	}
}
