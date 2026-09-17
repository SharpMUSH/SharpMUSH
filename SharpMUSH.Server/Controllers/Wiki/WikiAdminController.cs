using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Helpers;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Wiki administration: protection, metadata, the two batch operations and pre-render cache
/// eviction. Everything here is Wizard-level except metadata, which is an edit.
///
/// Routes:
///   PUT  /api/wiki/{slug}/protection  — set protection flag (Wizard+)
///   PUT  /api/wiki/{slug}/metadata    — set category/tags/published (authenticated)
///   POST /api/wiki/batch/protect      — batch protection change (Wizard+)
///   POST /api/wiki/batch/delete       — batch deletion (Wizard+)
///   POST /api/wiki/invalidate-cache   — evict pre-render cache entries after an edit
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiAdminController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IPrerenderCacheService prerenderCache,
	ILogger<WikiAdminController> logger) : WikiControllerBase(wikiService, localization, logger)
{
	/// <summary>
	/// PUT /api/wiki/{slug}/protection
	/// Sets or clears the protection flag on a wiki page, identified by slug.
	/// </summary>
	[HttpPut("{slug}/protection")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> SetProtection(string slug, [FromBody] SetProtectionRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		if (await Wiki.GetBySlugAsync(slug, category, ParseNamespace(ns)) is not WikiPage page)
			return NotFound();

		if (await Wiki.SetProtectionAsync(page.Id, request.IsProtected) is not None)
			return NotFound();

		return Ok();
	}

	/// <summary>
	/// PUT /api/wiki/{slug}/metadata
	/// Sets the category, tags and published flag on a page, identified by slug.
	/// Does not create a content revision.
	/// </summary>
	[HttpPut("{slug}/metadata")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> SetMetadata(string slug, [FromBody] SetMetadataRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		if (await Wiki.GetBySlugAsync(slug, category, ParseNamespace(ns)) is not WikiPage existing)
			return NotFound();

		// Protected pages may only be retagged/(un)published by Wizard-level users,
		// mirroring the edit restriction in UpdatePage.
		if (existing.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		if (await Wiki.SetMetadataAsync(existing.Id, request.Category, request.Tags ?? [], request.Published)
			is not WikiPage page)
			return NotFound();

		Logger.LogInformation("Wiki page metadata updated: slug={Slug} category={Category} published={Published}",
			LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(page.Category), page.Published);
		prerenderCache.InvalidatePrefix("/wiki/");
		return Ok(ToDto(page));
	}

	/// <summary>
	/// POST /api/wiki/batch/protect
	/// Sets or clears the protection flag on multiple pages at once.
	/// </summary>
	[HttpPost("batch/protect")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> BatchProtect([FromBody] BatchProtectRequest request)
	{
		var succeeded = new List<string>();
		var failed = new List<string>();

		foreach (var reference in request.Refs ?? [])
		{
			var (ns, category, slug) = ParseRef(reference);
			if (await Wiki.GetBySlugAsync(slug, category, ns) is not WikiPage page)
			{
				failed.Add(reference);
				continue;
			}

			var result = await Wiki.SetProtectionAsync(page.Id, request.IsProtected);
			(result is None ? succeeded : failed).Add(reference);
		}

		Logger.LogInformation("Wiki batch protect: protected={Protected} ok={Ok} failed={Failed}",
			request.IsProtected, succeeded.Count, failed.Count);
		return Ok(new WikiBatchResult(succeeded, failed));
	}

	/// <summary>
	/// POST /api/wiki/batch/delete
	/// Deletes multiple pages (and their revisions) at once.
	/// </summary>
	[HttpPost("batch/delete")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
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
			if (await Wiki.GetBySlugAsync(slug, category, ns) is not WikiPage page)
			{
				failed.Add(reference);
				continue;
			}

			var result = await Wiki.DeleteAsync(page.Id, editorDbref);
			(result is None ? succeeded : failed).Add(reference);
		}

		prerenderCache.InvalidatePrefix("/wiki/");
		Logger.LogInformation("Wiki batch delete: by={Editor} ok={Ok} failed={Failed}",
			LogSanitizer.Sanitize(editorDbref), succeeded.Count, failed.Count);
		return Ok(new WikiBatchResult(succeeded, failed));
	}

	/// <summary>
	/// POST /api/wiki/invalidate-cache
	/// Evicts one or more pre-render cache entries.
	/// Called by the wiki edit handler after a page is saved.
	/// </summary>
	[HttpPost("invalidate-cache")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public IActionResult InvalidateCache([FromBody] InvalidateCacheRequest request)
	{
		if (!string.IsNullOrWhiteSpace(request.Path))
			prerenderCache.Invalidate(request.Path);

		if (!string.IsNullOrWhiteSpace(request.Prefix))
			prerenderCache.InvalidatePrefix(request.Prefix);

		Logger.LogInformation("Pre-render cache invalidated: path={Path} prefix={Prefix}",
			LogSanitizer.Sanitize(request.Path), LogSanitizer.Sanitize(request.Prefix));

		return Ok();
	}
}
