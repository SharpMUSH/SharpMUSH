using SharpMUSH.Library.Authorization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// Administrative @wiki subcommands: delete (a page action, see <see cref="IWikiAccessService"/>), and
/// protect/unprotect and publish/unpublish, which need wiki.admin. Categories are an edit, set by
/// <c>@wiki/category page=list</c> (<see cref="EditWiki"/>).
/// </summary>
public static class ManageWiki
{
	/// <summary>Operations dispatched through <see cref="Handle"/>.</summary>
	public enum Operation
	{
		Delete,
		Protect,
		Unprotect,
		Publish,
		Unpublish,
	}

	public static async ValueTask<MString> Handle(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		Operation op)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var reader = await WikiCommandHelper.ReaderAsync(parser, executor);

		// Deleting is a page action like editing (wiki.delete plus what the page requires); the rest manage
		// the wiki and need wiki.admin.
		if (op != Operation.Delete && !reader.Has(PortalPermission.WikiAdmin))
		{
			await notifyService.Notify(executor, "WIKI: Permission denied. That needs the wiki.admin permission.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.PermissionDenied);
		}

		var (ns, slug) = WikiHelpers.ResolveTitle(targetArg.ToPlainText());
		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		switch (op)
		{
			case Operation.Delete:
				if (await WikiCommandHelper.RefusalAsync(parser, notifyService, executor, page, WikiAction.Delete,
					targetArg.ToPlainText()) is { } refused)
					return refused;
				await wikiService.DeleteAsync(page.Id, WikiCommandHelper.EditorDbref(executor));
				await notifyService.Notify(executor, $"WIKI: Deleted '{page.Title}' and its revision history.", executor);
				return MarkupText.Plain(page.Slug);

			// Protection is a page requirement of wiki.admin to edit and delete, the shortcut for the
			// common case of @wiki/require.
			case Operation.Protect or Operation.Unprotect:
				{
					var protect = op == Operation.Protect;
					var changes = protect
						? WikiRequirementSet.Protection
						: new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Edit] = [], [WikiAction.Delete] = [] };
					switch (await WikiCommandHelper.Access(parser).SetRequirementsAsync(reader,
						WikiCommandHelper.EditorDbref(executor), WikiRuleTarget.ForPage(page.Id), changes))
					{
						case WikiRequirements:
							await notifyService.Notify(executor,
								$"WIKI: '{page.Title}' is now {(protect ? "protected (editing and deleting need wiki.admin)" : "unprotected")}.", executor);
							return MarkupText.Plain(page.Slug);
						case Error<string> error:
							await notifyService.Notify(executor, $"WIKI: {error.Value}", executor);
							return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
						default:
							await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
							return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
					}
				}

			case Operation.Publish or Operation.Unpublish:
				{
					var publish = op == Operation.Publish;
					var publishResult = await wikiService.SetMetadataAsync(page.Id, page.Categories, publish);
					if (publishResult is NotFound)
					{
						await notifyService.Notify(executor, $"WIKI: Could not update '{page.Title}' (it may have just been deleted).", executor);
						return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
					}
					await notifyService.Notify(executor,
						$"WIKI: '{page.Title}' is now {(publish ? "published" : "an unpublished draft")}.", executor);
					return MarkupText.Plain(page.Slug);
				}

			default:
				return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}
	}
}
