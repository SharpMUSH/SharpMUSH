using System.Net;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A wiki page's image is the first image in its body (D1 README §6.2). The server reports it on
/// the page DTO so listings can show it as a thumbnail. Only a <em>lead</em> image — one that opens
/// the page, before any text — becomes the page's banner, and the client removes that one copy from
/// the rendered body so it does not appear twice; an image further down is part of the body. Both read the rendered HTML the store already keeps: no
/// second Markdown parse per listing, and the image found is the element the client removes
/// however Markdig escaped its URL (<c>&amp;amp;</c>, percent-encoding). An image quoted in a code
/// block is text (<c>&amp;lt;img</c>) and never matches.
/// </summary>
public static partial class WikiImages
{
	/// <summary>A lead image's entity-decoded URL and authored alternative text.</summary>
	public sealed record ImageReference(string Url, string? Alt);

	[GeneratedRegex(@"<img\b[^>]*\bsrc\s*=\s*""([^""]*)""[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ImgTag();

	[GeneratedRegex(@"\s+alt\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex AltAttribute();

	[GeneratedRegex(@"<p>\s*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex OpeningParagraphBefore();

	[GeneratedRegex(@"^\s*</p>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ClosingParagraphAfter();

	[GeneratedRegex(@"^\s*(<div class=""center"">\s*)?(<p>\s*)?$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex NothingBefore();

	/// <summary>The URL of the first <c>&lt;img&gt;</c> in rendered HTML, entity-decoded, or null.</summary>
	public static string? FirstImageUrl(string? html)
	{
		if (string.IsNullOrWhiteSpace(html)) return null;
		var match = ImgTag().Match(html);
		if (!match.Success) return null;
		var url = WebUtility.HtmlDecode(match.Groups[1].Value);
		return string.IsNullOrWhiteSpace(url) ? null : url;
	}

	/// <summary>
	/// The page's lead image — its first <c>&lt;img&gt;</c> when nothing but whitespace and the opening of
	/// its paragraph comes before it, inside a <c>::: center</c> block or not — with its URL and authored
	/// alternative text entity-decoded, or null when the page opens with text.
	/// </summary>
	public static ImageReference? LeadImage(string? html)
	{
		if (string.IsNullOrWhiteSpace(html)) return null;
		var match = ImgTag().Match(html);
		if (!match.Success || !NothingBefore().IsMatch(html[..match.Index])) return null;

		var url = WebUtility.HtmlDecode(match.Groups[1].Value);
		if (string.IsNullOrWhiteSpace(url)) return null;

		var altMatch = AltAttribute().Match(match.Value);
		var alt = altMatch.Success ? WebUtility.HtmlDecode(altMatch.Groups[1].Value) : null;
		return new ImageReference(url, alt);
	}

	/// <summary>The entity-decoded URL of the page's lead image, or null when the page opens with text.</summary>
	public static string? LeadImageUrl(string? html) => LeadImage(html)?.Url;

	/// <summary>
	/// Removes the first <c>&lt;img&gt;</c> whose decoded <c>src</c> is <paramref name="url"/> from
	/// rendered HTML, together with the <c>&lt;p&gt;</c> it sat in alone. Later images, including a
	/// repeat of the same one, stay.
	/// </summary>
	public static string StripFirstImage(string html, string url)
	{
		if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(url)) return html;
		var match = ImgTag().Matches(html).FirstOrDefault(m => WebUtility.HtmlDecode(m.Groups[1].Value) == url);
		if (match is null) return html;

		var start = match.Index;
		var end = match.Index + match.Length;
		var open = OpeningParagraphBefore().Match(html[..start]);
		var close = ClosingParagraphAfter().Match(html[end..]);
		if (open.Success && close.Success)
		{
			start -= open.Length;
			end += close.Length;
		}

		return html.Remove(start, end - start);
	}
}
