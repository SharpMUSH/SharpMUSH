using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// Administrative @wiki subcommands: delete, protect/unprotect (Wizard-only),
/// publish/unpublish (Wizard-only), and category/tag metadata edits.
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
		Category,
		Tag,
	}

	private static bool RequiresWizard(Operation op) =>
		op is Operation.Delete or Operation.Protect or Operation.Unprotect
			or Operation.Publish or Operation.Unpublish;

	public static async ValueTask<MString> Handle(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		MString? valueArg,
		Operation op)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		if (RequiresWizard(op) && !await executor.IsWizard())
		{
			await notifyService.Notify(executor, "WIKI: Permission denied. That operation is wizard-only.", executor);
			return MModule.single(ErrorMessages.Returns.PermissionDenied);
		}

		var (ns, slug) = WikiCommandHelper.ResolveTarget(targetArg.ToPlainText());
		var lookup = await wikiService.GetBySlugAsync(slug, ns);
		if (lookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;

		// Metadata edits on protected pages follow the same rule as content edits.
		if (op is Operation.Category or Operation.Tag && !await WikiCommandHelper.CanEdit(executor, page))
		{
			await notifyService.Notify(executor, $"WIKI: '{page.Title}' is protected. Only wizards may edit it.", executor);
			return MModule.single(ErrorMessages.Returns.PermissionDenied);
		}

		switch (op)
		{
			case Operation.Delete:
				await wikiService.DeleteAsync(page.Id, WikiCommandHelper.EditorDbref(executor));
				await notifyService.Notify(executor, $"WIKI: Deleted '{page.Title}' and its revision history.", executor);
				return MModule.single(page.Slug);

			case Operation.Protect or Operation.Unprotect:
			{
				var protect = op == Operation.Protect;
				await wikiService.SetProtectionAsync(page.Id, protect);
				await notifyService.Notify(executor,
					$"WIKI: '{page.Title}' is now {(protect ? "protected (wizard-only edits)" : "unprotected")}.", executor);
				return MModule.single(page.Slug);
			}

			case Operation.Publish or Operation.Unpublish:
			{
				var publish = op == Operation.Publish;
				await wikiService.SetMetadataAsync(page.Id, page.Category, page.Tags, publish);
				await notifyService.Notify(executor,
					$"WIKI: '{page.Title}' is now {(publish ? "published" : "an unpublished draft")}.", executor);
				return MModule.single(page.Slug);
			}

			case Operation.Category:
			{
				var category = valueArg?.ToPlainText().Trim();
				await wikiService.SetMetadataAsync(page.Id, category, page.Tags, page.Published);
				await notifyService.Notify(executor,
					string.IsNullOrWhiteSpace(category)
						? $"WIKI: Cleared the category on '{page.Title}'."
						: $"WIKI: '{page.Title}' is now in category '{category.ToLowerInvariant()}'.", executor);
				return MModule.single(page.Slug);
			}

			case Operation.Tag:
			{
				// Tags are supplied as a space-separated list; the service normalises them.
				var tags = (valueArg?.ToPlainText() ?? string.Empty)
					.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				var result = await wikiService.SetMetadataAsync(page.Id, page.Category, tags, page.Published);
				var stored = result.IsT0 && result.AsT0.Tags.Count > 0
					? string.Join(", ", result.AsT0.Tags)
					: "(none)";
				await notifyService.Notify(executor, $"WIKI: Tags on '{page.Title}' set to: {stored}.", executor);
				return MModule.single(page.Slug);
			}

			default:
				return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}
	}
}
