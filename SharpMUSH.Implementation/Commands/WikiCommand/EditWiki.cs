using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// @wiki/create &lt;title&gt;=&lt;markdown&gt;, @wiki/edit &lt;page&gt;=&lt;markdown&gt;,
/// @wiki/append &lt;page&gt;=&lt;markdown&gt; — content authoring subcommands.
/// </summary>
public static class EditWiki
{
	public static async ValueTask<MString> Create(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString titleArg,
		MString contentArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		// The title may carry a namespace prefix ("Help:Some Topic"); the remainder
		// is the human title (the slug is derived from it by the service).
		var rawTitle = titleArg.ToPlainText().Trim();
		var (ns, _) = WikiCommandHelper.ResolveTarget(rawTitle);
		var colonIdx = rawTitle.IndexOf(':');
		var title = colonIdx > 0 && Enum.TryParse<SharpMUSH.Library.Models.Wiki.WikiNamespace>(
			rawTitle[..colonIdx].Trim(), ignoreCase: true, out _)
			? rawTitle[(colonIdx + 1)..].Trim()
			: rawTitle;

		if (title.Length == 0)
		{
			await notifyService.Notify(executor, "WIKI: A page needs a title.", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var result = await wikiService.CreateAsync(
			title, contentArg.ToPlainText(), WikiCommandHelper.EditorDbref(executor), ns);

		return await result.Match(
			async page =>
			{
				await notifyService.Notify(executor,
					$"WIKI: Created page '{page.Title}' ({WikiCommandHelper.DisplayReference(page)}).", executor);
				return MModule.single(page.Slug);
			},
			async err =>
			{
				await notifyService.Notify(executor, $"WIKI: {err.Value}", executor);
				return MModule.single($"#-1 {err.Value.ToUpperInvariant()}");
			});
	}

	public static async ValueTask<MString> Edit(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		MString contentArg,
		bool append)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, slug) = WikiCommandHelper.ResolveTarget(targetArg.ToPlainText());

		var lookup = await wikiService.GetBySlugAsync(slug, ns);
		if (lookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;
		if (!await WikiCommandHelper.CanEdit(executor, page))
		{
			await notifyService.Notify(executor, $"WIKI: '{page.Title}' is protected. Only wizards may edit it.", executor);
			return MModule.single(ErrorMessages.Returns.PermissionDenied);
		}

		var newContent = append
			? $"{page.MarkdownSource.TrimEnd()}\n\n{contentArg.ToPlainText()}"
			: contentArg.ToPlainText();
		var summary = append ? "appended in-game via @wiki/append" : "edited in-game via @wiki/edit";

		var result = await wikiService.UpdateAsync(
			page.Id, newContent, WikiCommandHelper.EditorDbref(executor), summary);

		return await result.Match(
			async updated =>
			{
				await notifyService.Notify(executor,
					$"WIKI: {(append ? "Appended to" : "Updated")} '{updated.Title}' (now rev {updated.RevisionNumber}).", executor);
				return MModule.single(updated.Slug);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
				return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
			});
	}
}
