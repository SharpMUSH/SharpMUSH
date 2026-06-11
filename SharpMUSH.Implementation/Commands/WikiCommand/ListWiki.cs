using Mediator;
using SharpMUSH.Library.Definitions;
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

	public static async ValueTask<MString> List(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString? nsArg)
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

		var pages = await wikiService.GetAllPagesAsync(0, MaxListed, ns);
		var total = await wikiService.CountPagesAsync(ns);

		var lines = new List<MString>
		{
			MModule.single($"WIKI: {total} page(s){(ns is null ? "" : $" in namespace '{nsText!.ToLowerInvariant()}'")}:"),
		};
		lines.AddRange(pages.Select(p => MModule.single("  " + WikiCommandHelper.FormatPageLine(p))));
		if (total > pages.Count)
			lines.Add(MModule.single($"  … and {total - pages.Count} more. See the web portal for the full index."));

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

		var matches = await SearchPagesAsync(wikiService, needle, MaxSearchResults);

		var lines = new List<MString>
		{
			MModule.single($"WIKI: {matches.Count} page(s) matching '{needle}':"),
		};
		lines.AddRange(matches.Select(p => MModule.single("  " + WikiCommandHelper.FormatPageLine(p))));

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}

	public static async ValueTask<MString> Recent(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString? countArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var count = 10;
		var countText = countArg?.ToPlainText().Trim();
		if (!string.IsNullOrEmpty(countText) && (!int.TryParse(countText, out count) || count < 1 || count > 50))
		{
			await notifyService.Notify(executor, "WIKI: Count must be a number between 1 and 50.", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var pages = await wikiService.GetRecentChangesAsync(count);

		var lines = new List<MString> { MModule.single("WIKI: Recently edited pages:") };
		lines.AddRange(pages.Select(p => MModule.single("  " + WikiCommandHelper.FormatPageLine(p))));

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}

	/// <summary>
	/// Case-insensitive title/plaintext substring scan, paged through the full page
	/// list. Adequate for in-game scale; a full-text index (area 14) can replace it.
	/// </summary>
	internal static async Task<List<WikiPage>> SearchPagesAsync(
		IWikiService wikiService, string needle, int maxResults)
	{
		var matches = new List<WikiPage>();
		var skip = 0;
		while (matches.Count < maxResults)
		{
			var batch = await wikiService.GetAllPagesAsync(skip, SearchScanPageSize);
			if (batch.Count == 0) break;

			matches.AddRange(batch.Where(p =>
				p.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
				|| p.PlainText.Contains(needle, StringComparison.OrdinalIgnoreCase)));

			if (batch.Count < SearchScanPageSize) break;
			skip += SearchScanPageSize;
		}

		return matches.Take(maxResults).ToList();
	}
}
