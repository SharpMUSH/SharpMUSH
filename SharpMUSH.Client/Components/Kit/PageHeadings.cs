using System.Net;
using System.Text.RegularExpressions;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Fits rendered Markdown under a page that already has its own <c>h1</c> (the plain header or the banner): a
/// leading <c>h1</c> that repeats the page's title is dropped, and any other <c>h1</c> becomes an <c>h2</c>, so
/// the page has one top heading and the text's outline sits under it.
/// </summary>
public static partial class PageHeadings
{
	public static string UnderPageTitle(string html, string? pageTitle)
	{
		if (pageTitle is not null && LeadHeading().Match(html) is { Success: true } lead
			&& string.Equals(PlainText(lead.Groups["text"].Value), pageTitle.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			html = html[lead.Length..];
		}

		return html.Replace("<h1", "<h2", StringComparison.Ordinal).Replace("</h1>", "</h2>", StringComparison.Ordinal);
	}

	private static string PlainText(string html) => WebUtility.HtmlDecode(Tag().Replace(html, string.Empty)).Trim();

	[GeneratedRegex(@"^\s*<h1\b[^>]*>(?<text>.*?)</h1>\s*", RegexOptions.Singleline)]
	private static partial Regex LeadHeading();

	[GeneratedRegex("<[^>]*>")]
	private static partial Regex Tag();
}
