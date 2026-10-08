using Mediator;
using Microsoft.Extensions.DependencyInjection;
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

		// The title may carry a namespace prefix ("Help:Some Topic"); the remainder is the human title
		// (the slug is derived from it).
		var (ns, title) = WikiHelpers.SplitTitle(titleArg.ToPlainText());

		if (title.Length == 0)
		{
			await notifyService.Notify(executor, "WIKI: A page needs a title.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var decision = await WikiCommandHelper.Access(parser).DecideCreateAsync(
			await WikiCommandHelper.ReaderAsync(parser, executor), WikiHelpers.NamespaceName(ns), []);
		if (!decision.Allowed)
		{
			await notifyService.Notify(executor, $"WIKI: You can't create pages in {WikiHelpers.NamespaceName(ns)}: {decision.Describe()}.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.PermissionDenied);
		}

		// Stamped at birth, exactly as the API create path is: SourceLocale is materialised once and never
		// re-derived on read, so a page that misses its stamp here would need the migration to rescue it.
		// This is the second and last create path in the codebase.
		var localization = parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>();
		var result = await wikiService.CreateAsync(
			title, contentArg.ToPlainText(), WikiCommandHelper.EditorDbref(executor), ns,
			localization.DefaultLocale);

		var (message, returned) = result switch
		{
			WikiPage page => (
				$"WIKI: Created page '{page.Title}' ({WikiCommandHelper.DisplayReference(page)}).",
				MarkupText.Plain(page.Slug)),
			Error<string> err => ($"WIKI: {err.Value}", MarkupText.Plain($"#-1 {err.Value.ToUpperInvariant()}"))
		};

		await notifyService.Notify(executor, message, executor);
		return returned;
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
		var (ns, slug) = WikiHelpers.ResolveTitle(targetArg.ToPlainText());

		if (!int.TryParse(revisionArg.ToPlainText().Trim(), out var revisionNumber) || revisionNumber < 1)
		{
			await notifyService.Notify(executor, "WIKI: Rollback needs a revision number (see @wiki/history).", executor);
			return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		if (await WikiCommandHelper.RefusalAsync(parser, notifyService, executor, page, WikiAction.Edit,
			targetArg.ToPlainText()) is { } refused)
			return refused;

		if (await wikiService.GetRevisionAsync(page.Id, revisionNumber) is not WikiRevision revision)
		{
			await notifyService.Notify(executor, $"WIKI: '{page.Title}' has no revision r{revisionNumber}.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		// A rollback is a normal edit — it creates a NEW revision, so history
		// is preserved and the rollback itself can be rolled back.
		var result = await wikiService.UpdateAsync(
			page.Id, revision.MarkdownSource, WikiCommandHelper.EditorDbref(executor),
			$"rollback to r{revisionNumber} via @wiki/rollback");

		if (result is not WikiPage updated)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		await notifyService.Notify(executor,
			$"WIKI: Restored '{updated.Title}' to r{revisionNumber} (now rev {updated.RevisionNumber}).", executor);
		return MarkupText.Plain(updated.Slug);
	}

	/// <summary>
	/// <c>@wiki/category &lt;page&gt;=&lt;name&gt;, &lt;name&gt;…</c> — replace the categories a page is in. An empty
	/// list takes it out of every category. Categories are page metadata, not text, so this is no revision.
	/// The same edit rule as <c>@wiki/edit</c> applies.
	/// </summary>
	public static async ValueTask<MString> SetCategories(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString targetArg,
		MString? categoriesArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, slug) = WikiHelpers.ResolveTitle(targetArg.ToPlainText());

		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		if (await WikiCommandHelper.RefusalAsync(parser, notifyService, executor, page, WikiAction.Edit,
			targetArg.ToPlainText()) is { } refused)
			return refused;

		var names = (categoriesArg?.ToPlainText() ?? string.Empty).Split(',');
		var access = WikiCommandHelper.Access(parser);
		var reader = await WikiCommandHelper.ReaderAsync(parser, executor);
		var filing = await access.DecideCategoriesAsync(reader, page, names);
		if (!filing.Allowed)
		{
			await notifyService.Notify(executor, $"WIKI: You can't file '{page.Title}' there: {filing.Describe()}.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.PermissionDenied);
		}
		if (await wikiService.SetMetadataAsync(page.Id, names, page.Published) is not WikiPage updated)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		// A new category keeps the name as typed, as its category page's title, when the executor may make one.
		if ((await access.DecideCreateAsync(reader, WikiHelpers.NamespaceName(WikiNamespace.Category), [])).Allowed)
			await wikiService.NameCategoriesAsync(names, page.Categories, WikiCommandHelper.EditorDbref(executor),
				localization.DefaultLocale);

		var categoryNames = await localization.GetCategoryNamesAsync(await WikiCommandHelper.ResolveExecutorLocaleAsync(parser, executor),
			await WikiCommandHelper.VisibilityAsync(parser, executor));
		var labels = string.Join(", ", updated.Categories.Select(c => WikiHelpers.CategoryLabel(c, categoryNames)));
		await notifyService.Notify(executor, updated.Categories.Count == 0
			? $"WIKI: '{updated.Title}' is in no category."
			: $"WIKI: '{updated.Title}' categories: {labels}.", executor);
		return MarkupText.Plain(string.Join(' ', updated.Categories));
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
		var (ns, slug) = WikiHelpers.ResolveTitle(targetArg.ToPlainText());

		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		if (await WikiCommandHelper.RefusalAsync(parser, notifyService, executor, page, WikiAction.Edit,
			targetArg.ToPlainText()) is { } refused)
			return refused;

		var newContent = append
			? $"{page.MarkdownSource.TrimEnd()}\n\n{contentArg.ToPlainText()}"
			: contentArg.ToPlainText();
		var summary = append ? "appended in-game via @wiki/append" : "edited in-game via @wiki/edit";

		var result = await wikiService.UpdateAsync(
			page.Id, newContent, WikiCommandHelper.EditorDbref(executor), summary);

		if (result is not WikiPage updated)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		await notifyService.Notify(executor,
			$"WIKI: {(append ? "Appended to" : "Updated")} '{updated.Title}' (now rev {updated.RevisionNumber}).", executor);
		return MarkupText.Plain(updated.Slug);
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

		return WikiCommandHelper.SplitLocaleTarget(targetArg.ToPlainText()) switch
		{
			WikiCommandHelper.LocaleTarget target =>
				await WriteTranslation(parser, wikiService, localization, notifyService, executor, target, contentArg),
			Error<string> splitError => await RefuseTranslateTarget(notifyService, executor, splitError.Value),
		};
	}

	/// <summary>
	/// Writes the translation <see cref="Translate"/> was asked for, once its target has split into a page
	/// and a canonical locale.
	/// </summary>
	private static async ValueTask<MString> WriteTranslation(
		IMUSHCodeParser parser,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		AnySharpObject executor,
		WikiCommandHelper.LocaleTarget target,
		MString contentArg)
	{
		var (pageTarget, locale) = target;
		var (ns, slug) = WikiHelpers.ResolveTitle(pageTarget);

		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {pageTarget}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		// A translation is an edit to the page, gated exactly as one — the same rule the API's
		// PUT .../translations/{locale} applies, and no new permission of its own.
		if (await WikiCommandHelper.RefusalAsync(parser, notifyService, executor, page, WikiAction.Edit, pageTarget) is { } refused)
			return refused;

		// The store refuses this too ("no row may shadow the source"), but only as a raw Error<string>
		// naming a page id. Catching it here is what turns it into an instruction.
		if (string.Equals(localization.SourceLocaleOf(page), locale, StringComparison.OrdinalIgnoreCase))
		{
			await notifyService.Notify(executor,
				$"WIKI: '{page.Title}' is written in {locale}; use @wiki/edit to change the page itself.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		// The compare-and-swap baseline is the row as it stands right now, which is the closest thing a
		// one-shot command has to the web editor's "revision I loaded". Passing null instead would make
		// every write after the first an AlreadyExists conflict, i.e. a translation nobody could ever update.
		var existing = await wikiService.GetTranslationAsync(page.Id, locale) is WikiTranslation row ? row : null;
		var expectedRevision = existing?.RevisionNumber;

		// Title and Published belong to the translation, and this command supplies neither: an existing row
		// keeps both, so an in-game body edit cannot silently retitle or publish what the web is drafting.
		// A brand-new row is born published — @wiki has no per-translation publish switch, so a draft created
		// here would be invisible to every in-game reader including its author, with no way to reveal it. The
		// page's own draft state still hides the whole page either way.
		var title = existing?.Title ?? page.Title;
		var published = existing?.Published ?? true;

		var result = await wikiService.UpsertTranslationAsync(
			page.Id, locale, title, contentArg.ToPlainText(), WikiCommandHelper.EditorDbref(executor),
			"translated in-game via @wiki/translate", published, expectedRevision);

		var (message, returned) = result switch
		{
			WikiTranslation translation => (
				$"WIKI: Wrote the {translation.Locale} translation of '{page.Title}' (now rev {translation.RevisionNumber}).",
				MarkupText.Plain(page.Slug)),
			// A lost race, never a retry: re-reading the winner's revision and writing again would put
			// this translator's stale prose on top of theirs, which is the loss the compare-and-swap
			// exists to prevent. The text stays in the player's scrollback to be re-applied by hand.
			WikiWriteConflict conflict => (
				$"WIKI: {ConflictMessage(conflict, locale, page.Title)}",
				MarkupText.Plain(ErrorMessages.Returns.WikiWriteConflict)),
			Error<string> err => ($"WIKI: {err.Value}", MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand))
		};

		await notifyService.Notify(executor, message, executor);
		return returned;
	}

	/// <summary>Tells the executor why a <c>@wiki/translate</c> target names no page and locale.</summary>
	private static async ValueTask<MString> RefuseTranslateTarget(
		INotifyService notifyService, AnySharpObject executor, string reason)
	{
		await notifyService.Notify(executor, $"WIKI: {reason}", executor);
		return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
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
