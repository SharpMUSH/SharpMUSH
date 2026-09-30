using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Renderers.Html;
using SharpMUSH.Library.Services.Interfaces;
using System.Net;
using System.Text;

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
		if (article is not null)
		{
			foreach (var heading in document.OfType<HeadingBlock>())
			{
				var section = article.Sections.FirstOrDefault(section =>
					section.Heading == HelpArticleParser.HeadingText(markdown, heading));
				if (section is not null)
				{
					heading.GetAttributes().Id = section.Id;
				}
			}
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
			: "<nav class=\"help-toc\"><ul>" + string.Concat(article.Sections.Select(section =>
				$"<li><a href=\"#{WebUtility.HtmlEncode(section.Id)}\">{WebUtility.HtmlEncode(section.Heading)}</a></li>")) + "</ul></nav>";
		return toc + writer.ToString();
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
			sb.Append(literal.Content.ToString());
			if (sb.Length >= maxLength)
			{
				break;
			}
		}

		var text = sb.ToString().Trim();
		return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";
	}
}
