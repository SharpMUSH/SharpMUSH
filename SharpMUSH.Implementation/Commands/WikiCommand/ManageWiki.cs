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
/// protect/unprotect, publish/unpublish and pin/unpin, which need wiki.admin. Categories are an edit, set by
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

	/// <summary>
	/// <c>@wiki/pin</c> lists the categories pinned to the wiki home; <c>@wiki/pin &lt;category&gt;</c> and
	/// <c>@wiki/unpin &lt;category&gt;</c> change them, and need wiki.admin. Returns the pinned keys.
	/// </summary>
	public static async ValueTask<MString> Pin(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString? categoryArg,
		bool pin)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var names = await localization.GetCategoryNamesAsync(await WikiCommandHelper.ResolveExecutorLocaleAsync(parser, executor),
			await WikiCommandHelper.VisibilityAsync(parser, executor));
		var name = categoryArg?.ToPlainText().Trim() ?? string.Empty;

		if (name.Length > 0)
		{
			if (!(await WikiCommandHelper.ReaderAsync(parser, executor)).Has(PortalPermission.WikiAdmin))
			{
				await notifyService.Notify(executor, "WIKI: Permission denied. That needs the wiki.admin permission.", executor);
				return MarkupText.Plain(ErrorMessages.Returns.PermissionDenied);
			}

			if (name.StartsWith("category:", StringComparison.OrdinalIgnoreCase)) name = name["category:".Length..];
			var label = WikiHelpers.CategoryLabel(name, names);
			switch (await wikiService.SetCategoryPinnedAsync(name, pin))
			{
				case bool changed:
					await notifyService.Notify(executor, (changed, pin) switch
					{
						(true, true) => $"WIKI: Category '{label}' is now pinned to the wiki home.",
						(true, false) => $"WIKI: Category '{label}' is no longer pinned to the wiki home.",
						(false, true) => $"WIKI: Category '{label}' was already pinned.",
						(false, false) => $"WIKI: Category '{label}' was not pinned.",
					}, executor);
					break;
				case Error<string>:
					await notifyService.Notify(executor, "WIKI: Which category?", executor);
					return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
			}
		}

		var pinned = await wikiService.GetPinnedCategoriesAsync();
		if (name.Length == 0)
		{
			await notifyService.Notify(executor, pinned.Count == 0
				? "WIKI: No category is pinned to the wiki home."
				: $"WIKI: Pinned to the wiki home: {string.Join(", ", pinned.Select(key => WikiHelpers.CategoryLabel(key, names)))}.", executor);
		}

		return MarkupText.Plain(string.Join(' ', pinned));
	}
}
