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
/// A wiki page: fetch it, create it, edit it, delete it. Listings live in
/// <see cref="WikiBrowseController"/>, history in <see cref="WikiRevisionsController"/>, per-locale
/// bodies in <see cref="WikiTranslationsController"/>, and protection, metadata and the batch
/// operations in <see cref="WikiAdminController"/>. All five share
/// <see cref="WikiControllerBase"/>'s visibility rules and share the <c>api/wiki</c> prefix.
///
/// Routes:
///   GET    /api/wiki/ns/{ns}/{slug}   — JSON page data (canonical, all clients)
///   GET    /api/wiki/character/{name} — character namespace alias
///   POST   /api/wiki                  — create page (authenticated)
///   PUT    /api/wiki/{slug}           — update page (authenticated)
///   DELETE /api/wiki/{slug}           — delete page (wiki.delete and what the page requires)
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	IPrerenderCacheService prerenderCache,
	IWikiNameResolver names,
	ILogger<WikiController> logger) : WikiControllerBase(wikiService, localization, access, names, logger)
{
	/// <summary>
	/// GET /api/wiki/ns/{namespace}/{slug}?lang=fr
	/// Returns JSON page data for a page identified by (namespace, slug), resolved into the
	/// reader's locale, or 404 when the page doesn't exist. This is the canonical page route.
	/// <c>lang</c> is advisory: a malformed or unknown tag is treated as absent and falls to the
	/// configured default rather than producing a 400.
	/// </summary>
	[HttpGet("ns/{ns}/{slug}")]
	public async Task<IActionResult> GetPage(string ns, string slug, [FromQuery] string? lang = null)
	{
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page || !await CanSeeAsync(page))
			return NotFound();

		return Ok(await LocalizedDtoAsync(page, lang));
	}

	/// <summary>
	/// GET /api/wiki/character/{name}?lang=fr
	/// Resolves the /character/{name} alias to the Character namespace wiki page, resolved into the
	/// reader's locale.
	/// </summary>
	[HttpGet("character/{name}")]
	public async Task<IActionResult> GetCharacterPage(string name, [FromQuery] string? lang = null)
	{
		if (await Wiki.GetBySlugAsync(name, WikiNamespace.Character) is not WikiPage page
			|| !await CanSeeAsync(page))
			return NotFound();

		return Ok(await LocalizedDtoAsync(page, lang));
	}

	/// <summary>
	/// POST /api/wiki
	/// Creates a new wiki page. The slug is derived from the title.
	/// </summary>
	[HttpPost]
	[Authorize(Policy = PortalPermission.WikiCreate)]
	public async Task<IActionResult> CreatePage([FromBody] CreatePageRequest request)
	{
		var authorDbref = CallerDbref;
		if (string.IsNullOrEmpty(authorDbref))
			return Unauthorized("Missing character identity.");
		var ns = ParseNamespace(request.Namespace);
		if (!(await Access.DecideCreateAsync(await ReaderAsync(), WikiHelpers.NamespaceName(ns), request.Categories ?? [])).Allowed)
			return Forbid();
		// SourceLocale is materialised at creation. The configured default affects new pages and fallback
		// resolution only; it never reinterprets a page that already exists.
		var result = await Wiki.CreateAsync(
			request.Title, request.Markdown, authorDbref, ns, Localization.DefaultLocale, request.Categories);
		return result switch
		{
			WikiPage page => await PageCreatedAsync(page),
			Error<string> err => Conflict(new { error = err.Value })
		};

		async Task<IActionResult> PageCreatedAsync(WikiPage page)
		{
			await NameCategoriesAsync(request.Categories ?? [], [], authorDbref);
			Logger.LogInformation("Wiki page created: slug={Slug} ns={Ns} by={Author}",
				LogSanitizer.Sanitize(page.Slug), ns, LogSanitizer.Sanitize(authorDbref));
			return CreatedAtAction(nameof(GetPage),
				new { ns = page.Namespace, slug = page.Slug }, await ToDtoAsync(page));
		}
	}

	/// <summary>
	/// PUT /api/wiki/{slug}
	/// Updates an existing wiki page's markdown content, identified by its slug.
	/// Using slug (not the internal DB ID) avoids encoded-slash routing issues with
	/// </summary>
	[HttpPut("{slug}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> UpdatePage(string slug, [FromBody] UpdatePageRequest request, [FromQuery] string? ns = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage existing)
			return NotFound();

		if (await RefusalAsync(existing, WikiAction.Edit) is { } refused)
			return refused;

		if (await Wiki.UpdateAsync(existing.Id, request.Markdown, editorDbref, request.EditSummary)
			is not WikiPage page)
			return NotFound();

		Logger.LogInformation("Wiki page updated: slug={Slug} rev={Rev} by={Editor}", LogSanitizer.Sanitize(slug), page.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
		prerenderCache.InvalidatePrefix("/wiki/");
		return Ok(await ToDtoAsync(page));
	}

	/// <summary>
	/// DELETE /api/wiki/{slug}
	/// Deletes a wiki page and all its revisions, identified by slug.
	/// </summary>
	[HttpDelete("{slug}")]
	[Authorize(Policy = PortalPermission.WikiDelete)]
	public async Task<IActionResult> DeletePage(string slug, [FromQuery] string? ns = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page)
			return NotFound();
		if (await RefusalAsync(page, WikiAction.Delete) is { } refused)
			return refused;

		if (await Wiki.DeleteAsync(page.Id, editorDbref) is not None)
			return NotFound();

		Logger.LogInformation("Wiki page deleted: slug={Slug} by={Editor}", LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(editorDbref));
		prerenderCache.InvalidatePrefix("/wiki/");
		return NoContent();
	}
}
