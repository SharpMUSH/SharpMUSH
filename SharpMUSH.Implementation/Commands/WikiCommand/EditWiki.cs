using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// @wiki/create &lt;title&gt;=&lt;markdown&gt;, @wiki/edit &lt;page&gt;=&lt;markdown&gt;,
/// @wiki/append &lt;page&gt;=&lt;markdown&gt;, @wiki/translate &lt;page&gt;/&lt;lang&gt;=&lt;markdown&gt;
/// — content authoring subcommands.
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

		// The title may carry namespace/category prefixes ("Help:Guides:Some Topic" or
		// "Help:Some Topic"); the remainder is the human title (the slug is derived from it).
		var rawTitle = titleArg.ToPlainText().Trim();
		var (ns, category, _) = WikiCommandHelper.ResolveTarget(rawTitle);
		var parts = rawTitle.Split(':', 3);
		var title = parts.Length == 3
				&& Enum.TryParse<SharpMUSH.Library.Models.Wiki.WikiNamespace>(parts[0].Trim(), ignoreCase: true, out _)
			? parts[2].Trim()
			: parts.Length == 2
				&& Enum.TryParse<SharpMUSH.Library.Models.Wiki.WikiNamespace>(parts[0].Trim(), ignoreCase: true, out _)
				? parts[1].Trim()
				: rawTitle;

		if (title.Length == 0)
		{
			await notifyService.Notify(executor, "WIKI: A page needs a title.", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		// Stamped at birth, exactly as the API create path is: SourceLocale is materialised once and never
		// re-derived on read, so a page that misses its stamp here would need the migration to rescue it.
		// This is the second and last create path in the codebase.
		var localization = parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>();
		var result = await wikiService.CreateAsync(
			title, contentArg.ToPlainText(), WikiCommandHelper.EditorDbref(executor), ns, category,
			localization.DefaultLocale);

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

	public static async ValueTask<MString> Rollback(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		MString revisionArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, category, slug) = WikiCommandHelper.ResolveTarget(targetArg.ToPlainText());

		if (!int.TryParse(revisionArg.ToPlainText().Trim(), out var revisionNumber) || revisionNumber < 1)
		{
			await notifyService.Notify(executor, "WIKI: Rollback needs a revision number (see @wiki/history).", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
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

		var revisionLookup = await wikiService.GetRevisionAsync(page.Id, revisionNumber);
		if (revisionLookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: '{page.Title}' has no revision r{revisionNumber}.", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		// A rollback is a normal edit — it creates a NEW revision, so history
		// is preserved and the rollback itself can be rolled back.
		var result = await wikiService.UpdateAsync(
			page.Id, revisionLookup.AsT0.MarkdownSource, WikiCommandHelper.EditorDbref(executor),
			$"rollback to r{revisionNumber} via @wiki/rollback");

		return await result.Match(
			async updated =>
			{
				await notifyService.Notify(executor,
					$"WIKI: Restored '{updated.Title}' to r{revisionNumber} (now rev {updated.RevisionNumber}).", executor);
				return MModule.single(updated.Slug);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
				return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
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
		var (ns, category, slug) = WikiCommandHelper.ResolveTarget(targetArg.ToPlainText());

		var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
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

	/// <summary>
	/// <c>@wiki/translate &lt;page&gt;/&lt;lang&gt;=&lt;markdown&gt;</c> — write one locale's translation of a
	/// page. The counterpart of <c>PUT /api/wiki/{slug}/translations/{locale}</c>, and the only in-game
	/// write that touches a translation row rather than the source.
	/// </summary>
	/// <remarks>
	/// <paramref name="targetArg"/> carries the locale, and it is mandatory: unlike every read path, this
	/// one never consults the executor's <c>LOCALE</c>. See <see cref="WikiCommandHelper.SplitLocaleTarget"/>.
	/// </remarks>
	public static async ValueTask<MString> Translate(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString targetArg,
		MString contentArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var split = WikiCommandHelper.SplitLocaleTarget(targetArg.ToPlainText());
		if (split.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: {split.AsT1.Value}", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var (pageTarget, locale) = split.AsT0;
		var (ns, category, slug) = WikiCommandHelper.ResolveTarget(pageTarget);

		var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
		if (lookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {pageTarget}", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;

		// A translation is an edit to the page, gated exactly as one — the same rule the API's
		// PUT .../translations/{locale} applies, and no new permission of its own.
		if (!await WikiCommandHelper.CanEdit(executor, page))
		{
			await notifyService.Notify(executor, $"WIKI: '{page.Title}' is protected. Only wizards may edit it.", executor);
			return MModule.single(ErrorMessages.Returns.PermissionDenied);
		}

		// The store refuses this too ("no row may shadow the source"), but only as a raw Error<string>
		// naming a page id. Catching it here is what turns it into an instruction.
		if (string.Equals(localization.SourceLocaleOf(page), locale, StringComparison.OrdinalIgnoreCase))
		{
			await notifyService.Notify(executor,
				$"WIKI: '{page.Title}' is written in {locale}; use @wiki/edit to change the page itself.", executor);
			return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		// The compare-and-swap baseline is the row as it stands right now, which is the closest thing a
		// one-shot command has to the web editor's "revision I loaded". Passing null instead would make
		// every write after the first an AlreadyExists conflict, i.e. a translation nobody could ever update.
		var existing = await wikiService.GetTranslationAsync(page.Id, locale);
		var expectedRevision = existing.IsT0 ? existing.AsT0.RevisionNumber : (int?)null;

		// Title and Published belong to the translation, and this command supplies neither: an existing row
		// keeps both, so an in-game body edit cannot silently retitle or publish what the web is drafting.
		// A brand-new row is born published — @wiki has no per-translation publish switch, so a draft created
		// here would be invisible to every in-game reader including its author, with no way to reveal it. The
		// page's own draft state still hides the whole page either way.
		var title = existing.IsT0 ? existing.AsT0.Title : page.Title;
		var published = !existing.IsT0 || existing.AsT0.Published;

		var result = await wikiService.UpsertTranslationAsync(
			page.Id, locale, title, contentArg.ToPlainText(), WikiCommandHelper.EditorDbref(executor),
			"translated in-game via @wiki/translate", published, expectedRevision);

		return await result.Match(
			async translation =>
			{
				await notifyService.Notify(executor,
					$"WIKI: Wrote the {translation.Locale} translation of '{page.Title}' (now rev {translation.RevisionNumber}).",
					executor);
				return MModule.single(page.Slug);
			},
			async conflict =>
			{
				// A lost race, never a retry: re-reading the winner's revision and writing again would put
				// this translator's stale prose on top of theirs, which is the loss the compare-and-swap
				// exists to prevent. The text stays in the player's scrollback to be re-applied by hand.
				await notifyService.Notify(executor, $"WIKI: {ConflictMessage(conflict, locale, page.Title)}", executor);
				return MModule.single(ErrorMessages.Returns.WikiWriteConflict);
			},
			async err =>
			{
				await notifyService.Notify(executor, $"WIKI: {err.Value}", executor);
				return MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand);
			});
	}

	/// <summary>
	/// Human wording for a <see cref="WikiWriteConflict"/>, in-game. Deliberately parallel to
	/// <c>WikiController.ConflictMessage</c>: the phrasing is presentation and belongs to each surface,
	/// which is why the storage layer returns an enum rather than a sentence.
	/// </summary>
	private static string ConflictMessage(WikiWriteConflict conflict, string locale, string pageTitle) =>
		conflict switch
		{
			WikiWriteConflict.AlreadyExists =>
				$"The {locale} translation of '{pageTitle}' was created while you were typing. Read it first, then re-apply your text.",
			WikiWriteConflict.TranslationGone =>
				$"The {locale} translation of '{pageTitle}' was deleted while you were typing. Nothing was written.",
			_ =>
				$"Somebody else saved the {locale} translation of '{pageTitle}' while you were typing. Nothing was written — read it and re-apply your text."
		};
}
