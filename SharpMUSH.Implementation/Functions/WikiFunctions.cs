using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// wiki(&lt;page&gt;[, &lt;field&gt;])
	/// Returns information about a wiki page. The page target accepts a namespace
	/// prefix ("Help:Markdown Guide"). Fields: text (default), markdown, title,
	/// category, tags, namespace, revision, updated, author.
	/// </summary>
	[SharpFunction(Name = "wiki", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["page", "field"])]
	public static async ValueTask<CallState> wiki(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var field = args.TryGetValue("1", out var fieldArg)
			? fieldArg.Message!.ToPlainText().Trim().ToLowerInvariant()
			: "text";

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var (ns, slug) = WikiCommandHelper.ResolveTarget(target);
		var lookup = await wikiService.GetBySlugAsync(slug, ns);
		if (lookup.IsT1)
		{
			return new CallState(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;
		return field switch
		{
			"text" => new CallState(page.PlainText),
			"markdown" => new CallState(page.MarkdownSource),
			"title" => new CallState(page.Title),
			"category" => new CallState(page.Category ?? string.Empty),
			"tags" => new CallState(string.Join(" ", page.Tags)),
			"namespace" => new CallState(page.Namespace),
			"revision" => new CallState(page.RevisionNumber.ToString()),
			"updated" => new CallState(page.UpdatedAt.ToUnixTimeSeconds().ToString()),
			"author" => new CallState(page.AuthorDbref),
			_ => new CallState("#-1 UNKNOWN WIKI FIELD"),
		};
	}

	/// <summary>
	/// wikilist([&lt;namespace&gt;])
	/// Returns a space-separated list of page references ("slug" for the main
	/// namespace, "ns:slug" otherwise), optionally restricted to one namespace.
	/// </summary>
	[SharpFunction(Name = "wikilist", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["namespace"])]
	public static async ValueTask<CallState> wikilist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		WikiNamespace? ns = null;
		if (args.TryGetValue("0", out var nsArg))
		{
			var nsText = nsArg.Message!.ToPlainText().Trim();
			if (nsText.Length > 0)
			{
				if (!Enum.TryParse<WikiNamespace>(nsText, ignoreCase: true, out var parsed))
				{
					return new CallState("#-1 NO SUCH WIKI NAMESPACE");
				}
				ns = parsed;
			}
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var pages = await wikiService.GetAllPagesAsync(0, 1000, ns);

		return new CallState(string.Join(" ", pages.Select(WikiCommandHelper.DisplayReference)));
	}

	/// <summary>
	/// wikisearch(&lt;text&gt;)
	/// Returns a space-separated list of page references whose title or body
	/// contains the given text (case-insensitive).
	/// </summary>
	[SharpFunction(Name = "wikisearch", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["text"])]
	public static async ValueTask<CallState> wikisearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var needle = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		if (needle.Length == 0)
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "WIKISEARCH"));
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var matches = await ListWiki.SearchPagesAsync(wikiService, needle, 100);

		return new CallState(string.Join(" ", matches.Select(WikiCommandHelper.DisplayReference)));
	}

	/// <summary>
	/// wikirecent([&lt;count&gt;])
	/// Returns a space-separated list of the most recently edited page references.
	/// Count defaults to 10, clamped to 1–50.
	/// </summary>
	[SharpFunction(Name = "wikirecent", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["count"])]
	public static async ValueTask<CallState> wikirecent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var count = 10;
		if (parser.CurrentState.Arguments.TryGetValue("0", out var countArg))
		{
			var countText = countArg.Message!.ToPlainText().Trim();
			if (countText.Length > 0 && (!int.TryParse(countText, out count) || count < 1 || count > 50))
			{
				return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "WIKIRECENT"));
			}
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var pages = await wikiService.GetRecentChangesAsync(count);

		return new CallState(string.Join(" ", pages.Select(WikiCommandHelper.DisplayReference)));
	}
}
