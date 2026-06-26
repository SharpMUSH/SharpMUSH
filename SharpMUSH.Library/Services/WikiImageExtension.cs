using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;

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
///
/// Image sizing: the Generic Attributes extension (part of UseAdvancedExtensions)
/// parses <c>![alt](src){width=200 height=100}</c> onto the AST node; this renderer
/// emits a WHITELISTED subset of those attributes — width, height, and extra CSS
/// classes — with strict value validation. Everything else (style, onerror, id, …)
/// is dropped so the attribute block cannot reopen the XSS door that DisableHtml
/// closed.
/// </summary>
internal sealed partial class WikiLinkInlineRenderer : HtmlObjectRenderer<LinkInline>
{
	/// <summary>CSS class applied to every wiki-rendered image.</summary>
	public const string ImageCssClass = "wiki-img";

	/// <summary>Dimension values: plain integers ("200") or percentages ("50%").</summary>
	[GeneratedRegex(@"^\d{1,5}%?$")]
	private static partial Regex DimensionPattern();

	/// <summary>Author-supplied CSS class names: conservative identifier charset.</summary>
	[GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_-]*$")]
	private static partial Regex CssClassPattern();

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
		var attributes = link.TryGetAttributes();

		renderer.Write("<img");
		renderer.Write($" src=\"");
		renderer.WriteEscapeUrl(src);
		renderer.Write("\"");
		renderer.Write($" alt=\"");
		renderer.WriteEscape(alt);
		renderer.Write("\"");
		renderer.Write($" class=\"{BuildCssClasses(attributes)}\"");
		renderer.Write(" loading=\"lazy\"");
		WriteDimension(renderer, attributes, "width");
		WriteDimension(renderer, attributes, "height");
		renderer.Write(" />");
	}

	/// <summary>
	/// Emits <c>width</c>/<c>height</c> from the generic-attributes block when present
	/// and the value passes the dimension whitelist. Invalid values are dropped silently.
	/// </summary>
	private static void WriteDimension(HtmlRenderer renderer, HtmlAttributes? attributes, string name)
	{
		var value = attributes?.Properties?
			.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase))
			.Value;

		if (value is not null && DimensionPattern().IsMatch(value))
			renderer.Write($" {name}=\"{value}\"");
	}

	/// <summary>
	/// Combines the mandatory <see cref="ImageCssClass"/> with any valid author-supplied
	/// classes from the generic-attributes block (<c>{.logo}</c>).
	/// </summary>
	private static string BuildCssClasses(HtmlAttributes? attributes)
	{
		if (attributes?.Classes is not { Count: > 0 } classes)
			return ImageCssClass;

		var extra = classes.Where(c => CssClassPattern().IsMatch(c)).ToList();
		return extra.Count == 0
			? ImageCssClass
			: $"{ImageCssClass} {string.Join(' ', extra)}";
	}

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
