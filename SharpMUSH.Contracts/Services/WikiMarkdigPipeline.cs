using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Factory and rendering utilities for the SharpMUSH wiki Markdig pipeline.
/// </summary>
/// <remarks>
/// Thread-safe: the pipeline and the pipeline instance are immutable after creation.
/// Create a single instance and reuse it for the lifetime of the application.
/// </remarks>
public sealed class WikiMarkdigPipeline
{
	private readonly MarkdownPipeline _pipeline;

	/// <summary>
	/// Initialises the pipeline with all wiki-required extensions enabled.
	/// </summary>
	public WikiMarkdigPipeline()
	{
		_pipeline = CreatePipeline();
	}

	/// <summary>
	/// Builds a <see cref="MarkdownPipeline"/> with the extensions required for
	/// wiki rendering:
	/// <list type="bullet">
	///   <item>Advanced extensions (tables, task lists, auto-links, pipe tables, emphasis extras)</item>
	///   <item><see cref="WikiLinkExtension"/> for <c>[[page]]</c> links</item>
	///   <item><see cref="WikiImageExtension"/> for lazy-loading, lightbox-ready images</item>
	///   <item><see cref="WikiDirectiveExtension"/> for dynamic-listing directives
	///     (<c>::: category …</c>, <c>::: pagelist …</c>, <c>::: recent N</c>)</item>
	///   <item><see cref="MarkdownFlexExtension"/> for <c>flex</c> / <c>item</c> layouts</item>
	///   <item>DisableHtml — raw HTML in wiki source is blocked for security</item>
	///   <item><see cref="WikiSafetyExtension"/> — attribute blocks and link URLs cannot carry script either</item>
	/// </list>
	/// </summary>
	public static MarkdownPipeline CreatePipeline() =>
		new MarkdownPipelineBuilder()
			.UseAdvancedExtensions()
			.Use<WikiLinkExtension>()
			.Use<WikiImageExtension>()
			.Use<WikiDirectiveExtension>()
			.UseFlexLayout()
			.DisableHtml()
			.Use<WikiSafetyExtension>()
			.Build();

	/// <summary>
	/// Renders a Markdown string to HTML using the wiki pipeline.
	/// Raw HTML in the source is stripped (DisableHtml is active).
	/// </summary>
	public string RenderToHtml(string markdown)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		return Markdown.ToHtml(markdown, _pipeline);
	}

	/// <summary>
	/// Renders a package's README. A README is written for the package's own folder, so a link or image whose
	/// URL is not absolute (<c>../profile-handler/</c>, <c>docs/setup.md</c>, <c>shot.png</c>) points at a file
	/// that is not served here: the link becomes its text and the image its description. Absolute
	/// <c>http</c>/<c>https</c>/<c>mailto</c> links, in-page <c>#</c> anchors and <c>[[wiki links]]</c> stay.
	/// </summary>
	public string RenderReadmeToHtml(string markdown)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		var document = Markdown.Parse(markdown, _pipeline);
		foreach (var link in document.Descendants<LinkInline>().Where(l => !Reachable(l.Url)).ToList())
		{
			Unlink(link);
		}

		return document.ToHtml(_pipeline);
	}

	private static bool Reachable(string? url) =>
		url is not null
		&& (url.StartsWith('#')
			|| (Uri.TryCreate(url, UriKind.Absolute, out var absolute)
				&& absolute.Scheme is "http" or "https" or "mailto"));

	/// <summary>Puts <paramref name="link"/>'s text where it stood, without the link.</summary>
	private static void Unlink(LinkInline link)
	{
		while (link.FirstChild is { } child)
		{
			child.Remove();
			link.InsertBefore(child);
		}

		link.Remove();
	}

	/// <summary>
	/// Extracts human-readable plain text from a Markdown string.
	/// All Markdown markup is stripped; wiki-link anchor text is preserved.
	/// </summary>
	/// <remarks>
	/// Strategy: render to HTML first (so custom AST nodes such as
	/// <see cref="WikiLinkInline"/> are handled by their registered HTML renderer),
	/// then strip HTML tags and decode HTML entities to produce clean text.
	/// </remarks>
	public string ExtractPlainText(string markdown)
	{
		ArgumentNullException.ThrowIfNull(markdown);

		var html = Markdown.ToHtml(markdown, _pipeline);
		return StripHtml(html);
	}

	/// <summary>
	/// Strips HTML tags and decodes entities to produce plain readable text.
	/// Block-level elements are replaced with newlines to preserve paragraph structure.
	/// </summary>
	private static string StripHtml(string html)
	{
		// Replace block-closing tags with newlines so paragraphs stay separated
		html = Regex.Replace(html, @"</(p|li|h[1-6]|blockquote|pre|tr)>", "\n", RegexOptions.IgnoreCase);
		html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
		html = Regex.Replace(html, @"<[^>]+>", string.Empty);
		html = WebUtility.HtmlDecode(html);
		html = Regex.Replace(html, @"\n{3,}", "\n\n");
		return html.Trim();
	}
}
