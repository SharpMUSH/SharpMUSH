using System.Net;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A wiki page's image is the first image in its body (D1 README §6.2). The server reports it on
/// the page DTO so a banner can show it, and the client removes that one copy from the rendered
/// body so it does not appear twice. Both read the rendered HTML the store already keeps: no
/// second Markdown parse per listing, and the image found is the element the client removes
/// however Markdig escaped its URL (<c>&amp;amp;</c>, percent-encoding). An image quoted in a code
/// block is text (<c>&amp;lt;img</c>) and never matches.
/// </summary>
public static partial class WikiImages
{
	[GeneratedRegex(@"<img\b[^>]*\bsrc\s*=\s*""([^""]*)""[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ImgTag();

	[GeneratedRegex(@"<p>\s*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex OpeningParagraphBefore();

	[GeneratedRegex(@"^\s*</p>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ClosingParagraphAfter();

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
	/// Removes the first <c>&lt;img&gt;</c> whose decoded <c>src</c> is <paramref name="url"/> from
	/// rendered HTML, together with the <c>&lt;p&gt;</c> it sat in alone. Later images, including a
	/// repeat of the same one, stay.
	/// </summary>
	public static string StripFirstImage(string html, string url)
	{
		if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(url)) return html;
		foreach (Match match in ImgTag().Matches(html))
		{
			if (WebUtility.HtmlDecode(match.Groups[1].Value) != url) continue;

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

		return html;
	}
}
