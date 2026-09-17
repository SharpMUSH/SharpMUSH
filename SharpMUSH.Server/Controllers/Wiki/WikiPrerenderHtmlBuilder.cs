using SharpMUSH.Library.Models.Wiki;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Builds the minimal static HTML <c>BotPrerenderMiddleware</c> serves to crawlers: OpenGraph tags,
/// <c>hreflang</c> alternates, JSON-LD and the rendered body. Nothing here touches a request, a
/// principal or a service — it is string building, which is why it is not on a controller.
/// </summary>
public static class WikiPrerenderHtmlBuilder
{
	/// <summary>
	/// Generates a minimal static HTML page for bot consumption, resolved into one locale.
	/// Includes OpenGraph meta tags, <c>hreflang</c> alternates and the rendered page content.
	/// </summary>
	/// <param name="page">
	/// The resolved page. Its <c>Title</c> and body are the served locale's, not the source's — reading
	/// <c>page.Page</c> for content here is what would put an English title over French prose.
	/// </param>
	/// <param name="canonicalUrl">
	/// The unsuffixed canonical URL. <c>?lang=</c> never changes it: every locale is a view of one
	/// canonical page, and a per-locale canonical would split its ranking.
	/// </param>
	/// <param name="alternateLocales">Every locale a reader can read this page in, for <c>hreflang</c>.</param>
	/// <param name="defaultLocale">The configured default, emitted as <c>hreflang="x-default"</c>.</param>
	public static string GeneratePrerenderHtml(
		LocalizedWikiPage page,
		string canonicalUrl,
		IReadOnlyList<string> alternateLocales,
		string defaultLocale,
		string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{page.Title} - {siteName} Wiki");
		var ogTitle = HttpUtility.HtmlEncode($"{page.Title} - {siteName} Wiki");
		var ogDesc = HttpUtility.HtmlEncode(
			page.PlainText.Length > 200 ? page.PlainText[..200] + "…" : page.PlainText);
		var ogUrl = HttpUtility.HtmlEncode(canonicalUrl);
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);
		var lang = HttpUtility.HtmlEncode(page.Locale);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine($"<html lang=\"{lang}\">");
		sb.AppendLine("<head>");
		sb.AppendLine($"  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		AppendAlternates(sb, canonicalUrl, alternateLocales, defaultLocale);
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{ogTitle}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{ogDesc}\" />");
		sb.AppendLine($"  <meta property=\"og:type\" content=\"article\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{ogUrl}\" />");
		sb.AppendLine($"  <meta property=\"og:locale\" content=\"{lang}\" />");
		sb.AppendLine($"  <script type=\"application/ld+json\">{BuildArticleJsonLd(page, canonicalUrl)}</script>");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{HttpUtility.HtmlEncode(page.Title)}</h1>");
		sb.AppendLine($"  {page.RenderedHtml}");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}

	/// <summary>
	/// Emits one <c>&lt;link rel="alternate" hreflang="…"&gt;</c> per available locale plus
	/// <c>x-default</c> at the configured default. Nothing is emitted for a single-locale page: a lone
	/// self-referential alternate is noise, and advertising a locale nobody can read would be a lie.
	/// </summary>
	private static void AppendAlternates(
		StringBuilder sb, string canonicalUrl, IReadOnlyList<string> alternateLocales, string defaultLocale)
	{
		if (alternateLocales.Count < 2) return;

		var separator = canonicalUrl.Contains('?') ? '&' : '?';

		foreach (var locale in alternateLocales)
		{
			// The default locale's alternate still points at ?lang=, and x-default below points at the bare
			// canonical. Collapsing the two would leave the default locale with no self-referential
			// alternate, which Google reads as an incomplete cluster.
			var href = HttpUtility.HtmlEncode(
				$"{canonicalUrl}{separator}lang={Uri.EscapeDataString(locale)}");
			sb.AppendLine(
				$"  <link rel=\"alternate\" hreflang=\"{HttpUtility.HtmlEncode(locale)}\" href=\"{href}\" />");
		}

		sb.AppendLine(
			$"  <link rel=\"alternate\" hreflang=\"x-default\" href=\"{HttpUtility.HtmlEncode(canonicalUrl)}\" />");
	}

	/// <summary>
	/// Builds a schema.org Article JSON-LD block for the pre-rendered page.
	/// Serializing the whole object via System.Text.Json guarantees safe escaping
	/// of titles/URLs containing quotes or angle brackets.
	/// </summary>
	private static string BuildArticleJsonLd(LocalizedWikiPage page, string canonicalUrl)
	{
		var jsonLd = new Dictionary<string, object>
		{
			["@context"] = "https://schema.org",
			["@type"] = "Article",
			["headline"] = page.Title,
			["datePublished"] = page.Page.CreatedAt.ToString("O"),
			["dateModified"] = page.Page.UpdatedAt.ToString("O"),
			["url"] = canonicalUrl,
			["inLanguage"] = page.Locale,
		};
		return JsonSerializer.Serialize(jsonLd);
	}

	/// <summary>
	/// Generates a minimal static HTML page for a character profile bot response.
	/// </summary>
	public static string GenerateCharacterPrerenderHtml(
		LocalizedWikiPage page,
		string canonicalUrl,
		IReadOnlyList<string> alternateLocales,
		string defaultLocale,
		string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{page.Title} - {siteName}");
		var ogTitle = HttpUtility.HtmlEncode(page.Title);
		var ogDesc = HttpUtility.HtmlEncode(
			page.PlainText.Length > 200 ? page.PlainText[..200] + "…" : page.PlainText);
		var ogUrl = HttpUtility.HtmlEncode(canonicalUrl);
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);
		var lang = HttpUtility.HtmlEncode(page.Locale);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine($"<html lang=\"{lang}\">");
		sb.AppendLine("<head>");
		sb.AppendLine($"  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		AppendAlternates(sb, canonicalUrl, alternateLocales, defaultLocale);
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{ogTitle}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{ogDesc}\" />");
		sb.AppendLine($"  <meta property=\"og:type\" content=\"profile\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{ogUrl}\" />");
		sb.AppendLine($"  <meta property=\"og:locale\" content=\"{lang}\" />");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{HttpUtility.HtmlEncode(page.Title)}</h1>");
		sb.AppendLine($"  {page.RenderedHtml}");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}
}
