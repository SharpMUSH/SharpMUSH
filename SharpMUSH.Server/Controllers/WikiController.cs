using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;
using System.Net;
using System.Text;
using System.Web;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// REST API for wiki page data and bot-facing pre-rendered HTML.
/// Routes:
///   GET /api/wiki/{slug}            — JSON page data (all clients)
///   GET /api/wiki/{slug}/exists     — existence check (case-insensitive)
///   GET /api/wiki/{ns}/{slug}       — page in a specific namespace
///   POST /api/wiki/invalidate-cache — evict pre-render cache entries after an edit
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiController(
	IWikiService wikiService,
	IPrerenderCacheService prerenderCache,
	ILogger<WikiController> logger) : ControllerBase
{
	// ── DTO record types ─────────────────────────────────────────────────────

	/// <summary>Lightweight page summary returned by the API.</summary>
	public record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string RenderedHtml,
		string PlainText,
		DateTimeOffset CreatedAt,
		DateTimeOffset UpdatedAt,
		bool IsProtected,
		int RevisionNumber);

	// ── Helpers ──────────────────────────────────────────────────────────────

	private static WikiPageDto ToDto(WikiPage p) => new(
		p.Id, p.Slug, p.Title, p.Namespace, p.RenderedHtml, p.PlainText,
		p.CreatedAt, p.UpdatedAt, p.IsProtected, p.RevisionNumber);

	private static WikiNamespace ParseNamespace(string? ns) =>
		Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	// ── Read endpoints ───────────────────────────────────────────────────────

	/// <summary>
	/// GET /api/wiki/{slug}
	/// Returns JSON page data, or 404 when the page doesn't exist.
	/// </summary>
	[HttpGet("{slug}")]
	public async Task<IActionResult> GetPage(string slug)
	{
		var result = await wikiService.GetBySlugAsync(slug);
		return result.Match<IActionResult>(
			page => Ok(ToDto(page)),
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/wiki/ns/{namespace}/{slug}
	/// Returns JSON page data for a namespaced page.
	/// </summary>
	[HttpGet("ns/{ns}/{slug}")]
	public async Task<IActionResult> GetNamespacedPage(string ns, string slug)
	{
		var result = await wikiService.GetBySlugAsync(slug, ParseNamespace(ns));
		return result.Match<IActionResult>(
			page => Ok(ToDto(page)),
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/wiki/character/{name}
	/// Resolves the /character/{name} alias to the Character namespace wiki page.
	/// </summary>
	[HttpGet("character/{name}")]
	public async Task<IActionResult> GetCharacterPage(string name)
	{
		var result = await wikiService.GetBySlugAsync(name, WikiNamespace.Character);
		return result.Match<IActionResult>(
			page => Ok(ToDto(page)),
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/wiki/recent?count=20
	/// Returns recently updated pages.
	/// </summary>
	[HttpGet("recent")]
	public async Task<IActionResult> GetRecentChanges([FromQuery] int count = 20)
	{
		var pages = await wikiService.GetRecentChangesAsync(count);
		return Ok(pages.Select(ToDto));
	}

	// ── Cache invalidation ────────────────────────────────────────────────────

	public record InvalidateCacheRequest(string? Path, string? Prefix);

	/// <summary>
	/// POST /api/wiki/invalidate-cache
	/// Evicts one or more pre-render cache entries.
	/// Called by the wiki edit handler after a page is saved.
	/// </summary>
	[HttpPost("invalidate-cache")]
	public IActionResult InvalidateCache([FromBody] InvalidateCacheRequest request)
	{
		if (!string.IsNullOrWhiteSpace(request.Path))
			prerenderCache.Invalidate(request.Path);

		if (!string.IsNullOrWhiteSpace(request.Prefix))
			prerenderCache.InvalidatePrefix(request.Prefix);

		logger.LogInformation("Pre-render cache invalidated: path={Path} prefix={Prefix}",
			request.Path, request.Prefix);

		return Ok();
	}

	// ── Pre-render helper (called by BotPrerenderMiddleware) ─────────────────

	/// <summary>
	/// Generates a minimal static HTML page for bot consumption.
	/// Includes OpenGraph meta tags and the rendered page content.
	/// </summary>
	public static string GeneratePrerenderHtml(WikiPage page, string canonicalUrl, string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{page.Title} - {siteName} Wiki");
		var ogTitle = HttpUtility.HtmlEncode($"{page.Title} - {siteName} Wiki");
		var ogDesc = HttpUtility.HtmlEncode(
			page.PlainText.Length > 200 ? page.PlainText[..200] + "…" : page.PlainText);
		var ogUrl = HttpUtility.HtmlEncode(canonicalUrl);
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine("<html lang=\"en\">");
		sb.AppendLine("<head>");
		sb.AppendLine($"  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{ogTitle}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{ogDesc}\" />");
		sb.AppendLine($"  <meta property=\"og:type\" content=\"article\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{ogUrl}\" />");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{HttpUtility.HtmlEncode(page.Title)}</h1>");
		sb.AppendLine($"  {page.RenderedHtml}");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}

	/// <summary>
	/// Generates a minimal static HTML page for a character profile bot response.
	/// </summary>
	public static string GenerateCharacterPrerenderHtml(WikiPage page, string canonicalUrl, string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{page.Title} - {siteName}");
		var ogTitle = HttpUtility.HtmlEncode(page.Title);
		var ogDesc = HttpUtility.HtmlEncode(
			page.PlainText.Length > 200 ? page.PlainText[..200] + "…" : page.PlainText);
		var ogUrl = HttpUtility.HtmlEncode(canonicalUrl);
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine("<html lang=\"en\">");
		sb.AppendLine("<head>");
		sb.AppendLine($"  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{ogTitle}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{ogDesc}\" />");
		sb.AppendLine($"  <meta property=\"og:type\" content=\"profile\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{ogUrl}\" />");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{HttpUtility.HtmlEncode(page.Title)}</h1>");
		sb.AppendLine($"  {page.RenderedHtml}");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}
}
