using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Markdig AST node that represents a wiki-link: <c>[[Target]]</c> or
/// <c>[[Display Text|Target]]</c>.
/// </summary>
public sealed class WikiLinkInline : LeafInline
{
	/// <summary>
	/// The target's identity as <c>namespace/slug</c>, e.g. <c>help/getting_started</c>.
	/// </summary>
	public required string Slug { get; init; }

	/// <summary>
	/// The portal path this link points at. Character biographies resolve to their
	/// <c>/character/{slug}</c> alias rather than the wiki route they are stored under;
	/// see <see cref="WikiRoutes.PathFor"/>.
	/// </summary>
	public required string Href { get; init; }

	/// <summary>
	/// The human-readable title of the target page (derived from the slug if not specified).
	/// </summary>
	public required string Title { get; init; }

	/// <summary>
	/// Optional custom display text, supplied via <c>[[Display Text|Target]]</c> syntax.
	/// When <c>null</c>, the rendered anchor text falls back to <see cref="Title"/>.
	/// </summary>
	public string? DisplayText { get; init; }

	/// <summary>
	/// Whether to add the <c>wiki-redlink</c> CSS class (page known to not exist).
	/// Always <c>false</c> for now; redlink detection is deferred until DB integration.
	/// </summary>
	public bool IsRedLink { get; init; }
}

/// <summary>
/// Markdig inline parser that recognises <c>[[...]]</c> wiki-link syntax.
/// Handles the following forms:
/// <list type="bullet">
///   <item><c>[[Page Name]]</c> — link to main-namespace page, title = page name</item>
///   <item><c>[[Display Text|Page Name]]</c> — custom display text</item>
///   <item><c>[[Help:Getting Started]]</c> — namespace-prefixed page</item>
///   <item><c>[[Category:Lore]]</c> (or MediaWiki's <c>[[:Category:Lore]]</c>) — a link to the category's
///     page. A page's own categories are a list stored on the page, never read from its text.</item>
/// </list>
/// </summary>
internal sealed class WikiLinkParser : InlineParser
{
	public WikiLinkParser()
	{
		OpeningCharacters = ['['];
	}

	public override bool Match(InlineProcessor processor, ref StringSlice slice)
	{
		var current = slice;
		if (current.CurrentChar != '[') return false;
		current.NextChar();
		if (current.CurrentChar != '[') return false;
		current.NextChar();

		var sb = new System.Text.StringBuilder();
		int depth = 0;

		while (true)
		{
			var c = current.CurrentChar;
			if (c == '\0') return false; // EOF without closing ]]
			if (c == ']')
			{
				current.NextChar();
				if (current.CurrentChar == ']')
				{
					current.NextChar();
					break; // found closing ]]
				}
				sb.Append(']');
				continue;
			}
			if (c == '[') depth++;
			if (depth > 0) return false; // nested [[, bail
			sb.Append(c);
			current.NextChar();
		}

		var raw = sb.ToString().Trim();
		if (raw.Length == 0) return false;

		// Parse display text: [[Display|Target]] vs [[Target]]
		string? displayText = null;
		string target = raw;

		var pipeIdx = raw.IndexOf('|');
		if (pipeIdx >= 0)
		{
			displayText = raw[..pipeIdx].Trim();
			target = raw[(pipeIdx + 1)..].Trim();
		}

		// MediaWiki's leading colon ([[:Category:Lore]]) is accepted and means the same link.
		if (target.StartsWith(':')) target = target[1..].TrimStart();

		var (ns, slug) = WikiHelpers.ResolveTitle(target);
		if (slug.Length == 0) return false;

		// Derive title from the bare slug (spaces for underscores)
		var title = System.Globalization.CultureInfo.CurrentCulture.TextInfo
			.ToTitleCase(slug.Replace('_', ' '));

		var node = new WikiLinkInline
		{
			Slug = $"{WikiHelpers.NamespaceName(ns)}/{slug}",
			Href = WikiRoutes.PathFor(ns, slug),
			Title = title,
			DisplayText = displayText,
		};

		processor.GetSourcePosition(slice.Start, out _, out _);
		processor.Inline = node;
		slice = current;
		return true;
	}
}

/// <summary>
/// Markdig HTML renderer that converts <see cref="WikiLinkInline"/> nodes into HTML anchor
/// tags pointing at the node's canonical portal path (see <see cref="WikiLinkInline.Href"/>).
/// </summary>
internal sealed class WikiLinkHtmlRenderer : HtmlObjectRenderer<WikiLinkInline>
{
	protected override void Write(HtmlRenderer renderer, WikiLinkInline obj)
	{
		var href = obj.Href;
		var cssClass = obj.IsRedLink ? " class=\"wiki-redlink\"" : string.Empty;
		var text = obj.DisplayText ?? obj.Title;
		// C-5: Use WriteEscapeUrl for the href so slug characters like '"' and '>'
		// cannot break out of the attribute and create an XSS vector.
		renderer.Write("<a href=\"");
		renderer.WriteEscapeUrl(href);
		renderer.Write("\"");
		renderer.Write(cssClass);
		renderer.Write(">");
		renderer.WriteEscape(text);
		renderer.Write("</a>");
	}
}

/// <summary>
/// Markdig extension that adds <c>[[wiki-link]]</c> support.
/// Register via <c>pipeline.Use&lt;WikiLinkExtension&gt;()</c>.
/// </summary>
public sealed class WikiLinkExtension : Markdig.IMarkdownExtension
{
	public void Setup(Markdig.MarkdownPipelineBuilder pipeline)
	{
		if (!pipeline.InlineParsers.Contains<WikiLinkParser>())
			pipeline.InlineParsers.Insert(0, new WikiLinkParser());
	}

	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
		if (renderer is HtmlRenderer htmlRenderer
				&& !htmlRenderer.ObjectRenderers.Contains<WikiLinkHtmlRenderer>())
		{
			htmlRenderer.ObjectRenderers.Insert(0, new WikiLinkHtmlRenderer());
		}
	}
}
