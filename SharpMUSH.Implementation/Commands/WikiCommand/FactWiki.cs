using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// <c>@wiki/fact</c>: a page's facts, held by the page beside its categories. <c>&lt;page&gt;</c> shows them,
/// <c>&lt;page&gt;/&lt;property&gt;=&lt;value&gt;[|&lt;value&gt;...]</c> sets one property's values, and an empty
/// value clears it. Setting is an edit of the page; showing is a read.
/// </summary>
public static class FactWiki
{
	public static async ValueTask<MString> Show(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		INotifyService notifyService, MString targetArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var visibility = await WikiCommandHelper.VisibilityAsync(parser, executor);
		var (ns, slug) = WikiHelpers.ResolveTitle(targetArg.ToPlainText());
		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page || !visibility.Admits(page))
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var schema = await wikiService.GetFactSchemaAsync();
		var shown = await WikiFactEntry.ShownAsync(wikiService, schema, page.Facts, visibility, target => target.Title);
		Block body = shown.Count == 0
			? new TextBlock(MarkupText.Plain("No facts."))
			: ServerLayout.KeyValues(shown.GroupBy(f => f.Property)
				.Select(g => (g.Key, MarkupText.Plain(string.Join(" | ", g.Select(f => f.Value))))));
		await notifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Facts: {page.Title}"), body), 78, parser.HouseLook()), executor);
		return MarkupText.Plain(string.Join(' ', shown.Select(f => f.Property).Distinct()));
	}

	public static async ValueTask<MString> Set(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		INotifyService notifyService, MString targetArg, MString? valuesArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var target = targetArg.ToPlainText().Trim();
		var split = target.LastIndexOf('/');
		if (split <= 0 || split == target.Length - 1)
		{
			await notifyService.Notify(executor, "WIKI: Name the property: @wiki/fact <page>/<property>=<value>[|<value>...]", executor);
			return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}
		var (pageTarget, property) = (target[..split].Trim(), target[(split + 1)..].Trim());

		var (ns, slug) = WikiHelpers.ResolveTitle(pageTarget);
		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {pageTarget}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}
		if (await WikiCommandHelper.RefusalAsync(parser, notifyService, executor, page, WikiAction.Edit, pageTarget) is { } refused)
			return refused;

		var schema = await wikiService.GetFactSchemaAsync();
		var visibility = await WikiCommandHelper.VisibilityAsync(parser, executor);
		var typed = (valuesArg?.ToPlainText() ?? string.Empty).Split('|');
		var written = await WikiFactEntry.ResolveAsync(wikiService, schema, property, typed, visibility) switch
		{
			IReadOnlyList<string> values => await wikiService.SetFactsAsync(page.Id, WikiFactEntry.WithProperty(page.Facts, property, values)),
			Error<string> error => error,
		};
		return written switch
		{
			WikiPage updated => await StatedAsync(wikiService, notifyService, executor, schema, visibility, updated, property),
			Error<string> error => await RefusedAsync(notifyService, executor, error.Value, ErrorMessages.Returns.InvalidWikiFact),
			NotFound => await RefusedAsync(notifyService, executor, $"No such page: {pageTarget}", ErrorMessages.Returns.NoSuchWikiPage),
		};
	}

	/// <summary>
	/// <c>@wiki/fact/index</c> shows how far the fact index has come; <c>@wiki/fact/index rebuild</c> (wiki.admin) starts
	/// it over. Returns the phase, the pages indexed and the pages in the wiki.
	/// </summary>
	public static async ValueTask<MString> Index(IMUSHCodeParser parser, IMediator mediator, INotifyService notifyService, MString? arg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var request = arg?.ToPlainText().Trim() ?? string.Empty;
		WikiFactIndexStatus status;
		if (request.Length == 0)
			status = await mediator.Send(new GetWikiFactIndexStatusQuery());
		else if (!request.Equals("rebuild", StringComparison.OrdinalIgnoreCase))
			return await RefusedAsync(notifyService, executor, "Usage: @wiki/fact/index [rebuild]", ErrorMessages.Returns.BadArgumentsToWikiCommand);
		else if (!(await WikiCommandHelper.ReaderAsync(parser, executor)).Has(PortalPermission.WikiAdmin))
			return await RefusedAsync(notifyService, executor, "Permission denied. That needs the wiki.admin permission.", ErrorMessages.Returns.PermissionDenied);
		else
			status = await parser.ServiceProvider.GetRequiredService<IWikiFactIndexBuilder>().RebuildAsync();

		await notifyService.Notify(executor, status.Phase switch
		{
			WikiFactIndexPhase.Ready => $"WIKI: The fact index is ready: {status.Pages} pages.",
			WikiFactIndexPhase.Building => $"WIKI: The fact index is building: {status.Indexed} of {status.Pages} pages.",
			_ => "WIKI: The fact index is clearing; it builds again after."
		}, executor);
		return MarkupText.Plain($"{status.Phase.ToString().ToLowerInvariant()} {status.Indexed} {status.Pages}");
	}

	private static async ValueTask<MString> StatedAsync(IWikiService wikiService, INotifyService notifyService, AnySharpObject executor,
		SemanticSchema schema, WikiVisibility visibility, WikiPage updated, string property)
	{
		var stated = updated.Facts.Where(f => f.Property == property).ToList();
		var shown = await WikiFactEntry.ShownAsync(wikiService, schema, stated, visibility, page => page.Title);
		await notifyService.Notify(executor, shown.Count == 0
			? $"WIKI: '{updated.Title}' states no {property}."
			: $"WIKI: '{updated.Title}' {property}: {string.Join(" | ", shown.Select(f => f.Value))}.", executor);
		return MarkupText.Plain(string.Join('|', stated.Select(f => f.Value)));
	}

	private static async ValueTask<MString> RefusedAsync(INotifyService notifyService, AnySharpObject executor, string why, string returned)
	{
		await notifyService.Notify(executor, $"WIKI: {why}", executor);
		return MarkupText.Plain(returned);
	}
}
