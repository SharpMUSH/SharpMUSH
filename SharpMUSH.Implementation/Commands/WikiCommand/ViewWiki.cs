using Mediator;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// @wiki &lt;page&gt; / @wiki/view &lt;page&gt; — display a wiki page rendered for the
/// terminal, and @wiki/history &lt;page&gt; — show the revision log.
/// </summary>
public static class ViewWiki
{
	private const int RenderWidth = 78;

	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource">
	/// <c>/SOURCE</c>: skip localization entirely and render the page as authored. Distinct from a null
	/// <paramref name="locale"/>, which still resolves through the configured default.
	/// </param>
	public static async ValueTask<MString> Handle(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString target,
		string? locale = null,
		bool forceSource = false)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, category, slug) = WikiCommandHelper.ResolveTarget(target.ToPlainText());

		var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
		if (lookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {target.ToPlainText().Trim()}", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;

		// In-game readers see draft translations only if they could edit the page; @wiki has no per-locale
		// permission of its own, so reuse the page-edit gate the write paths already use.
		var includeDrafts = await WikiCommandHelper.CanEdit(executor, page);
		var localized = forceSource
			? null
			: await localization.LocalizeAsync(page, locale, includeDrafts);

		var title = localized?.Title ?? page.Title;
		var markdown = localized?.MarkdownSource ?? page.MarkdownSource;
		var revision = localized?.RevisionNumber ?? page.RevisionNumber;
		var published = localized?.Published ?? page.Published;
		// Only shown when a different language was served than asked for — silently handing a reader
		// English is how a translation gap goes unnoticed in game exactly as it does on the web.
		var localeMarker = localized is { IsFallback: true } ? $" [{localized.Locale}]" : string.Empty;

		var line = MModule.repeat(MModule.single("-"), RenderWidth);
		var markers = $"{(published ? "" : " (draft)")}{(page.IsProtected ? " (protected)" : "")}";
		var tags = page.Tags.Count > 0 ? string.Join(", ", page.Tags) : "-";

		var rendered = RecursiveMarkdownHelper.RenderMarkdown(markdown, RenderWidth, parser);

		var output = MModule.multipleWithDelimiter(MModule.single("\n"),
		[
			line,
			MModule.single($"Wiki: {title} [{page.Namespace}]{markers}"),
			MModule.single($"Category: {page.Category ?? "-"}   Tags: {tags}   Rev {revision}{localeMarker} — {page.UpdatedAt:yyyy-MM-dd HH:mm}"),
			line,
			rendered,
			line,
		]);

		await notifyService.Notify(executor, output, executor);
		return output;
	}

	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource"><c>/SOURCE</c>: show the source locale's stream whatever the reader's is.</param>
	public static async ValueTask<MString> History(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString target,
		string? locale = null,
		bool forceSource = false)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, category, slug) = WikiCommandHelper.ResolveTarget(target.ToPlainText());

		var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
		if (lookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {target.ToPlainText().Trim()}", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;

		// Revision numbering restarts at 1 per locale, so the stream has to be picked explicitly — the same
		// reason GET /revisions takes ?lang=. It must be picked from the locale actually *served*, not the
		// one asked for: resolving the requested locale directly would show a `de` reader an empty history
		// for a page they can read perfectly well in English, which is a read failing for locale reasons.
		// This mirrors WikiController.ResolveRevisionStreamAsync exactly.
		var includeDrafts = await WikiCommandHelper.CanEdit(executor, page);
		var localized = forceSource ? null : await localization.LocalizeAsync(page, locale, includeDrafts);
		var stream = localized is null
			|| string.Equals(localized.Locale, localization.SourceLocaleOf(page), StringComparison.OrdinalIgnoreCase)
			? string.Empty
			: localized.Locale;

		var revisions = stream.Length == 0
			? await wikiService.GetRevisionsAsync(page.Id)
			: await wikiService.GetRevisionsForLocaleAsync(page.Id, stream, 0, 20);

		var streamMarker = stream.Length == 0 ? string.Empty : $" ({stream})";
		var lines = new List<MString>
		{
			MModule.single($"WIKI: Revision history for {localized?.Title ?? page.Title} [{page.Namespace}]{streamMarker}:"),
		};
		lines.AddRange(revisions.Select(r => MModule.single(
			$"  r{r.RevisionNumber,-4} {r.Timestamp:yyyy-MM-dd HH:mm}  by {r.EditorDbref,-8} {r.EditSummary ?? ""}".TrimEnd())));

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}
}
