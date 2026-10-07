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
using SharpMUSH.Library.Logging;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Wiki administration: protection, metadata, the two batch operations and pre-render cache
/// eviction. Everything here is Wizard-level except metadata, which is an edit.
///
/// Routes:
///   PUT  /api/wiki/{slug}/protection  — require wiki.admin to edit and delete the page, or not (wiki.admin)
///   PUT  /api/wiki/{slug}/metadata    — set categories/published (authenticated)
///   POST /api/wiki/batch/protect      — batch protection change (Wizard+)
///   POST /api/wiki/batch/delete       — batch deletion (Wizard+)
///   POST /api/wiki/invalidate-cache   — evict pre-render cache entries after an edit
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiAdminController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	IPrerenderCacheService prerenderCache,
	IWikiNameResolver names,
	ILogger<WikiAdminController> logger) : WikiControllerBase(wikiService, localization, access, names, logger)
{
	/// <summary>
	/// PUT /api/wiki/{slug}/protection
	/// Protects a page (a page requirement of wiki.admin to edit and delete it) or clears that requirement.
	/// </summary>
	[HttpPut("{slug}/protection")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> SetProtection(string slug, [FromBody] SetProtectionRequest request, [FromQuery] string? ns = null)
	{
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page)
			return NotFound();

		return await ProtectAsync(page, request.IsProtected) switch
		{
			WikiRequirements => Ok(),
			Error<string> error => BadRequest(error.Value),
			_ => NotFound(),
		};
	}

	/// <summary>Sets or clears the page requirement of wiki.admin to edit and delete it.</summary>
	private async Task<FoundResult<WikiRequirements>> ProtectAsync(WikiPage page, bool protect)
		=> await Access.SetRequirementsAsync(await ReaderAsync(), CallerDbref ?? string.Empty, WikiRuleTarget.ForPage(page.Id),
			protect
				? WikiRequirementSet.Protection
				: new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Edit] = [], [WikiAction.Delete] = [] });

	/// <summary>
	/// PUT /api/wiki/{slug}/metadata
	/// Sets the categories and published flag on a page, identified by slug.
	/// Does not create a content revision.
	/// </summary>
	[HttpPut("{slug}/metadata")]
	[Authorize]
	public async Task<IActionResult> SetMetadata(string slug, [FromBody] SetMetadataRequest request, [FromQuery] string? ns = null)
	{
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage existing)
			return NotFound();

		// Metadata is an edit: the page's requirements apply, and filing it in a category is an edit under the
		// new categories too. Publishing is wiki.admin's.
		if (await RefusalAsync(existing, WikiAction.Edit) is { } refusal)
			return refusal;
		var reader = await ReaderAsync();
		if (!(await Access.DecideCategoriesAsync(reader, existing, request.Categories ?? [])).Allowed)
			return Forbid();
		if (request.Published != existing.Published && !reader.Has(PortalPermission.WikiAdmin))
			return Forbid();

		if (await Wiki.SetMetadataAsync(existing.Id, request.Categories ?? [], request.Published) is not WikiPage page)
			return NotFound();
		await NameCategoriesAsync(request.Categories ?? [], existing.Categories, CallerDbref);

		Logger.LogInformation("Wiki page metadata updated: slug={Slug} categories={Categories} published={Published}",
			LogSanitizer.Sanitize(slug), string.Join(',', page.Categories), page.Published);
		prerenderCache.InvalidatePrefix("/wiki/");
		return Ok(await ToDtoAsync(page));
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
			var (ns, slug) = ParseRef(reference);
			if (await Wiki.GetBySlugAsync(slug, ns) is not WikiPage page)
			{
				failed.Add(reference);
				continue;
			}

			var result = await ProtectAsync(page, request.IsProtected);
			(result is WikiRequirements ? succeeded : failed).Add(reference);
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
			var (ns, slug) = ParseRef(reference);
			if (await Wiki.GetBySlugAsync(slug, ns) is not WikiPage page || await RefusalAsync(page, WikiAction.Delete) is not null)
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
