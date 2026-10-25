using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.RateLimiting;
using SharpMUSH.Server.Services;
using System.Security;
using System.Text;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// SEO endpoints served at the site root (no /api prefix):
///   GET /sitemap.xml — XML sitemap covering the site root, /wiki, and every published wiki page
///   GET /robots.txt  — crawler directives pointing at the sitemap
/// </summary>
// [ApiController] is required by the Asp.Versioning AV0014 analyzer, which every controller the
// versioning system sees must satisfy. It is behaviourally inert here: both actions already use
// attribute routing, bind no parameters (so the automatic 400/ProblemDetails paths are unreachable),
// and only ever return 200 ContentResult. Version resolution is unaffected because
// AssumeDefaultVersionWhenUnspecified is on, so these root-served documents keep answering
// unversioned crawler requests.
[ApiController]
[EnableRateLimiting(WikiReadRateLimit.PolicyName)]
public class SeoController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	SitemapCache sitemap,
	ILogger<SeoController> logger) : ControllerBase
{
	private const int PageSize = 500;

	/// <summary>
	/// GET /sitemap.xml
	/// Enumerates all published wiki pages and emits a sitemaps.org-compliant XML document.
	/// </summary>
	[HttpGet("/sitemap.xml")]
	[AllowAnonymous]
	public async Task<IActionResult> Sitemap()
	{
		var baseUrl = $"{Request.Scheme}://{Request.Host}";
		var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
		var entries = await sitemap.GetAsync(SitemapEntriesAsync, HttpContext.RequestAborted);

		var sb = new StringBuilder();
		sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
		sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">");

		AppendUrl(sb, $"{baseUrl}/", now);
		AppendUrl(sb, $"{baseUrl}/wiki", now);

		foreach (var entry in entries)
			AppendUrl(sb, baseUrl + entry.Path, entry.UpdatedAt.ToString("yyyy-MM-dd"), entry.Locales);

		sb.AppendLine("</urlset>");

		Response.Headers.CacheControl = "public, max-age=3600";
		return Content(sb.ToString(), "application/xml; charset=utf-8");
	}

	/// <summary>
	/// Every published page a logged-out reader may see: no drafts, and nothing whose read requirements the
	/// everyone role misses.
	/// </summary>
	private async Task<IReadOnlyList<SitemapEntry>> SitemapEntriesAsync(CancellationToken ct)
	{
		var visibility = await access.VisibilityAsync(await access.AnonymousAsync(ct));
		var entries = new List<SitemapEntry>();
		WikiPageScanPosition position = new WikiPageScanStart();
		while (true)
		{
			var batch = await wikiService.ScanPagesAsync(visibility, position, PageSize);
			foreach (var page in batch.Pages.Where(page => page.Published && visibility.Admits(page)))
			{
				// Bot-facing, so includeDrafts: false — the sitemap must never advertise a locale whose only
				// translation is an unpublished draft.
				entries.Add(new SitemapEntry(PathFor(page), page.UpdatedAt,
					await localization.GetVisibleLocalesAsync(page, includeDrafts: false)));
			}

			if (batch.Next is not WikiPageScanContinuation next) break;
			position = next;
			ct.ThrowIfCancellationRequested();
		}

		logger.LogDebug("Sitemap built with {Count} published wiki pages", entries.Count);
		return entries;
	}

	/// <summary>
	/// GET /robots.txt
	/// Allows all crawlers, blocks admin/API paths, and advertises the sitemap.
	/// </summary>
	[HttpGet("/robots.txt")]
	[AllowAnonymous]
	public IActionResult Robots()
	{
		var baseUrl = $"{Request.Scheme}://{Request.Host}";

		var sb = new StringBuilder();
		sb.AppendLine("User-agent: *");
		sb.AppendLine("Allow: /");
		sb.AppendLine("Disallow: /admin/");
		sb.AppendLine("Disallow: /api/");
		sb.AppendLine();
		sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");

		Response.Headers.CacheControl = "public, max-age=3600";
		return Content(sb.ToString(), "text/plain; charset=utf-8");
	}

	/// <summary>Maps a wiki page to its public portal path based on its namespace.</summary>
	private static string PathFor(WikiPage page) =>
		WikiRoutes.PathFor(page.Namespace, page.Slug);

	private static void AppendUrl(
		StringBuilder sb, string loc, string lastmod, IReadOnlyList<string>? alternateLocales = null)
	{
		sb.AppendLine("  <url>");
		sb.AppendLine($"    <loc>{SecurityElement.Escape(loc)}</loc>");
		sb.AppendLine($"    <lastmod>{lastmod}</lastmod>");

		// Only worth emitting when there is more than one locale to point at: a lone self-referential
		// alternate says nothing, and the site-root entries have no locales at all.
		if (alternateLocales is { Count: > 1 })
		{
			foreach (var locale in alternateLocales)
			{
				var href = $"{loc}{(loc.Contains('?') ? '&' : '?')}lang={Uri.EscapeDataString(locale)}";
				sb.AppendLine(
					$"    <xhtml:link rel=\"alternate\" hreflang=\"{SecurityElement.Escape(locale)}\" href=\"{SecurityElement.Escape(href)}\" />");
			}
		}

		sb.AppendLine("  </url>");
	}
}
