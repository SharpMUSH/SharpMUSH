using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A wiki page's image is the first image in its Markdown (D1 README §6.2). The server reports it
/// on the page DTO so a banner can show it, and the client removes that one copy from the rendered
/// body so it does not appear twice.
/// </summary>
public static partial class WikiImages
{
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

	/// <summary>
	/// The URL of the first Markdown image (<c>![alt](url)</c>, with or without a title), or null.
	/// Images inside fenced or inline code are not images and are skipped, which parsing (rather
	/// than a regex over the source) gets right for free.
	/// </summary>
	public static string? FirstImageUrl(string? markdown)
	{
		if (string.IsNullOrWhiteSpace(markdown)) return null;
		var document = Markdown.Parse(markdown, Pipeline);
		var image = document.Descendants<LinkInline>().FirstOrDefault(link => link.IsImage);
		return string.IsNullOrWhiteSpace(image?.Url) ? null : image.Url;
	}

	/// <summary>
	/// Removes the first <c>&lt;img&gt;</c> whose <c>src</c> is <paramref name="url"/> from rendered
	/// HTML. Later images, including a repeat of the same one, stay.
	/// </summary>
	public static string StripFirstImage(string html, string url)
	{
		if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(url)) return html;
		var pattern = @"<img\b[^>]*\bsrc\s*=\s*""" + Regex.Escape(url) + @"""[^>]*>";
		return Regex.Replace(html, pattern, string.Empty, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)) is var stripped && stripped.Length != html.Length
			? RemoveOnlyFirst(html, pattern)
			: html;
	}

	private static string RemoveOnlyFirst(string html, string pattern)
	{
		var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
		return match.Success ? html.Remove(match.Index, match.Length) : html;
	}
}
