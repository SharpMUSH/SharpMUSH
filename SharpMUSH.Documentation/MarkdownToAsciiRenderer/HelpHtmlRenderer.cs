using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Renderers.Html;
using SharpMUSH.Library.Services.Interfaces;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Renders a help entry to HTML for the web portal, from the same markdown and through the same
/// syntax extensions the terminal renderer uses (<see cref="RecursiveMarkdownHelper.ConfigureHelpSyntax"/>).
/// </summary>
/// <remarks>
/// The one thing that has to differ is what a topic reference points at. In game, <c>[@mail]</c>
/// becomes an OSC 8 hyperlink whose target is the command <c>help @mail</c>; in a browser it has to
/// become a portal URL. So this renderer parses with the shared extensions and then rewrites the
/// synthetic <c>help &lt;topic&gt;</c> URLs the parser produced, rather than re-deciding what a
/// topic link is.
/// <para>
/// Raw HTML is <b>not</b> disabled. Unlike wiki pages, help files are not user-authored content —
/// they ship with the server, and the index deliberately uses <c>&lt;br&gt;</c> for its layout.
/// Anyone who can edit <c>TextFiles/</c> already has the server's filesystem.
/// </para>
/// </remarks>
public static class HelpHtmlRenderer
{
	/// <summary>The URL scheme the shared <c>[topic]</c> parser emits: <c>help &lt;topic&gt;</c>.</summary>
	private const string TopicUrlPrefix = "help ";

	private static readonly MarkdownPipeline Pipeline =
		RecursiveMarkdownHelper.ConfigureHelpSyntax(new MarkdownPipelineBuilder()).Build();

	/// <summary>The class a header-less table gets: a list of names in columns, drawn without borders.</summary>
	public const string ListTableClass = "help-list";

	/// <summary>The class a paragraph laid out in columns of spaces gets, so the portal sets it monospaced.</summary>
	public const string AlignedClass = "help-aligned";

	/// <summary>A gap of three or more spaces inside a line: text lined up in columns.</summary>
	private static readonly Regex WideGap = new(@"\S {3,}\S", RegexOptions.Compiled);

	/// <summary>A gap of two or more spaces inside a line.</summary>
	private static readonly Regex Gap = new(@"\S {2,}\S", RegexOptions.Compiled);

	/// <summary>
	/// Renders help markdown to an HTML fragment.
	/// </summary>
	/// <param name="markdown">The entry body as the resolver returned it.</param>
	/// <param name="topicHref">
	/// Maps a topic name to the URL a reader should be sent to. Called for every <c>[topic]</c>
	/// reference in the body; return <see langword="null"/> to leave the reference as plain text.
	/// </param>
	public static string RenderToHtml(string markdown, Func<string, string?> topicHref, HelpArticle? article = null)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		ArgumentNullException.ThrowIfNull(topicHref);

		var document = Markdown.Parse(markdown, Pipeline);
		var sectionLabels = new Dictionary<string, string>(StringComparer.Ordinal);
		if (article is not null)
		{
			foreach (var heading in document.OfType<HeadingBlock>())
			{
				var section = article.Sections.FirstOrDefault(section =>
					section.Heading == HelpArticleParser.HeadingText(markdown, heading));
				if (section is not null)
				{
					heading.GetAttributes().Id = section.Id;
					sectionLabels[section.Id] = HeadingLabel(heading.Inline?.FirstChild);
				}
			}
		}

		foreach (var table in document.Descendants<Table>().ToList())
		{
			MarkHeaderlessList(table);
		}

		foreach (var paragraph in document.Descendants<ParagraphBlock>().ToList())
		{
			KeepTerminalSpacing(paragraph);
		}

		foreach (var link in document.Descendants<LinkInline>())
		{
			if (link.Url is null || !link.Url.StartsWith(TopicUrlPrefix, StringComparison.Ordinal))
			{
				continue;
			}

			var topic = link.Url[TopicUrlPrefix.Length..];
			link.Url = topicHref(topic) ?? string.Empty;
		}

		var writer = new StringWriter();
		var renderer = new HtmlRenderer(writer);
		Pipeline.Setup(renderer);
		renderer.Render(document);
		writer.Flush();

