using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Helpers;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// REST API for wiki page data and bot-facing pre-rendered HTML.
/// Routes:
///   GET    /api/wiki/ns/{ns}/{category}/{slug} — JSON page data (canonical, all clients)
///   GET    /api/wiki/character/{name}  — character namespace alias
///   GET    /api/wiki/recent           — recently updated pages
///   GET    /api/wiki/ns/{ns}           — list pages in a namespace
///   GET    /api/wiki/{slug}/revisions — revision history (newest first)
///   GET    /api/wiki/{slug}/revisions/{n} — single revision snapshot
///   POST   /api/wiki                  — create page (authenticated)
///   PUT    /api/wiki/{slug}            — update page (authenticated)
///   DELETE /api/wiki/{slug}             — delete page (Wizard+)
///   PUT    /api/wiki/{slug}/protection  — set protection flag (Wizard+)
///   POST   /api/wiki/{slug}/rollback    — restore an earlier revision (authenticated)
///   POST   /api/wiki/exists             — batch page-existence check (redlinks)
///   GET    /api/wiki/pages            — paginated listing of all pages (X-Total-Count header)
///   GET    /api/wiki/category/{cat}   — pages in a category
///   GET    /api/wiki/tag/{tag}        — pages carrying a tag
///   PUT    /api/wiki/{slug}/metadata  — set category/tags/published (authenticated)
///   POST   /api/wiki/batch/protect    — batch protection change (Wizard+)
///   POST   /api/wiki/batch/delete     — batch deletion (Wizard+)
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
		int RevisionNumber,
		string? Category,
		IReadOnlyList<string> Tags,
		bool Published);

	/// <summary>A single revision snapshot. MarkdownSource is the full page body at that revision.</summary>
	public record WikiRevisionDto(
		int RevisionNumber,
		string EditorDbref,
		DateTimeOffset Timestamp,
		string? EditSummary,
		string MarkdownSource);

	// ── Helpers ──────────────────────────────────────────────────────────────

	private static WikiPageDto ToDto(WikiPage p) => new(
		p.Id, p.Slug, p.Title, p.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.CreatedAt, p.UpdatedAt, p.IsProtected, p.RevisionNumber,
		p.Category, p.Tags, p.Published);

	private static WikiRevisionDto ToDto(WikiRevision r) => new(
		r.RevisionNumber, r.EditorDbref, r.Timestamp, r.EditSummary, r.MarkdownSource);

	private static WikiNamespace ParseNamespace(string? ns) =>
		Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	private static WikiNamespace? ParseOptionalNamespace(string? ns) =>
		string.IsNullOrWhiteSpace(ns) ? null
		: Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	/// <summary>
	/// Parses a wiki page reference into its (namespace, category, slug) identity. Accepts
	/// "ns/category/slug" (canonical), "ns/slug" (category defaults to general), or "slug"
	/// (main namespace, general category).
	/// </summary>
	private static (WikiNamespace Ns, string Category, string Slug) ParseRef(string reference)
	{
		var parts = reference.Split('/');
		return parts.Length switch
		{
			>= 3 => (ParseNamespace(parts[0]), parts[1], string.Join('/', parts[2..])),
			2 => (ParseNamespace(parts[0]), WikiHelpers.DefaultCategory, parts[1]),
			_ => (WikiNamespace.Main, WikiHelpers.DefaultCategory, reference)
		};
	}

	/// <summary>True when the caller carries an authenticated identity. Anonymous
	/// callers only see Published pages; drafts are reserved for logged-in users.</summary>
	private bool IsAuthenticatedCaller => User.Identity?.IsAuthenticated == true;

	/// <summary>
	/// The caller's character dbref from the JWT NameIdentifier claim. Never defaults to a
	/// privileged dbref: a missing claim means we cannot attribute the action, so callers
	/// must reject the request rather than silently acting as God (#1).
	/// </summary>
	private string? CallerDbref => User.FindFirstValue(ClaimTypes.NameIdentifier);

	/// <summary>Filters out unpublished (draft) pages for anonymous callers.</summary>
	private IEnumerable<WikiPage> FilterVisible(IEnumerable<WikiPage> pages) =>
		IsAuthenticatedCaller ? pages : pages.Where(p => p.Published);

	// ── Read endpoints ───────────────────────────────────────────────────────

	/// <summary>
	/// GET /api/wiki/ns/{namespace}/{category}/{slug}
	/// Returns JSON page data for a page identified by (namespace, category, slug),
	/// or 404 when the page doesn't exist. This is the canonical page route.
	/// </summary>
	[HttpGet("ns/{ns}/{category}/{slug}")]
	public async Task<IActionResult> GetPage(string ns, string category, string slug)
	{
		var result = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		return result.Match<IActionResult>(
			page => page.Published || IsAuthenticatedCaller ? Ok(ToDto(page)) : NotFound(),
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/wiki/character/{name}
	/// Resolves the /character/{name} alias to the Character namespace wiki page (default category).
	/// </summary>
	[HttpGet("character/{name}")]
	public async Task<IActionResult> GetCharacterPage(string name)
	{
		var result = await wikiService.GetBySlugAsync(name, WikiHelpers.DefaultCategory, WikiNamespace.Character);
		return result.Match<IActionResult>(
			page => page.Published || IsAuthenticatedCaller ? Ok(ToDto(page)) : NotFound(),
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
		return Ok(FilterVisible(pages).Select(ToDto));
	}

	/// <summary>
	/// GET /api/wiki/ns/{namespace}?skip=0&amp;take=50
	/// Lists pages within a namespace, ordered by title, with pagination.
	/// </summary>
	[HttpGet("ns/{ns}")]
	public async Task<IActionResult> ListNamespacePages(string ns, [FromQuery] int skip = 0, [FromQuery] int take = 50)
	{
		var pages = await wikiService.GetByNamespaceAsync(ParseNamespace(ns), skip, take);
		return Ok(FilterVisible(pages).Select(ToDto));
	}

	/// <summary>
	/// GET /api/wiki/pages?skip=0&amp;take=50&amp;ns=
	/// Paginated listing of all pages (optionally restricted to a namespace).
	/// The X-Total-Count response header carries the unpaginated page count.
	/// Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("pages")]
	public async Task<IActionResult> ListAllPages([FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? ns = null)
	{
		var nsFilter = ParseOptionalNamespace(ns);
		var pages = await wikiService.GetAllPagesAsync(skip, take, nsFilter);
		// CountPagesAsync includes drafts, so only expose the total to authenticated callers.
		// Emitting it for anonymous users would leak how many unpublished pages exist and
		// would not match the published-only collection they receive.
		if (IsAuthenticatedCaller)
			Response.Headers["X-Total-Count"] = (await wikiService.CountPagesAsync(nsFilter)).ToString();
		return Ok(FilterVisible(pages).Select(ToDto));
	}

	/// <summary>
	/// GET /api/wiki/category/{category}?skip=0&amp;take=50
	/// Lists pages in a category. Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("category/{category}")]
	public async Task<IActionResult> ListCategoryPages(string category, [FromQuery] int skip = 0, [FromQuery] int take = 50)
	{
		var pages = await wikiService.GetByCategoryAsync(category, skip, take);
		return Ok(FilterVisible(pages).Select(ToDto));
	}

	/// <summary>
	/// GET /api/wiki/tag/{tag}?skip=0&amp;take=50
	/// Lists pages carrying a tag. Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("tag/{tag}")]
	public async Task<IActionResult> ListTagPages(string tag, [FromQuery] int skip = 0, [FromQuery] int take = 50)
	{
		var pages = await wikiService.GetByTagAsync(tag, skip, take);
		return Ok(FilterVisible(pages).Select(ToDto));
	}

	/// <summary>
	/// GET /api/wiki/{slug}/revisions?skip=0&amp;take=20
	/// Returns the revision history for a page, newest first.
	/// </summary>
	[HttpGet("{slug}/revisions")]
	public async Task<IActionResult> GetRevisions(string slug, [FromQuery] int skip = 0, [FromQuery] int take = 20, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();
		// Mirror GetPage: drafts (and their history) are hidden from anonymous callers.
		if (!lookup.AsT0.Published && !IsAuthenticatedCaller) return NotFound();

		var revisions = await wikiService.GetRevisionsAsync(lookup.AsT0.Id, skip, take);
		return Ok(revisions.Select(ToDto));
	}

	/// <summary>
	/// GET /api/wiki/{slug}/revisions/{number}
	/// Returns a single revision snapshot, including its full markdown body.
	/// </summary>
	[HttpGet("{slug}/revisions/{number:int}")]
	public async Task<IActionResult> GetRevision(string slug, int number, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();
		// Mirror GetPage: drafts (and their history) are hidden from anonymous callers.
		if (!lookup.AsT0.Published && !IsAuthenticatedCaller) return NotFound();

		var result = await wikiService.GetRevisionAsync(lookup.AsT0.Id, number);
		return result.Match<IActionResult>(
			revision => Ok(ToDto(revision)),
			_ => NotFound());
	}

	// ── Write endpoints ───────────────────────────────────────────────────────

	/// <summary>Request body for creating a new wiki page. Category is part of identity and is fixed at create.</summary>
	public record CreatePageRequest(string Title, string Markdown, string? Namespace, string? Category);

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
		var authorDbref = CallerDbref;
		if (string.IsNullOrEmpty(authorDbref))
			return Unauthorized("Missing character identity.");
		var ns = ParseNamespace(request.Namespace);
		var result = await wikiService.CreateAsync(request.Title, request.Markdown, authorDbref, ns, request.Category);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page created: slug={Slug} ns={Ns} category={Category} by={Author}",
					LogSanitizer.Sanitize(page.Slug), ns, LogSanitizer.Sanitize(page.Category), LogSanitizer.Sanitize(authorDbref));
				return CreatedAtAction(nameof(GetPage),
					new { ns = page.Namespace, category = page.Category, slug = page.Slug }, ToDto(page));
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
	public async Task<IActionResult> UpdatePage(string slug, [FromBody] UpdatePageRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		// Protected pages may only be edited by Wizard-level users.
		if (lookup.AsT0.IsProtected && !User.IsInRole(nameof(PortalRole.Wizard)))
			return Forbid();

		var id = lookup.AsT0.Id;
		var result = await wikiService.UpdateAsync(id, request.Markdown, editorDbref, request.EditSummary);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page updated: slug={Slug} rev={Rev} by={Editor}", LogSanitizer.Sanitize(slug), page.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix($"/wiki/");
				return Ok(ToDto(page));
			},
			_ => NotFound());
	}

	/// <summary>Request body for rolling a page back to an earlier revision.</summary>
	public record RollbackRequest(int RevisionNumber);

	/// <summary>
	/// POST /api/wiki/{slug}/rollback
	/// Restores the page body from an earlier revision snapshot. The restore is a
	/// normal edit — it creates a NEW revision rather than rewriting history, so
	/// a rollback can itself be rolled back.
	/// </summary>
	[HttpPost("{slug}/rollback")]
	[Authorize]
	public async Task<IActionResult> RollbackPage(string slug, [FromBody] RollbackRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		var page = lookup.AsT0;
		if (page.IsProtected && !User.IsInRole(nameof(PortalRole.Wizard)))
			return Forbid();

		var revisionLookup = await wikiService.GetRevisionAsync(page.Id, request.RevisionNumber);
		if (revisionLookup.IsT1) return NotFound();

		var result = await wikiService.UpdateAsync(
			page.Id, revisionLookup.AsT0.MarkdownSource, editorDbref,
			$"rollback to r{request.RevisionNumber}");
		return result.Match<IActionResult>(
			updated =>
			{
				logger.LogInformation("Wiki page rolled back: slug={Slug} to r{Target} (now r{Rev}) by={Editor}",
					LogSanitizer.Sanitize(slug), request.RevisionNumber, updated.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix($"/wiki/");
				return Ok(ToDto(updated));
			},
			_ => NotFound());
	}

	/// <summary>Request body for the batch existence check. Refs use URL-path form:
	/// "ns/category/slug" (canonical), "ns/slug" (general category), or "slug" (main/general).</summary>
	public record ExistsRequest(string[] Refs);

	/// <summary>
	/// POST /api/wiki/exists
	/// Batch existence check used by the client to mark redlinks at view time.
	/// Returns a map of each requested ref to whether the page exists (and is
	/// visible to the caller — drafts count as missing for anonymous callers).
	/// </summary>
	[HttpPost("exists")]
	[AllowAnonymous]
	public async Task<IActionResult> CheckExists([FromBody] ExistsRequest request)
	{
		const int maxRefs = 200;
		var result = new Dictionary<string, bool>(StringComparer.Ordinal);

		foreach (var reference in request.Refs.Distinct(StringComparer.Ordinal).Take(maxRefs))
		{
			var (ns, category, slug) = ParseRef(reference);
			var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
			result[reference] = lookup.IsT0 && (lookup.AsT0.Published || IsAuthenticatedCaller);
		}

		return Ok(result);
	}

	/// <summary>
	/// DELETE /api/wiki/{slug}
	/// Deletes a wiki page and all its revisions, identified by slug.
	/// </summary>
	[HttpDelete("{slug}")]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
	public async Task<IActionResult> DeletePage(string slug, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		var id = lookup.AsT0.Id;
		var result = await wikiService.DeleteAsync(id, editorDbref);
		return result.Match<IActionResult>(
			_ =>
			{
				logger.LogInformation("Wiki page deleted: slug={Slug} by={Editor}", LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(editorDbref));
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
	public async Task<IActionResult> SetProtection(string slug, [FromBody] SetProtectionRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		var id = lookup.AsT0.Id;
		var result = await wikiService.SetProtectionAsync(id, request.IsProtected);
		return result.Match<IActionResult>(
			_ => Ok(),
			_ => NotFound());
	}

	/// <summary>Request body for setting page metadata.</summary>
	public record SetMetadataRequest(string? Category, string[] Tags, bool Published);

	/// <summary>
	/// PUT /api/wiki/{slug}/metadata
	/// Sets the category, tags and published flag on a page, identified by slug.
	/// Does not create a content revision.
	/// </summary>
	[HttpPut("{slug}/metadata")]
	[Authorize]
	public async Task<IActionResult> SetMetadata(string slug, [FromBody] SetMetadataRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		// Protected pages may only be retagged/(un)published by Wizard-level users,
		// mirroring the edit restriction in UpdatePage.
		if (lookup.AsT0.IsProtected && !User.IsInRole(nameof(PortalRole.Wizard)))
			return Forbid();

		var result = await wikiService.SetMetadataAsync(
			lookup.AsT0.Id, request.Category, request.Tags ?? [], request.Published);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page metadata updated: slug={Slug} category={Category} published={Published}",
					LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(page.Category), page.Published);
				prerenderCache.InvalidatePrefix("/wiki/");
				return Ok(ToDto(page));
			},
			_ => NotFound());
	}

	// ── Batch operations ──────────────────────────────────────────────────────

	/// <summary>Request body for batch protection changes. Refs use "ns/category/slug" form.</summary>
	public record BatchProtectRequest(string[] Refs, bool IsProtected);

	/// <summary>Request body for batch deletion. Refs use "ns/category/slug" form.</summary>
	public record BatchDeleteRequest(string[] Refs);

	/// <summary>Per-slug outcome of a batch operation.</summary>
	public record BatchResult(IReadOnlyList<string> Succeeded, IReadOnlyList<string> Failed);

	/// <summary>
	/// POST /api/wiki/batch/protect
	/// Sets or clears the protection flag on multiple pages at once.
	/// </summary>
	[HttpPost("batch/protect")]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
	public async Task<IActionResult> BatchProtect([FromBody] BatchProtectRequest request)
	{
		var succeeded = new List<string>();
		var failed = new List<string>();

		foreach (var reference in request.Refs ?? [])
		{
			var (ns, category, slug) = ParseRef(reference);
			var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
			if (lookup.IsT1)
			{
				failed.Add(reference);
				continue;
			}

			var result = await wikiService.SetProtectionAsync(lookup.AsT0.Id, request.IsProtected);
			(result.IsT0 ? succeeded : failed).Add(reference);
		}

		logger.LogInformation("Wiki batch protect: protected={Protected} ok={Ok} failed={Failed}",
			request.IsProtected, succeeded.Count, failed.Count);
		return Ok(new BatchResult(succeeded, failed));
	}

	/// <summary>
	/// POST /api/wiki/batch/delete
	/// Deletes multiple pages (and their revisions) at once.
	/// </summary>
	[HttpPost("batch/delete")]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
	public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequest request)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var succeeded = new List<string>();
		var failed = new List<string>();

		foreach (var reference in request.Refs ?? [])
		{
			var (ns, category, slug) = ParseRef(reference);
			var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
			if (lookup.IsT1)
			{
				failed.Add(reference);
				continue;
			}

			var result = await wikiService.DeleteAsync(lookup.AsT0.Id, editorDbref);
			(result.IsT0 ? succeeded : failed).Add(reference);
		}

		prerenderCache.InvalidatePrefix("/wiki/");
		logger.LogInformation("Wiki batch delete: by={Editor} ok={Ok} failed={Failed}",
			LogSanitizer.Sanitize(editorDbref), succeeded.Count, failed.Count);
		return Ok(new BatchResult(succeeded, failed));
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
			LogSanitizer.Sanitize(request.Path), LogSanitizer.Sanitize(request.Prefix));

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
	/// Builds a schema.org Article JSON-LD block for the pre-rendered page.
	/// Serializing the whole object via System.Text.Json guarantees safe escaping
	/// of titles/URLs containing quotes or angle brackets.
	/// </summary>
	private static string BuildArticleJsonLd(WikiPage page, string canonicalUrl)
	{
		var jsonLd = new Dictionary<string, object>
		{
			["@context"] = "https://schema.org",
			["@type"] = "Article",
			["headline"] = page.Title,
			["datePublished"] = page.CreatedAt.ToString("O"),
			["dateModified"] = page.UpdatedAt.ToString("O"),
			["url"] = canonicalUrl,
		};
		return JsonSerializer.Serialize(jsonLd);
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
