using System.Net;
using System.Text.RegularExpressions;
using Markdig;

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
	///     (<c>::: category …</c>, <c>::: tag …</c>, <c>::: pagelist …</c>, <c>::: recent N</c>)</item>
	///   <item>DisableHtml — raw HTML in wiki source is blocked for security</item>
	/// </list>
	/// </summary>
	public static MarkdownPipeline CreatePipeline() =>
		new MarkdownPipelineBuilder()
			.UseAdvancedExtensions()
			.Use<WikiLinkExtension>()
			.Use<WikiImageExtension>()
			.Use<WikiDirectiveExtension>()
			.DisableHtml()
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