		var toc = article is null || article.Sections.Count == 0 ? string.Empty
			: "<nav class=\"help-toc\" aria-label=\"Article sections\"><ul>" + string.Concat(article.Sections.Select(section =>
				$"<li><a href=\"{WebUtility.HtmlEncode(topicHref(article.Lookup) ?? string.Empty)}#{WebUtility.HtmlEncode(section.Id)}\">{WebUtility.HtmlEncode(sectionLabels.GetValueOrDefault(section.Id, section.Heading))}</a></li>")) + "</ul></nav>";
		return toc + writer;
	}

	/// <summary>
	/// A table whose header cells are all empty is a list laid out in columns, such as the topic index.
	/// The terminal prints it without borders or a header line (<see cref="RecursiveMarkdownRenderer"/>),
	/// so the empty header row is dropped here and the table marked <see cref="ListTableClass"/>.
	/// </summary>
	private static void MarkHeaderlessList(Table table)
	{
		var headers = table.OfType<TableRow>().Where(row => row.IsHeader).ToList();
		if (headers.Count == 0 || headers.SelectMany(row => row.OfType<TableCell>())
			.Any(cell => cell.Descendants<ParagraphBlock>().Any(paragraph => paragraph.Inline?.FirstChild is not null)))
		{
			return;
		}
		foreach (var header in headers)
		{
			table.Remove(header);
		}
		table.GetAttributes().AddClass(ListTableClass);
	}

	/// <summary>
	/// Shapes a paragraph so a browser lays it out the way the terminal renderer does. The portal shows
	/// paragraphs with <c>white-space: pre-wrap</c>, because helpfiles line text up with runs of spaces
	/// that a browser would otherwise collapse to one. That only works if the paragraph's HTML carries no
	/// newline the terminal would not print, so each line break becomes what the terminal makes of it: a
	/// soft break is a space, a soft break right after a <c>&lt;br&gt;</c> is nothing, and a hard break
	/// is a <c>&lt;br&gt;</c> with no newline after it. A paragraph whose lines are columns of spaces is
	/// marked <see cref="AlignedClass"/>, since the columns only line up in a monospaced font.
	/// </summary>
	private static void KeepTerminalSpacing(ParagraphBlock paragraph)
	{
		if (paragraph.Inline is null)
		{
			return;
		}

		foreach (var lineBreak in paragraph.Inline.Descendants<LineBreakInline>().ToList())
		{
			if (lineBreak.IsHard)
			{
				lineBreak.ReplaceBy(new HtmlInline("<br />"));
			}
			else if (lineBreak.PreviousSibling is HtmlInline { Tag: var tag } && IsBreakTag(tag))
			{
				lineBreak.Remove();
			}
			else
			{
				lineBreak.ReplaceBy(new LiteralInline(" "));
			}
		}

		var lines = new StringBuilder();
		AppendLineText(paragraph.Inline.FirstChild, lines);
		var text = lines.ToString().Split('\n');
		if (text.Any(WideGap.IsMatch) || text.Length > 1 && text.Any(Gap.IsMatch))
		{
			paragraph.GetAttributes().AddClass(AlignedClass);
		}
	}

	private static bool IsBreakTag(string tag) =>
		tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase) && tag.Length > 3 && tag[3] is '>' or '/' or ' ';

	/// <summary>The paragraph's text as a reader sees it, one <c>\n</c> per <c>&lt;br&gt;</c>.</summary>
	private static void AppendLineText(Inline? inline, StringBuilder text)
	{
		for (var child = inline; child is not null; child = child.NextSibling)
		{
			switch (child)
			{
				case LiteralInline literal:
					text.Append(literal.Content.AsSpan());
					break;
				case CodeInline code:
					text.Append(code.Content);
					break;
				case HtmlEntityInline entity:
					text.Append(entity.Transcoded.AsSpan());
					break;
				case HtmlInline html when IsBreakTag(html.Tag):
					text.Append('\n');
					break;
				case ContainerInline container:
					AppendLineText(container.FirstChild, text);
					break;
			}
		}
	}

	private static string HeadingLabel(Inline? inline)
	{
		var text = new StringBuilder();
		for (var child = inline; child is not null; child = child.NextSibling)
		{
			text.Append(child switch
			{
				LiteralInline literal => literal.Content.ToString(),
				CodeInline code => code.Content,
				HtmlEntityInline entity => entity.Transcoded.ToString(),
				AutolinkInline link => link.Url,
				LineBreakInline => " ",
				ContainerInline container => HeadingLabel(container.FirstChild),
				_ => string.Empty
			});
		}
		return text.ToString();
	}

	/// <summary>
	/// Strips markup to readable text — used for prerender meta descriptions and search snippets.
	/// </summary>
	public static string ExtractPlainText(string markdown, int maxLength = 300)
	{
		ArgumentNullException.ThrowIfNull(markdown);

		var document = Markdown.Parse(markdown, Pipeline);
		var sb = new StringBuilder();

		foreach (var literal in document.Descendants<LiteralInline>())
		{
			if (sb.Length > 0 && sb[^1] != ' ')
			{
				sb.Append(' ');
			}
			sb.Append(literal.Content);
			if (sb.Length >= maxLength)
			{
				break;
			}
		}

		var text = sb.ToString().Trim();
		return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";
	}
}
