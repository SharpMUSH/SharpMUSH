using System.Globalization;
using MarkupString.Layout;
using System.Collections.Immutable;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using SharpMUSH.Library.API;
using Block = MarkupString.Layout.Block;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// The live listing blocks of a page's text as the game shows them (<c>::: category</c>, <c>::: pagelist</c>,
/// <c>::: recent</c> and <c>::: facts</c>): the pages each finds when the text is read, as a table whose titles run
/// <c>@wiki</c> of their page. A <c>::: facts</c> block's conditions are a query, never softcode.
/// </summary>
public static class LiveBlocksWiki
{
	/// <summary>The most blocks in one text that are answered; the rest show as a placeholder.</summary>
	public const int MaxBlocks = RecursiveMarkdownRenderer.MaxLiveBlocks;

	/// <summary>The most pages one block lists; past them, a link runs the command listing the rest.</summary>
	public const int MaxPages = 20;

	/// <summary>
	/// What each of <paramref name="document"/>'s first <see cref="MaxBlocks"/> live listing blocks shows, by
	/// <see cref="RecursiveMarkdownRenderer.LiveBlockKey"/>: the pages <paramref name="visibility"/> admits, under their
	/// titles in <paramref name="locale"/>, or as written when <paramref name="forceSource"/>. A block whose argument the
	/// portal would refuse is left out, so it shows as a placeholder.
	/// </summary>
	public static async Task<IReadOnlyDictionary<string, MString>> AnswerAsync(LayoutTheme look, IWikiService wikiService,
		IWikiLocalizationService localization, MarkdownDocument document, WikiVisibility visibility, int width, string? locale,
		bool forceSource)
	{
		var blocks = new Dictionary<string, MString>(StringComparer.Ordinal);
		var written = document.Descendants<CustomContainer>()
			.Select(RecursiveMarkdownRenderer.Fence)
			.Where(fence => RecursiveMarkdownRenderer.IsLiveBlock(fence.Name) && WikiDirectives.Argument(fence.Name, fence.Arg) is not null)
			.DistinctBy(fence => RecursiveMarkdownRenderer.LiveBlockKey(fence.Name, fence.Arg), StringComparer.Ordinal)
			.Take(MaxBlocks);
		foreach (var (name, arg) in written)
			blocks[RecursiveMarkdownRenderer.LiveBlockKey(name, arg)] = name.ToLowerInvariant() switch
			{
				RecursiveMarkdownRenderer.FactsContainerName => await FactsAsync(look, wikiService, localization, arg, visibility, width, locale, forceSource),
				"category" => await PagesAsync(look, localization, await wikiService.GetByCategoryAsync(WikiHelpers.CategoryKey(arg), 0, MaxPages + 1, visibility),
					$"@wiki/category {arg}", dated: false, width, locale, forceSource),
				"pagelist" when WikiHelpers.ParseNamespace(arg) is { } ns => await PagesAsync(look, localization,
					await wikiService.GetByNamespaceAsync(ns, 0, MaxPages + 1, visibility), $"@wiki/list {arg}", dated: false, width, locale, forceSource),
				"pagelist" => MarkupText.Plain($"[No namespace {arg}.]"),
				_ => await RecentAsync(look, wikiService, localization, int.Parse(WikiDirectives.Argument(name, arg)!, CultureInfo.InvariantCulture),
					visibility, width, locale, forceSource),
			};
		return blocks;
	}

	private static async Task<MString> RecentAsync(LayoutTheme look, IWikiService wikiService, IWikiLocalizationService localization, int count,
		WikiVisibility visibility, int width, string? locale, bool forceSource)
		=> await PagesAsync(look, localization, await wikiService.GetRecentChangesAsync(Math.Min(count, MaxPages + 1), visibility),
			count > MaxPages ? $"@wiki/recent {count}" : null, dated: true, width, locale, forceSource);

	/// <summary>
	/// <paramref name="pages"/>' first <see cref="MaxPages"/> as a table of titles, each running <c>@wiki</c> of its page,
	/// with the date each was edited when <paramref name="dated"/>. Past them, a link runs <paramref name="more"/>.
	/// </summary>
	private static async Task<MString> PagesAsync(LayoutTheme look, IWikiLocalizationService localization, IReadOnlyList<WikiPage> pages,
		string? more, bool dated, int width, string? locale, bool forceSource)
	{
		if (pages.Count == 0) return MarkupText.Plain("No pages.");
		var shown = pages.Take(MaxPages).ToList();
		var titles = await TitlesAsync(localization, shown, locale, forceSource);
		var rows = shown.Zip(titles).Select(row => ImmutableArray.Create<Block>([
			ServerLayout.Body(ServerLayout.CommandLink(row.Second, $"@wiki {WikiCommandHelper.DisplayReference(row.First)}")),
			.. dated ? [ServerLayout.Body(MarkupText.Plain(row.First.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))] : Array.Empty<Block>(),
		])).ToList();
		Block table = ServerLayout.Listing([
			new TableColumn(MarkupText.Plain("Page")) { Min = 10 },
			.. dated ? [new TableColumn(MarkupText.Plain("Edited")) { Wrap = false }] : Array.Empty<TableColumn>(),
		], rows);
		return ServerLayout.Build(pages.Count > MaxPages && more is not null
			? new Stack([table, new TextBlock(ServerLayout.CommandLink("… and more", more))])
			: table, width, look);
	}

	private static async Task<MString> FactsAsync(LayoutTheme look, IWikiService wikiService, IWikiLocalizationService localization,
		string conditions, WikiVisibility visibility, int width, string? locale, bool forceSource)
		=> await QueryWiki.RunAsync(wikiService, WikiFactConditions.Split(conditions), visibility, MaxPages) switch
		{
			WikiFactQueryListing found => await ListedAsync(look, wikiService, found, conditions, visibility, width,
				await TitlesAsync(localization, found.Pages, locale, forceSource)),
			WikiFactIndexBuilding => MarkupText.Plain("[The fact index is building; read the page again when it is ready.]"),
			Error<string> error => MarkupText.Plain($"[Query {conditions}: {error.Value}]"),
		};

	private static async Task<IReadOnlyList<string>> TitlesAsync(IWikiLocalizationService localization, IReadOnlyList<WikiPage> pages,
		string? locale, bool forceSource)
		=> forceSource
			? [.. pages.Select(p => p.Title)]
			: [.. (await localization.LocalizeAllAsync(pages, locale, includeDrafts: false)).Select(p => p.Title)];

	private static async Task<MString> ListedAsync(LayoutTheme look, IWikiService wikiService, WikiFactQueryListing found, string conditions,
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
			: table, width, look);
	}
}
