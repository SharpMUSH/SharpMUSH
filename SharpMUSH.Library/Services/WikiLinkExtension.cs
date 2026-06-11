using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Library.Services;

// ──────────────────────────────────────────────────────────────────────────────
// WikiLinkInline — the AST node
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Markdig AST node that represents a wiki-link: <c>[[Target]]</c> or
/// <c>[[Display Text|Target]]</c>.
/// </summary>
public sealed class WikiLinkInline : LeafInline
{
	/// <summary>
	/// The target slug (possibly namespace-prefixed, e.g. <c>help/getting_started</c>).
	/// </summary>
	public required string Slug { get; init; }

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

// ──────────────────────────────────────────────────────────────────────────────
// WikiLinkParser — matches [[...]] syntax
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Markdig inline parser that recognises <c>[[...]]</c> wiki-link syntax.
/// Handles the following forms:
/// <list type="bullet">
///   <item><c>[[Page Name]]</c> — link to main-namespace page, title = page name</item>
///   <item><c>[[Display Text|Page Name]]</c> — custom display text</item>
///   <item><c>[[Help:Getting Started]]</c> — namespace-prefixed page</item>
/// </list>
/// </summary>
internal sealed class WikiLinkParser : InlineParser
{
	public WikiLinkParser()
	{
		// '[' is the opening delimiter
		OpeningCharacters = ['['];
	}

	public override bool Match(InlineProcessor processor, ref StringSlice slice)
	{
		// We need at least [[x]]
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

		// Resolve namespace + category + slug
		var (ns, category, slug) = ResolveTarget(target);

		// Derive title from the bare slug (spaces for underscores)
		var title = System.Globalization.CultureInfo.CurrentCulture.TextInfo
			.ToTitleCase(slug.Replace('_', ' '));

		var node = new WikiLinkInline
		{
			// Canonical path identity: namespace/category/slug.
			Slug = $"{ns}/{category}/{slug}",
			Title = title,
			DisplayText = displayText,
		};

		processor.GetSourcePosition(slice.Start, out _, out _);
		processor.Inline = node;
		slice = current;
		return true;
	}

	/// <summary>
	/// Resolves a wiki-link target into its (namespace, category, slug) identity. Forms:
	/// <list type="bullet">
	///   <item><c>Page Name</c> → (main, general, page_name)</item>
	///   <item><c>Help:Page Name</c> → (help, general, page_name)</item>
	///   <item><c>Help:Guides:Page Name</c> → (help, guides, page_name)</item>
	/// </list>
	/// </summary>
	private static (string Namespace, string Category, string Slug) ResolveTarget(string target)
	{
		var parts = target.Split(':', 3);
		return parts.Length switch
		{
			3 => (parts[0].Trim().ToLowerInvariant(),
				WikiHelpers.NormalizeCategory(parts[1]),
				Slugify(parts[2].Trim())),
			2 => (parts[0].Trim().ToLowerInvariant(),
				WikiHelpers.DefaultCategory,
				Slugify(parts[1].Trim())),
			_ => ("main", WikiHelpers.DefaultCategory, Slugify(target.Trim()))
		};
	}

	private static string Slugify(string text) =>
		WikiHelpers.Slugify(text);
}

// ──────────────────────────────────────────────────────────────────────────────
// WikiLinkHtmlRenderer — emits <a> tags
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Markdig HTML renderer that converts <see cref="WikiLinkInline"/> nodes into
/// HTML anchor tags pointing to <c>/wiki/{ns}/{slug}</c>.
/// </summary>
internal sealed class WikiLinkHtmlRenderer : HtmlObjectRenderer<WikiLinkInline>
{
	protected override void Write(HtmlRenderer renderer, WikiLinkInline obj)
	{
		// Build href from full slug (which already includes namespace prefix if any)
		// e.g. "main/page_name", "help/getting_started"
		var href = $"/wiki/{obj.Slug}";
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

// ──────────────────────────────────────────────────────────────────────────────
// WikiLinkExtension — wires parser + renderer into Markdig
// ──────────────────────────────────────────────────────────────────────────────

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
