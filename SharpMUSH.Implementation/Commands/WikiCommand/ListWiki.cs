using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// @wiki/list [&lt;namespace&gt;], @wiki/search &lt;text&gt;, @wiki/recent [&lt;count&gt;]
/// — page discovery subcommands.
/// </summary>
public static class ListWiki
{
	private const int MaxListed = 100;
	private const int SearchScanPageSize = 200;
	private const int MaxSearchResults = 25;

	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource">
	/// <c>/SOURCE</c>: list source-locale titles, skipping localization entirely.
	/// </param>
	public static async ValueTask<MString> List(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString? nsArg,
		string? locale = null,
		bool forceSource = false)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		WikiNamespace? ns = null;
		var nsText = nsArg?.ToPlainText().Trim();
		if (!string.IsNullOrEmpty(nsText))
		{
			if (!Enum.TryParse<WikiNamespace>(nsText, ignoreCase: true, out var parsed))
			{
				await notifyService.Notify(executor,
					$"WIKI: Unknown namespace '{nsText}'. Valid: main, help, character, system.", executor);
				return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
			}
			ns = parsed;
		}

		var fetched = await wikiService.GetAllPagesAsync(0, MaxListed, ns);
		var pages = await VisiblePagesAsync(executor, fetched);
		var total = await wikiService.CountPagesAsync(ns);

		var lines = new List<MString>
		{
			MModule.single($"WIKI: {total} page(s){(ns is null ? "" : $" in namespace '{nsText!.ToLowerInvariant()}'")}:"),
		};
		lines.AddRange((await FormatPagesAsync(localization, pages, locale, forceSource))
			.Select(l => MModule.single("  " + l)));
		// Compared against the fetched window, not the visible one: this line means "the window did not
		// reach the end of the store", and drafts filtered out of the window did not shorten the store.
		if (total > fetched.Count)
			lines.Add(MModule.single($"  … and {total - fetched.Count} more. See the web portal for the full index."));

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}

	public static async ValueTask<MString> Search(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString needleArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var needle = needleArg.ToPlainText().Trim();

		if (needle.Length == 0)
		{
			await notifyService.Notify(executor, "WIKI: What do you want to search for?", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var matches = await SearchPagesAsync(
			wikiService, needle, MaxSearchResults, await WikiCommandHelper.CanSeeDrafts(executor));

		var lines = new List<MString>
		{
			MModule.single($"WIKI: {matches.Count} page(s) matching '{needle}':"),
		};
		lines.AddRange(matches.Select(p => MModule.single("  " + WikiCommandHelper.FormatPageLine(p))));

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}

	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource">
	/// <c>/SOURCE</c>: list source-locale titles, skipping localization entirely.
	/// </param>
	public static async ValueTask<MString> Recent(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString? countArg,
		string? locale = null,
		bool forceSource = false)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var count = 10;
		var countText = countArg?.ToPlainText().Trim();
		if (!string.IsNullOrEmpty(countText) && (!int.TryParse(countText, out count) || count < 1 || count > 50))
		{
			await notifyService.Notify(executor, "WIKI: Count must be a number between 1 and 50.", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		// Filtered after the fetch, so a burst of draft edits shortens the answer rather than disclosing
		// them. Asking for count+N and trimming would only move the guesswork around.
		var pages = await VisiblePagesAsync(executor, await wikiService.GetRecentChangesAsync(count));

		var lines = new List<MString> { MModule.single("WIKI: Recently edited pages:") };
		lines.AddRange((await FormatPagesAsync(localization, pages, locale, forceSource))
			.Select(l => MModule.single("  " + l)));

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}

	/// <summary>
	/// Drops the unpublished pages this reader may not see. Every listing surface feeds its rows through
	/// this, because <c>GetAllPagesAsync</c> and <c>GetRecentChangesAsync</c> both return drafts and say so.
	/// </summary>
	private static async ValueTask<IReadOnlyList<WikiPage>> VisiblePagesAsync(
		AnySharpObject executor, IReadOnlyList<WikiPage> pages) =>
		await WikiCommandHelper.CanSeeDrafts(executor)
			? pages
			: pages.Where(p => p.Published).ToList();

	/// <summary>
	/// Formats a listing, resolving each title into the reader's locale unless <paramref name="forceSource"/>.
	/// </summary>
	/// <remarks>
	/// <c>includeDrafts: false</c> even for a wizard: a listing is a discovery surface, and an unpublished
	/// translation's title appearing in it is exactly the leak the visibility filter exists to prevent. A
	/// staffer who wants to see a draft translation opens the page.
	/// </remarks>
	private static async ValueTask<IReadOnlyList<string>> FormatPagesAsync(
		IWikiLocalizationService localization,
		IReadOnlyList<WikiPage> pages,
		string? locale,
		bool forceSource)
	{
		if (forceSource)
			return pages.Select(WikiCommandHelper.FormatPageLine).ToList();

		var localized = await localization.LocalizeAllAsync(pages, locale, includeDrafts: false);
		return localized.Select(WikiCommandHelper.FormatPageLine).ToList();
	}

	/// <summary>
	/// Case-insensitive title/plaintext substring scan, paged through the full page
	/// list. Adequate for in-game scale; a full-text index (area 14) can replace it.
	/// </summary>
	/// <param name="includeDrafts">
	/// True when this caller may see unpublished pages — see <see cref="WikiCommandHelper.CanSeeDrafts"/>.
	/// There is no default: <c>GetAllPagesAsync</c> returns drafts, so every call site has to decide, and a
	/// safe-looking default is how the filter went missing here in the first place.
	/// </param>
	internal static async Task<List<WikiPage>> SearchPagesAsync(
		IWikiService wikiService, string needle, int maxResults, bool includeDrafts)
	{
		var matches = new List<WikiPage>();
		var skip = 0;
		while (matches.Count < maxResults)
		{
			var batch = await wikiService.GetAllPagesAsync(skip, SearchScanPageSize);
			if (batch.Count == 0) break;

			matches.AddRange(batch.Where(p =>
				(includeDrafts || p.Published)
				&& (p.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
					|| p.PlainText.Contains(needle, StringComparison.OrdinalIgnoreCase))));

			if (batch.Count < SearchScanPageSize) break;
			skip += SearchScanPageSize;
		}

		return matches.Take(maxResults).ToList();
	}
}
