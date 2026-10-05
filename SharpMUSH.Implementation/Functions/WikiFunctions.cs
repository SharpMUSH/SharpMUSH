using SharpMUSH.Implementation.Common;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// wiki(&lt;page&gt;[, &lt;field&gt;[, &lt;locale&gt;]])
	/// Returns information about a wiki page. The page target accepts a namespace
	/// prefix ("Help:Markdown Guide"). Fields: text (default), markdown, title,
	/// locale, categories, namespace, revision, updated, author.
	/// The optional third argument names a locale; it defaults to the executor's LOCALE.
	/// Unpublished pages and unpublished translations are never reachable.
	/// </summary>
	[SharpFunction(Name = "wiki", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["page", "field", "locale"])]
	public async ValueTask<CallState> wiki(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var field = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, "text").ToPlainText().Trim().ToLowerInvariant();

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var (ns, slug) = WikiHelpers.ResolveTitle(target);
		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			return new CallState(ErrorMessages.Returns.NoSuchWikiPage);
		}

		// An unpublished page is not reachable from softcode at all, which is the same rule wikilist(),
		// wikisearch() and wikirecent() apply. includeDrafts below only filters unpublished *translations*;
		// on its own it left every draft page's body, title and metadata one wiki() call away from anybody
		// who could guess a slug. A page whose read requirements the executor does not meet is just as
		// absent. The answer is the one an absent page gives, because "this page exists but you may not
		// read it" is itself the disclosure.
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!(await WikiCommandHelper.SoftcodeVisibilityAsync(parser, executor)).Admits(page))
		{
			return new CallState(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var localization = parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>();

		// Third argument wins; otherwise the executor's LOCALE, exactly as @wiki does. An unparseable tag is
		// treated as absent by the localization service — a bad locale must not turn a read into #-1.
		var explicitLocale = args.TryGetValue("2", out var localeArg)
			? localeArg.Message!.ToPlainText().Trim()
			: null;
		var locale = string.IsNullOrWhiteSpace(explicitLocale)
			? await WikiCommandHelper.ResolveExecutorLocaleAsync(parser, executor)
			: explicitLocale;

		// Softcode is unauthenticated with respect to translation drafts too: there is no per-locale
		// permission to check against, so an unpublished translation is never reachable from a function.
		var localized = await localization.LocalizeAsync(page, locale, includeDrafts: false);

		return field switch
		{
			"text" => new CallState(localized.PlainText),
			"markdown" => new CallState(localized.MarkdownSource),
			"title" => new CallState(localized.Title),
			"locale" => new CallState(localized.Locale),
			"categories" => new CallState(string.Join(" ", localized.Page.Categories)),
			"namespace" => new CallState(localized.Page.Namespace),
			"revision" => new CallState(localized.RevisionNumber.ToString()),
			"updated" => new CallState(localized.UpdatedAt.ToUnixTimeSeconds().ToString()),
			"author" => new CallState(localized.Page.AuthorDbref),
			_ => new CallState(ErrorMessages.Returns.UnknownWikiField),
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
	public async ValueTask<CallState> wikilist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
					return new CallState(ErrorMessages.Returns.NoSuchWikiNamespace);
				}
				ns = parsed;
			}
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		// As in wiki() and wikisearch(), a draft is never discoverable from a function, nor a page the
		// executor may not read.
		var visibility = await WikiCommandHelper.SoftcodeVisibilityAsync(parser, await parser.CurrentState.KnownExecutorObject(Mediator));
		var pages = await wikiService.GetAllPagesAsync(0, 1000, ns, visibility);

		return new CallState(string.Join(" ", pages.Select(WikiCommandHelper.DisplayReference)));
	}

	/// <summary>
	/// wikicategory(&lt;category&gt;)
	/// Returns a space-separated list of the references of the pages in a category, subcategories
	/// (pages in the category namespace) included. The name is matched by key, so <c>Places of Note</c>
	/// and <c>places_of_note</c> are one category. Categories belong to the page, so the answer is the
	/// same in every locale.
	/// </summary>
	[SharpFunction(Name = "wikicategory", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["category"])]
	public async ValueTask<CallState> wikicategory(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var key = WikiHelpers.CategoryKey(name.StartsWith("category:", StringComparison.OrdinalIgnoreCase) ? name["category:".Length..] : name);
		if (key.Length == 0)
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "WIKICATEGORY"));
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		// Same rule as wikilist(): no drafts, and nothing the executor may not read.
		var visibility = await WikiCommandHelper.SoftcodeVisibilityAsync(parser, await parser.CurrentState.KnownExecutorObject(Mediator));
		var pages = await wikiService.GetByCategoryAsync(key, 0, 1000, visibility);

		return new CallState(string.Join(" ", pages.Select(WikiCommandHelper.DisplayReference)));
	}

	/// <summary>
	/// wikisearch(&lt;text&gt;)
	/// Returns a space-separated list of page references whose title or body
	/// contains the given text (case-insensitive), in any locale the page has been
	/// translated into.
	/// </summary>
	[SharpFunction(Name = "wikisearch", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["text"])]
	public async ValueTask<CallState> wikisearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var needle = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		if (needle.Length == 0)
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "WIKISEARCH"));
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var localization = parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>();

		// Neither an unpublished page nor an unpublished translation is ever discoverable from a function,
		// nor a page the executor may not read. requestedLocale is null because the matched locale only breaks
		// display ties and this function returns references, which have no locale dimension; reading the
		// executor's LOCALE to compute a value that is then discarded would be a query for nothing.
		var matches = await ListWiki.SearchPagesAsync(
			wikiService, localization, needle, 100,
			await WikiCommandHelper.SoftcodeVisibilityAsync(parser, await parser.CurrentState.KnownExecutorObject(Mediator)),
			requestedLocale: null);

		return new CallState(string.Join(" ", matches.Select(m => WikiCommandHelper.DisplayReference(m.Page))));
	}

	/// <summary>
	/// wikirecent([&lt;count&gt;])
	/// Returns a space-separated list of the most recently edited page references.
	/// Count defaults to 10, clamped to 1–50.
	/// </summary>
	[SharpFunction(Name = "wikirecent", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["count"])]
	public async ValueTask<CallState> wikirecent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		// Same rule as wikilist(): no drafts, nothing the executor may not read. The store filters before
		// counting, so a run of recent hidden edits neither shows nor shortens the answer.
		var visibility = await WikiCommandHelper.SoftcodeVisibilityAsync(parser, await parser.CurrentState.KnownExecutorObject(Mediator));
		var pages = await wikiService.GetRecentChangesAsync(count, visibility);

		return new CallState(string.Join(" ", pages.Select(WikiCommandHelper.DisplayReference)));
	}

	/// <summary>
	/// wikiaccess(&lt;page&gt;, &lt;action&gt;[, &lt;player&gt;])
	/// 1 when the executor (or the player named) may read, edit or delete the page, by its wiki permissions
	/// and what the page's namespace, categories and the page itself require; 0 when not. Asking about
	/// another player needs the wiki.admin permission. A page the executor may not read is no page.
	/// </summary>
	[SharpFunction(Name = "wikiaccess", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["page", "action", "player"])]
	public async ValueTask<CallState> wikiaccess(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!Enum.TryParse<WikiAction>(args["1"].Message!.ToPlainText().Trim(), ignoreCase: true, out var action)
			|| action == WikiAction.Create
			|| int.TryParse(args["1"].Message!.ToPlainText().Trim(), out _))
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "WIKIACCESS"));
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var access = WikiCommandHelper.Access(parser);
		var (ns, slug) = WikiHelpers.ResolveTitle(args["0"].Message!.ToPlainText());
		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page
			|| !(await WikiCommandHelper.SoftcodeVisibilityAsync(parser, executor)).Admits(page))
		{
			return new CallState(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var subject = executor;
		if (args.TryGetValue("2", out var playerArg) && playerArg.Message!.ToPlainText().Trim() is { Length: > 0 } name)
		{
			if (await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, name.TrimStart('*'))
				is not AnySharpObject who)
				return new CallState(ErrorMessages.Returns.NoSuchObject);
			if (who.Object().Key != executor.Object().Key
				&& !(await WikiCommandHelper.ReaderAsync(parser, executor)).Has(Library.Authorization.PortalPermission.WikiAdmin))
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			subject = who;
		}

		var reader = await access.ForObjectAsync(subject);
		var allowed = (action != WikiAction.Read || access.MaySeeDraft(reader, page))
			&& (await access.DecideAsync(reader, page, action)).Allowed;
		return new CallState(allowed ? "1" : "0");
	}
}
