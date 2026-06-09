using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Web;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// REST API for wiki page data and bot-facing pre-rendered HTML.
/// Routes:
///   GET    /api/wiki/{slug}            — JSON page data (all clients)
///   GET    /api/wiki/ns/{ns}/{slug}    — namespaced page
///   GET    /api/wiki/character/{name}  — character namespace alias
///   GET    /api/wiki/recent           — recently updated pages
///   POST   /api/wiki                  — create page (authenticated)
///   PUT    /api/wiki/{slug}            — update page (authenticated)
///   DELETE /api/wiki/{slug}             — delete page (Wizard+)
///   PUT    /api/wiki/{slug}/protection  — set protection flag (Wizard+)
///   POST   /api/wiki/invalidate-cache — evict pre-render cache entries after an edit
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiController(
	IWikiService wikiService,
	IPrerenderCacheService prerenderCache,
	ILogger<WikiController> logger) : ControllerBase
{
	// ── DTO record types ─────────────────────────────────────────────────────

	/// <summary>Page data returned by the API. Includes MarkdownSource so the editor can round-trip.</summary>
	public record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		string RenderedHtml,
		string PlainText,
		DateTimeOffset CreatedAt,
		DateTimeOffset UpdatedAt,
		bool IsProtected,
		int RevisionNumber);

	// ── Helpers ──────────────────────────────────────────────────────────────

	private static WikiPageDto ToDto(WikiPage p) => new(
		p.Id, p.Slug, p.Title, p.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
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

	// ── Write endpoints ───────────────────────────────────────────────────────

	/// <summary>Request body for creating a new wiki page.</summary>
	public record CreatePageRequest(string Title, string Markdown, string? Namespace);

	/// <summary>Request body for updating an existing wiki page.</summary>
	public record UpdatePageRequest(string Markdown, string? EditSummary);

	/// <summary>Request body for setting page protection.</summary>
	public record SetProtectionRequest(bool IsProtected);

	/// <summary>
	/// POST /api/wiki
	/// Creates a new wiki page. The slug is derived from the title.
	/// </summary>
	[HttpPost]
	[Authorize]
	public async Task<IActionResult> CreatePage([FromBody] CreatePageRequest request)
	{
		var authorDbref = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "#1";
		var ns = ParseNamespace(request.Namespace);
		var result = await wikiService.CreateAsync(request.Title, request.Markdown, authorDbref, ns);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page created: slug={Slug} ns={Ns} by={Author}", page.Slug, ns, authorDbref);
				return CreatedAtAction(nameof(GetPage), new { slug = page.Slug }, ToDto(page));
			},
			err => Conflict(new { error = err.Value }));
	}

	/// <summary>
	/// PUT /api/wiki/{slug}
	/// Updates an existing wiki page's markdown content, identified by its slug.
	/// Using slug (not the internal DB ID) avoids encoded-slash routing issues with
	/// ArangoDB-style IDs (e.g. "node_wiki_pages/1532") which contain a literal '/'.
	/// </summary>
	[HttpPut("{slug}")]
	[Authorize]
	public async Task<IActionResult> UpdatePage(string slug, [FromBody] UpdatePageRequest request)
	{
		var editorDbref = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "#1";
		var lookup = await wikiService.GetBySlugAsync(slug);
		if (lookup.IsT1) return NotFound();

		var id = lookup.AsT0.Id;
		var result = await wikiService.UpdateAsync(id, request.Markdown, editorDbref, request.EditSummary);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page updated: slug={Slug} rev={Rev} by={Editor}", slug, page.RevisionNumber, editorDbref);
				prerenderCache.InvalidatePrefix($"/wiki/");
				return Ok(ToDto(page));
			},
			_ => NotFound());
	}

	/// <summary>
	/// DELETE /api/wiki/{slug}
	/// Deletes a wiki page and all its revisions, identified by slug.
	/// </summary>
	[HttpDelete("{slug}")]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
	public async Task<IActionResult> DeletePage(string slug)
	{
		var editorDbref = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "#1";
		var lookup = await wikiService.GetBySlugAsync(slug);
		if (lookup.IsT1) return NotFound();

		var id = lookup.AsT0.Id;
		var result = await wikiService.DeleteAsync(id, editorDbref);
		return result.Match<IActionResult>(
			_ =>
			{
				logger.LogInformation("Wiki page deleted: slug={Slug} by={Editor}", slug, editorDbref);
				prerenderCache.InvalidatePrefix($"/wiki/");
				return NoContent();
			},
			_ => NotFound());
	}

	/// <summary>
	/// PUT /api/wiki/{slug}/protection
	/// Sets or clears the protection flag on a wiki page, identified by slug.
	/// </summary>
	[HttpPut("{slug}/protection")]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
	public async Task<IActionResult> SetProtection(string slug, [FromBody] SetProtectionRequest request)
	{
		var lookup = await wikiService.GetBySlugAsync(slug);
		if (lookup.IsT1) return NotFound();

		var id = lookup.AsT0.Id;
		var result = await wikiService.SetProtectionAsync(id, request.IsProtected);
		return result.Match<IActionResult>(
			_ => Ok(),
			_ => NotFound());
	}

	// ── Cache invalidation ────────────────────────────────────────────────────

	public record InvalidateCacheRequest(string? Path, string? Prefix);

	/// <summary>
	/// POST /api/wiki/invalidate-cache
	/// Evicts one or more pre-render cache entries.
	/// Called by the wiki edit handler after a page is saved.
	/// </summary>
	[HttpPost("invalidate-cache")]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
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
