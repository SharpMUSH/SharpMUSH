using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// <c>@wiki/require &lt;target&gt;=&lt;action&gt; &lt;permission&gt;…[, …]</c> and <c>@wiki/access &lt;target&gt;[=&lt;player&gt;]</c>:
/// what a namespace, category or page requires, and what a player may do with a page
/// (<see cref="IWikiAccessService"/>).
/// </summary>
public static class AccessWiki
{
	/// <summary>A namespace, a category, or a page that exists.</summary>
	private abstract record Target(WikiRuleTarget Rule, string Label);

	private sealed record AreaTarget(WikiRuleTarget Rule, string Label) : Target(Rule, Label);

	private sealed record PageTarget(WikiRuleTarget Rule, string Label, WikiPage Page) : Target(Rule, Label);

	/// <summary><c>@wiki/require &lt;target&gt;=&lt;action&gt; [&lt;permission&gt; …][, …]</c>.</summary>
	public static async ValueTask<MString> Require(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		MString? valueArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var reader = await WikiCommandHelper.ReaderAsync(parser, executor);
		if (!reader.Has(PortalPermission.WikiAdmin))
		{
			await notifyService.Notify(executor, "WIKI: Permission denied. Setting requirements needs the wiki.admin permission.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.PermissionDenied);
		}

		if (await ResolveAsync(wikiService, targetArg.ToPlainText()) is not Target target)
		{
			await notifyService.Notify(executor, $"WIKI: No such page, namespace or category: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		if (ParseChanges(valueArg?.ToPlainText() ?? string.Empty) is not { } changes)
		{
			await notifyService.Notify(executor,
				"WIKI: Say what each action requires: @wiki/require <target>=<action> <permission> ...[, <action> ...]. "
				+ "Actions are read, create, edit and delete; an action with no permissions requires nothing.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var result = await WikiCommandHelper.Access(parser).SetRequirementsAsync(reader,
			WikiCommandHelper.EditorDbref(executor), target.Rule, changes);
		switch (result)
		{
			case WikiRequirements requirements:
				var lines = new List<MString> { MarkupText.Plain($"WIKI: {target.Label} now requires:") };
				lines.AddRange(Describe(requirements.For(target.Rule)));
				var output = MarkupText.Join(MarkupText.NewLine, lines);
				await notifyService.Notify(executor, output, executor);
				return MarkupText.Plain(target.Rule.ToString());
			case Error<string> error:
				await notifyService.Notify(executor, $"WIKI: {error.Value}", executor);
				return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
			default:
				await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
				return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}
	}

	/// <summary>
	/// <c>@wiki/access &lt;target&gt;</c> lists what a namespace, category or page requires; for a page, also what
	/// it inherits. <c>@wiki/access &lt;page&gt;=&lt;player&gt;</c> says, action by action, whether that player may
	/// act on the page and why; another player's needs wiki.admin.
	/// </summary>
	public static async ValueTask<MString> Show(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		MString? playerArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var access = WikiCommandHelper.Access(parser);
		var me = await WikiCommandHelper.ReaderAsync(parser, executor);
		var targetText = targetArg.ToPlainText().Trim();

		var target = await ResolveAsync(wikiService, targetText);
		// A draft the reader may not see answers as a missing page, as everywhere else.
		if (target is PageTarget hidden && !await access.CanSeeAsync(me, hidden.Page))
			target = null;
		if (target is null)
		{
			await notifyService.Notify(executor, $"WIKI: No such page, namespace or category: {targetText}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var requirements = await access.RequirementsAsync();
		var lines = new List<MString>();
		var playerText = playerArg?.ToPlainText().Trim() ?? string.Empty;

		if (playerText.Length == 0)
		{
			lines.Add(MarkupText.Plain($"WIKI: {target.Label} requires:"));
			lines.AddRange(Describe(requirements.For(target.Rule)));
			if (target is PageTarget page)
			{
				var inherited = new[] { WikiRuleTarget.ForNamespace(page.Page.Namespace) }
					.Concat(page.Page.Categories.Select(WikiRuleTarget.ForCategory))
					.Select(requirements.For)
					.OfType<WikiRequirementSet>()
					.ToList();
				foreach (var set in inherited)
				{
					lines.Add(MarkupText.Plain($"  From {set.Target}:"));
					lines.AddRange(Describe(set).Select(line => MarkupText.Plain("  " + line.ToPlainText())));
				}
			}
		}
		else
		{
			if (target is not PageTarget page)
			{
				await notifyService.Notify(executor, "WIKI: Checking a player's access needs a page, not a namespace or category.", executor);
				return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
			}

			var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
			if (await locate.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, playerText.TrimStart('*'))
				is not (AnySharpObject who and SharpPlayer player))
				return MarkupText.Plain(ErrorMessages.Returns.NoSuchObject);

			if (player.Object.Key != executor.Object().Key && !me.Has(PortalPermission.WikiAdmin))
			{
				await notifyService.Notify(executor, "WIKI: Permission denied. Checking another player's access needs the wiki.admin permission.", executor);
				return MarkupText.Plain(ErrorMessages.Returns.PermissionDenied);
			}

			var reader = await access.ForObjectAsync(who);
			lines.Add(MarkupText.Plain($"WIKI: What {player.Object.Name} may do with {target.Label}:"));
			foreach (var action in Enum.GetValues<WikiAction>().Where(a => a != WikiAction.Create))
			{
				var decision = await access.DecideAsync(reader, page.Page, action);
				lines.Add(MarkupText.Plain($"  {action.ToString().ToLowerInvariant(),-7} {decision.Describe()}"));
			}

			if (!access.MaySeeDraft(reader, page.Page))
				lines.Add(MarkupText.Plain("  The page is a draft, which needs wiki.drafts to read."));
		}

		var output = MarkupText.Join(MarkupText.NewLine, lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}

	/// <summary>
	/// <c>namespace &lt;name&gt;</c>, <c>category &lt;name&gt;</c>, or a page title as <c>@wiki</c> takes it; null when
	/// it names nothing. A <c>Category:Lore</c> title is the category's own page, not the category.
	/// </summary>
	private static async ValueTask<Target?> ResolveAsync(IWikiService wikiService, string text)
	{
		var trimmed = text.Trim();
		var space = trimmed.IndexOf(' ');
		var head = space > 0 ? trimmed[..space] : trimmed;
		var rest = space > 0 ? trimmed[(space + 1)..].Trim() : string.Empty;

		if (head.Equals("namespace", StringComparison.OrdinalIgnoreCase) && rest.Length > 0)
		{
			return WikiHelpers.ParseNamespace(rest) is { } ns
				? new AreaTarget(WikiRuleTarget.ForNamespace(ns), $"Namespace {WikiHelpers.NamespaceName(ns)}")
				: null;
		}

		if (head.Equals("category", StringComparison.OrdinalIgnoreCase) && rest.Length > 0)
		{
			var key = WikiHelpers.CategoryKey(rest);
			return key.Length > 0 ? new AreaTarget(WikiRuleTarget.ForCategory(key), $"Category {key}") : null;
		}

		var (pageNs, slug) = WikiHelpers.ResolveTitle(trimmed);
		return await wikiService.GetBySlugAsync(slug, pageNs) is WikiPage page
			? new PageTarget(WikiRuleTarget.ForPage(page.Id), $"'{page.Title}'", page)
			: null;
	}

	/// <summary>
	/// <c>edit lore.edit wiki.edit, read</c> → edit needs both, read needs nothing; null when a clause does not
	/// start with an action.
	/// </summary>
	private static Dictionary<WikiAction, IReadOnlyList<string>>? ParseChanges(string text)
	{
		var changes = new Dictionary<WikiAction, IReadOnlyList<string>>();
		foreach (var clause in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var words = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (!Enum.TryParse<WikiAction>(words[0], ignoreCase: true, out var action) || int.TryParse(words[0], out _))
				return null;
			changes[action] = words[1..];
		}

		return changes.Count > 0 ? changes : null;
	}

	private static IEnumerable<MString> Describe(WikiRequirementSet? set)
	{
		var lines = Enum.GetValues<WikiAction>()
			.Where(action => set?.For(action).Count > 0)
			.Select(action => MarkupText.Plain($"  {action.ToString().ToLowerInvariant(),-7} {string.Join(" ", set!.For(action))}"))
			.ToList();
		return lines.Count > 0 ? lines : [MarkupText.Plain("  nothing beyond the wiki.* permissions")];
	}
}
