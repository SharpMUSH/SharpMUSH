using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using System.Security;
using System.Text;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// SEO endpoints served at the site root (no /api prefix):
///   GET /sitemap.xml — XML sitemap covering the site root, /wiki, and every published wiki page
///   GET /robots.txt  — crawler directives pointing at the sitemap
/// </summary>
public class SeoController(
	IWikiService wikiService,
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

		var sb = new StringBuilder();
		sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
		sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

		// Static entries: site root and the wiki landing page.
		AppendUrl(sb, $"{baseUrl}/", now);
		AppendUrl(sb, $"{baseUrl}/wiki", now);

		// Page through ALL wiki pages in fixed-size chunks until a short page signals the end.
		var skip = 0;
		var total = 0;
		while (true)
		{
			var chunk = await wikiService.GetAllPagesAsync(skip, PageSize);
			foreach (var page in chunk)
			{
				if (!page.Published)
					continue;

				AppendUrl(sb, baseUrl + PathFor(page), page.UpdatedAt.ToString("yyyy-MM-dd"));
				total++;
			}

			if (chunk.Count < PageSize)
				break;

			skip += PageSize;
		}

		sb.AppendLine("</urlset>");

		logger.LogDebug("Sitemap generated with {Count} published wiki pages", total);

		Response.Headers.CacheControl = "public, max-age=3600";
		return Content(sb.ToString(), "application/xml; charset=utf-8");
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

	// ── Helpers ──────────────────────────────────────────────────────────────

	/// <summary>Maps a wiki page to its public portal path based on its namespace.</summary>
	private static string PathFor(WikiPage page) =>
		page.Namespace.ToLowerInvariant() switch
		{
			"main" => $"/wiki/{page.Slug}",
			"character" => $"/character/{page.Slug}",
			var ns => $"/wiki/{ns}/{page.Slug}",
		};

	private static void AppendUrl(StringBuilder sb, string loc, string lastmod)
	{
		sb.AppendLine("  <url>");
		sb.AppendLine($"    <loc>{SecurityElement.Escape(loc)}</loc>");
		sb.AppendLine($"    <lastmod>{lastmod}</lastmod>");
		sb.AppendLine("  </url>");
	}
}
