using Mediator;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// @wiki &lt;page&gt; / @wiki/view &lt;page&gt; — display a wiki page rendered for the
/// terminal, and @wiki/history &lt;page&gt; — show the revision log.
/// </summary>
/// <remarks>
/// Both surfaces render unpublished content only under <c>/DRAFT</c> and only for a reader who passes
/// <see cref="WikiCommandHelper.CanSeeDrafts"/>. The header still names the page and marks it
/// <c>(draft)</c>, so a draft reads as withheld rather than as missing.
/// </remarks>
public static class ViewWiki
{
	private const int RenderWidth = 78;

	/// <summary>
	/// The stand-in for an unpublished body or revision log. Deliberately not "no such page": the page
	/// exists, the header above this line already says so, and answering with a lie would make a draft
	/// indistinguishable from a typo for the staff who have to work with both.
	/// </summary>
	/// <param name="what">The withheld part, named in the reader's terms: "body" or "revision history".</param>
	/// <param name="maySeeDrafts">
	/// Adds the <c>/DRAFT</c> hint. Keyed on permission and never on the switch, so a reader who may not
	/// see drafts gets byte-identical output with and without it — the switch can never be used to probe
	/// for a draft's existence.
	/// </param>
	private static MString DraftWithheld(string what, bool maySeeDrafts) =>
		MModule.single($"WIKI: This is a draft; its {what} is not shown."
			+ (maySeeDrafts ? " Add /DRAFT to read it." : string.Empty));

	/// <summary>
	/// Whether the served content may be shown to a reader with no draft permission. Both flags have to
	/// agree, and the page's is the one that can veto.
	/// </summary>
	/// <remarks>
	/// A page and each of its translations carry independent <c>Published</c> flags, and it is the page's
	/// that decides whether the article exists publicly at all. Reading the served row's flag alone let a
	/// published translation speak for an unpublished page: a mortal whose locale matched was handed the
	/// draft in full, unmarked, without asking for <c>/DRAFT</c>. The body and the revision log both call
	/// this rather than deriving it separately — one rule, because two is how the discrepancy arose.
	/// </remarks>
	private static bool IsPublic(WikiPage page, LocalizedWikiPage? localized) =>
		page.Published && (localized?.Published ?? true);

	/// <param name="locale">The reader's locale, or null for the configured default.</param>
	/// <param name="forceSource">
	/// <c>/SOURCE</c>: skip localization entirely and render the page as authored. Distinct from a null
	/// <paramref name="locale"/>, which still resolves through the configured default.
	/// </param>
	/// <param name="showDraft">
	/// <c>/DRAFT</c>: render an unpublished body rather than withholding it. Subject to
	/// <see cref="WikiCommandHelper.CanSeeDrafts"/> — on its own it grants nothing.
	/// </param>
	/// <param name="showRaw">
	/// <c>/MD</c>: show the stored markdown instead of the rendered body, so a builder can read the
	/// source of the page they are about to <c>@wiki/edit</c> without detouring through
	/// <c>wiki(&lt;page&gt;, markdown)</c>. Reveals nothing extra: it changes the presentation of a body
	/// the reader is already allowed to see, and is applied after the same draft gate.
	/// </param>
	public static async ValueTask<MString> Handle(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString target,
		string? locale = null,
		bool forceSource = false,
		bool showDraft = false,
		bool showRaw = false)
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

		// The page-edit gate cannot serve here: it passes every player on every unprotected page, so using
		// it meant a mortal whose LOCALE matched an unpublished translation was handed its body in full.
		// CanSeeDrafts is the one in-game notion of who may see unpublished content, per #740.
		var maySeeDrafts = await WikiCommandHelper.CanSeeDrafts(executor);
		var localized = forceSource
			? null
			: await localization.LocalizeAsync(page, locale, maySeeDrafts);

		var title = localized?.Title ?? page.Title;
		var markdown = localized?.MarkdownSource ?? page.MarkdownSource;
		var revision = localized?.RevisionNumber ?? page.RevisionNumber;
		var published = IsPublic(page, localized);
		// Only shown when a different language was served than asked for — silently handing a reader
		// English is how a translation gap goes unnoticed in game exactly as it does on the web.
		var localeMarker = localized is { IsFallback: true } ? $" [{localized.Locale}]" : string.Empty;

		var line = MModule.repeat(MModule.single("-"), RenderWidth);
		var markers = $"{(published ? "" : " (draft)")}{(page.IsProtected ? " (protected)" : "")}";
		var tags = page.Tags.Count > 0 ? string.Join(", ", page.Tags) : "-";

		// An unpublished body is opt-in even for a wizard: reading a draft is a deliberate act, not the
		// default a stray @wiki on a half-written page should perform. The gate is the same whether the
		// body is rendered or raw — /MD is a presentation choice made after permission, never before it.
		MString rendered;
		if (!published && !(showDraft && maySeeDrafts))
		{
			rendered = DraftWithheld("body", maySeeDrafts);
		}
		else if (showRaw)
		{
			// Byte-exact, deliberately: the point of /MD is source you can paste back into @wiki/edit,
			// so nothing here may wrap, re-indent or otherwise touch it. MModule.single also means the
			// markdown's own [ ] % $ reach the player as text rather than as anything to evaluate.
			rendered = MModule.single(markdown);
		}
		else
		{
			// WikiCommandRenderer rather than the shared default: a [[wiki link]] in the body is only
			// followable from a surface that can navigate the wiki, and this is that surface.
			rendered = RecursiveMarkdownHelper.RenderMarkdown(markdown, new WikiCommandRenderer(RenderWidth, parser));
		}

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
	/// <param name="showDraft">
	/// <c>/DRAFT</c>: list an unpublished stream's revisions rather than withholding them. Edit summaries
	/// are author-written prose about unpublished content, so the log is gated exactly as the body is
	/// rather than acquiring a visibility rule of its own.
	/// </param>
	public static async ValueTask<MString> History(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		IWikiLocalizationService localization,
		INotifyService notifyService,
		MString target,
		string? locale = null,
		bool forceSource = false,
		bool showDraft = false)
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
		// Same gate as Handle, and for the same reason: CanEdit passes every player on every unprotected
		// page, so it decided nothing.
		var maySeeDrafts = await WikiCommandHelper.CanSeeDrafts(executor);
		var localized = forceSource ? null : await localization.LocalizeAsync(page, locale, maySeeDrafts);
		var stream = localized is null
			|| string.Equals(localized.Locale, localization.SourceLocaleOf(page), StringComparison.OrdinalIgnoreCase)
			? string.Empty
			: localized.Locale;

		var streamMarker = stream.Length == 0 ? string.Empty : $" ({stream})";
		var lines = new List<MString>
		{
			MModule.single($"WIKI: Revision history for {localized?.Title ?? page.Title} [{page.Namespace}]{streamMarker}:"),
		};

		if (IsPublic(page, localized) || (showDraft && maySeeDrafts))
		{
			var revisions = stream.Length == 0
				? await wikiService.GetRevisionsAsync(page.Id)
				: await wikiService.GetRevisionsForLocaleAsync(page.Id, stream, 0, 20);

			lines.AddRange(revisions.Select(r => MModule.single(
				$"  r{r.RevisionNumber,-4} {r.Timestamp:yyyy-MM-dd HH:mm}  by {r.EditorDbref,-8} {r.EditSummary ?? ""}".TrimEnd())));
		}
		else
		{
			lines.Add(DraftWithheld("revision history", maySeeDrafts));
		}

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}
}
