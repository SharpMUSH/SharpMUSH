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

	public static async ValueTask<MString> Handle(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString target)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, slug) = WikiCommandHelper.ResolveTarget(target.ToPlainText());

		var lookup = await wikiService.GetBySlugAsync(slug, ns);
		if (lookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {target.ToPlainText().Trim()}", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;
		var line = MModule.repeat(MModule.single("-"), RenderWidth);
		var markers = $"{(page.Published ? "" : " (draft)")}{(page.IsProtected ? " (protected)" : "")}";
		var tags = page.Tags.Count > 0 ? string.Join(", ", page.Tags) : "-";

		var rendered = RecursiveMarkdownHelper.RenderMarkdown(page.MarkdownSource, RenderWidth, parser);

		var output = MModule.multipleWithDelimiter(MModule.single("\n"),
		[
			line,
			MModule.single($"Wiki: {page.Title} [{page.Namespace}]{markers}"),
			MModule.single($"Category: {page.Category ?? "-"}   Tags: {tags}   Rev {page.RevisionNumber} — {page.UpdatedAt:yyyy-MM-dd HH:mm}"),
			line,
			rendered,
			line,
		]);

		await notifyService.Notify(executor, output, executor);
		return output;
	}

	public static async ValueTask<MString> History(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString target)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, slug) = WikiCommandHelper.ResolveTarget(target.ToPlainText());

		var lookup = await wikiService.GetBySlugAsync(slug, ns);
		if (lookup.IsT1)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {target.ToPlainText().Trim()}", executor);
			return MModule.single(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var page = lookup.AsT0;
		var revisions = await wikiService.GetRevisionsAsync(page.Id);

		var lines = new List<MString>
		{
			MModule.single($"WIKI: Revision history for {page.Title} [{page.Namespace}]:"),
		};
		lines.AddRange(revisions.Select(r => MModule.single(
			$"  r{r.RevisionNumber,-4} {r.Timestamp:yyyy-MM-dd HH:mm}  by {r.EditorDbref,-8} {r.EditSummary ?? ""}".TrimEnd())));

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return output;
	}
}
