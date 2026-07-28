using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
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

	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource"><c>/SOURCE</c>: match source bodies only, ignoring every translation.</param>
	public static async ValueTask<MString> Search(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString needleArg,
		string? locale = null,
		bool forceSource = false)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var needle = needleArg.ToPlainText().Trim();

		if (needle.Length == 0)
		{
			await notifyService.Notify(executor, "WIKI: What do you want to search for?", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var matches = await SearchPagesAsync(
			wikiService, localization, needle, MaxSearchResults,
			await WikiCommandHelper.CanSeeDrafts(executor), locale, forceSource);

		var lines = new List<MString>
		{
			MModule.single($"WIKI: {matches.Count} page(s) matching '{needle}':"),
		};
		// The locale marker mirrors @wiki/view's: shown only when the hit is somewhere the reader is not
		// already looking. Without it, a page whose English title and body contain the needle nowhere looks
		// like a false positive.
		lines.AddRange(matches.Select(m => MModule.single(
			"  " + WikiCommandHelper.FormatPageLine(m.Page)
			+ (m.Locale.Equals(localization.SourceLocaleOf(m.Page), StringComparison.OrdinalIgnoreCase)
				? string.Empty
				: $" [{m.Locale}]"))));

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
	/// Case-insensitive title/plaintext substring scan over both content streams — every page's source
	/// body and every translation's — paged through each in turn. Adequate for in-game scale; a full-text
	/// index (area 14) can replace it.
	/// </summary>
	/// <remarks>
	/// Entirely in-process, which is why adding locales needed no query-language work in any backend: the
	/// translation stream is fetched in bulk exactly as the page stream is. The cost is that the scan now
	/// reads roughly twice the rows. At in-game wiki sizes (hundreds of pages, capped at 100 results) that
	/// is not worth an index; see <c>docs/todo/area-05-wiki.md</c> before assuming it stays that way.
	/// </remarks>
	/// <param name="includeDrafts">
	/// True when this caller may see unpublished pages <em>and</em> unpublished translations — see
	/// <see cref="WikiCommandHelper.CanSeeDrafts"/>. There is no default: both bulk accessors return drafts,
	/// so every call site has to decide, and a safe-looking default is how the filter went missing here in
	/// the first place.
	/// </param>
	/// <param name="requestedLocale">
	/// The reader's locale, used only to break ties: a page matching in several locales is reported under
	/// the reader's own if that is one of them. Never affects <em>which</em> pages match — a reader should
	/// find a page whatever language the word they remember was written in.
	/// </param>
	/// <param name="forceSource">Match source bodies only, skipping the translation stream entirely.</param>
	internal static async Task<List<WikiSearchMatch>> SearchPagesAsync(
		IWikiService wikiService,
		IWikiLocalizationService localization,
		string needle,
		int maxResults,
		bool includeDrafts,
		string? requestedLocale,
		bool forceSource = false)
	{
		var reader = WikiHelpers.NormalizeLocaleOrEmpty(requestedLocale ?? string.Empty);
		var byPage = new Dictionary<string, WikiSearchMatch>(StringComparer.Ordinal);
		var order = new List<string>();

		bool Hit(string title, string plainText) =>
			title.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| plainText.Contains(needle, StringComparison.OrdinalIgnoreCase);

		void Record(WikiPage page, string locale)
		{
			if (byPage.TryGetValue(page.Id, out var existing))
			{
				// One page is one result however many of its locales matched. Which locale gets reported
				// only matters when several did: prefer the reader's own, so a French reader who found a
				// page by a French word is told [fr] rather than being pointed at the English it also hit.
				if (reader.Length > 0
					&& !existing.Locale.Equals(reader, StringComparison.OrdinalIgnoreCase)
					&& locale.Equals(reader, StringComparison.OrdinalIgnoreCase))
					byPage[page.Id] = existing with { Locale = locale };
				return;
			}

			byPage.Add(page.Id, new WikiSearchMatch(page, locale));
			order.Add(page.Id);
		}

		var skip = 0;
		while (order.Count < maxResults)
		{
			var batch = await wikiService.GetAllPagesAsync(skip, SearchScanPageSize);
			if (batch.Count == 0) break;

			foreach (var page in batch)
			{
				if ((includeDrafts || page.Published) && Hit(page.Title, page.PlainText))
					Record(page, localization.SourceLocaleOf(page));
			}

			if (batch.Count < SearchScanPageSize) break;
			skip += SearchScanPageSize;
		}

		if (forceSource) return Ordered();

		// Pages reached from a translation are loaded one by one and memoised. Bounded by maxResults, and
		// only paid for by a hit — the alternative, holding the whole page stream in memory to join
		// against, costs the same reads plus the pages that matched nothing.
		var pages = new Dictionary<string, WikiPage?>(StringComparer.Ordinal);
		skip = 0;
		while (order.Count < maxResults)
		{
			var batch = await wikiService.GetAllTranslationsAsync(skip, SearchScanPageSize);
			if (batch.Count == 0) break;

			foreach (var translation in batch)
			{
				if (!includeDrafts && !translation.Published) continue;
				if (!Hit(translation.Title, translation.PlainText)) continue;

				if (byPage.TryGetValue(translation.PageId, out var already))
				{
					Record(already.Page, translation.Locale);
					continue;
				}

				if (!pages.TryGetValue(translation.PageId, out var page))
				{
					var lookup = await wikiService.GetByIdAsync(translation.PageId);
					page = lookup.IsT0 ? lookup.AsT0 : null;
					pages[translation.PageId] = page;
				}

				// A page deleted between the two scans, or an unpublished page this reader may not see: a
				// published translation does not make a draft page discoverable.
				if (page is null || (!includeDrafts && !page.Published)) continue;

				Record(page, translation.Locale);
			}

			if (batch.Count < SearchScanPageSize) break;
			skip += SearchScanPageSize;
		}

		return Ordered();

		List<WikiSearchMatch> Ordered() => order.Take(maxResults).Select(id => byPage[id]).ToList();
	}
}
