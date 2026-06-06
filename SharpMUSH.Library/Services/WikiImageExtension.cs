using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Markdig extension that replaces the default image renderer so that every
/// Markdown image (<c>![alt](src)</c>) emitted as HTML receives:
/// <list type="bullet">
///   <item><c>loading="lazy"</c> — browser-native lazy loading</item>
///   <item><c>class="wiki-img"</c> — hooked by CSS for max-width + lightbox</item>
/// </list>
/// Raw HTML <c>&lt;img&gt;</c> tags are still blocked by <c>DisableHtml()</c>
/// on the pipeline; only Markdig-generated images go through this renderer.
/// </summary>
public sealed class WikiImageExtension : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		// No parser changes required — standard Markdig image syntax is already
		// parsed as LinkInline nodes with IsImage = true.
	}

	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
		if (renderer is not HtmlRenderer htmlRenderer)
			return;

		// Find and replace the default LinkInlineRenderer with our subclass.
		var existing = htmlRenderer.ObjectRenderers.FindExact<LinkInlineRenderer>();
		if (existing is not null)
			htmlRenderer.ObjectRenderers.Remove(existing);

		htmlRenderer.ObjectRenderers.Insert(0, new WikiLinkInlineRenderer());
	}
}

/// <summary>
/// Replaces the default Markdig <see cref="LinkInlineRenderer"/> so that image
/// nodes get <c>loading="lazy"</c> and <c>class="wiki-img"</c> attributes added.
/// All other link rendering (anchors, etc.) is delegated to the base class.
/// </summary>
internal sealed class WikiLinkInlineRenderer : HtmlObjectRenderer<LinkInline>
{
	/// <summary>CSS class applied to every wiki-rendered image.</summary>
	public const string ImageCssClass = "wiki-img";

	protected override void Write(HtmlRenderer renderer, LinkInline link)
	{
		if (!link.IsImage)
		{
			// Normal anchor — use the default Markdig behaviour.
			WriteAnchor(renderer, link);
			return;
		}

		// Image — emit with lazy loading and wiki-img class.
		var src = link.Url ?? string.Empty;
		var alt = ExtractPlainText(link);

		renderer.Write("<img");
		renderer.Write($" src=\"");
		renderer.WriteEscapeUrl(src);
		renderer.Write("\"");
		renderer.Write($" alt=\"");
		renderer.WriteEscape(alt);
		renderer.Write("\"");
		renderer.Write($" class=\"{ImageCssClass}\"");
		renderer.Write(" loading=\"lazy\"");
		renderer.Write(" />");
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Renders a standard hyperlink anchor, matching the default Markdig output.
	/// </summary>
	private static void WriteAnchor(HtmlRenderer renderer, LinkInline link)
	{
		if (renderer.EnableHtmlForInline)
		{
			renderer.Write("<a href=\"");
			renderer.WriteEscapeUrl(link.Url ?? string.Empty);
			renderer.Write("\"");

			if (link.Title is { Length: > 0 } title)
			{
				renderer.Write(" title=\"");
				renderer.WriteEscape(title);
				renderer.Write("\"");
			}

			renderer.Write(">");
		}

		renderer.WriteChildren(link);

		if (renderer.EnableHtmlForInline)
			renderer.Write("</a>");
	}

	/// <summary>
	/// Extracts plain-text alt text from the inline content of an image node.
	/// </summary>
	private static string ExtractPlainText(LinkInline link)
	{
		var sb = new System.Text.StringBuilder();
		foreach (var inline in link)
		{
			if (inline is LiteralInline literal)
				sb.Append(literal.Content.ToString());
		}
		return sb.ToString();
	}
}
