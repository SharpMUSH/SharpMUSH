using System.Net;
using System.Text.RegularExpressions;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Fits rendered Markdown under a page that already has its own <c>h1</c> (the plain header or the banner): a
/// leading <c>h1</c> that repeats the page's title is dropped, and when another <c>h1</c> remains every heading
/// moves down one level (<c>h6</c> stays), so the page has one top heading and the text's outline keeps its levels
/// under it. A dropped heading leaves its <c>id</c> behind, so a link to it still has a target.
/// </summary>
public static partial class PageHeadings
{
	public static string UnderPageTitle(string html, string? pageTitle)
	{
		if (pageTitle is not null && LeadHeading().Match(html) is { Success: true } lead
			&& string.Equals(PlainText(lead.Groups["text"].Value), pageTitle.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			html = Anchor(lead.Groups["attrs"].Value) + html[lead.Length..];
		}

		return Heading1().IsMatch(html)
			? HeadingTag().Replace(html, tag => $"<{tag.Groups["close"].Value}h{Math.Min(tag.Groups["level"].Value[0] - '0' + 1, 6)}")
			: html;
	}

	private static string Anchor(string attributes) =>
		Id().Match(attributes) is { Success: true } id ? $"<span id=\"{id.Groups["id"].Value}\"></span>" : string.Empty;

	private static string PlainText(string html) => WebUtility.HtmlDecode(Tag().Replace(html, string.Empty)).Trim();

	[GeneratedRegex(@"^\s*<h1\b(?<attrs>[^>]*)>(?<text>.*?)</h1>\s*", RegexOptions.Singleline)]
	private static partial Regex LeadHeading();

	[GeneratedRegex(@"<h1\b")]
	private static partial Regex Heading1();

	[GeneratedRegex(@"<(?<close>/?)h(?<level>[1-6])\b")]
	private static partial Regex HeadingTag();

	[GeneratedRegex(@"\bid\s*=\s*""(?<id>[^""]*)""")]
	private static partial Regex Id();

	[GeneratedRegex("<[^>]*>")]
	private static partial Regex Tag();
}
