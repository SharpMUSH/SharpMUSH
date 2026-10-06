using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Keeps wiki source from writing script into the rendered page. <c>DisableHtml</c> blocks raw tags, but two
/// Markdown routes stayed open: the generic-attributes block (<c># Title {onclick=…}</c>,
/// <c>::: x {style=…}</c>) wrote any attribute onto any element, and a link or image took any URL scheme
/// (<c>[x](javascript:…)</c>, <c>&lt;javascript:…&gt;</c>). After parsing, every element keeps only its id (written
/// escaped, and the heading anchors the table of contents links to) and class names of plain identifier characters, plus the image dimensions <see cref="WikiLinkInlineRenderer"/>
/// already validates; a link or image URL keeps its value only when it is relative or http, https or mailto.
/// </summary>
/// <remarks>Registered last in <see cref="WikiMarkdigPipeline.CreatePipeline"/>, so it sees the finished tree.</remarks>
public sealed partial class WikiSafetyExtension : IMarkdownExtension
{
	private static readonly HashSet<string> SafeSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

	/// <summary>Attribute properties an element may keep, beside its id and classes; the image renderer checks their values.</summary>
	private static readonly HashSet<string> SafeProperties = new(StringComparer.OrdinalIgnoreCase) { "width", "height" };

	[GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_-]*$")]
	private static partial Regex IdentifierPattern();

	[GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.-]*):")]
	private static partial Regex SchemePattern();

	/// <summary>ASCII whitespace and control characters, which browsers drop from a URL before reading its scheme.</summary>
	[GeneratedRegex(@"[\x00-\x20\x7F]")]
	private static partial Regex IgnoredUrlCharacters();

	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		pipeline.DocumentProcessed -= Sanitize;
		pipeline.DocumentProcessed += Sanitize;
	}

	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
	}

	/// <summary>Whether a link or image may point at <paramref name="url"/>: relative, or http, https or mailto.</summary>
	public static bool IsSafeUrl(string? url)
	{
		if (string.IsNullOrEmpty(url)) return true;
		var scheme = SchemePattern().Match(IgnoredUrlCharacters().Replace(url, string.Empty));
		return !scheme.Success || SafeSchemes.Contains(scheme.Groups[1].Value);
	}

	private static void Sanitize(MarkdownDocument document)
	{
		foreach (var node in document.Descendants())
		{
			switch (node)
			{
				case LinkInline link when !IsSafeUrl(link.Url):
					link.Url = string.Empty;
					break;
				case AutolinkInline autolink when !IsSafeUrl(autolink.Url):
					autolink.Url = string.Empty;
					break;
			}

			if (node.TryGetAttributes() is { } attributes)
			{
				Restrict(attributes);
			}
		}
	}

	private static void Restrict(HtmlAttributes attributes)
	{
		attributes.Classes?.RemoveAll(name => !IdentifierPattern().IsMatch(name));
		attributes.Properties?.RemoveAll(property => !SafeProperties.Contains(property.Key));
	}
}
