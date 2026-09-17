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
/// Read-only listings over the wiki: recent changes, a namespace, a category, a tag, the paginated
/// index, and the batch existence check the reader uses to mark redlinks.
///
/// Routes:
///   GET  /api/wiki/recent          — recently updated pages
///   GET  /api/wiki/ns/{ns}         — pages in a namespace
///   GET  /api/wiki/pages           — paginated listing of all pages (X-Total-Count header)
///   GET  /api/wiki/category/{cat}  — pages in a category
///   GET  /api/wiki/tag/{tag}       — pages carrying a tag
///   POST /api/wiki/exists          — batch page-existence check (redlinks)
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiBrowseController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	ILogger<WikiBrowseController> logger) : WikiControllerBase(wikiService, localization, logger)
{
	/// <summary>
	/// GET /api/wiki/recent?count=20&amp;lang=fr
	/// Returns recently updated pages with titles resolved into the reader's locale, one row per page.
	/// </summary>
	[HttpGet("recent")]
	public async Task<IActionResult> GetRecentChanges([FromQuery] int count = 20, [FromQuery] string? lang = null)
	{
		var pages = await Wiki.GetRecentChangesAsync(count);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/ns/{namespace}?skip=0&amp;take=50&amp;lang=fr
	/// Lists pages within a namespace, ordered by title, with pagination and localized titles.
	/// </summary>
	[HttpGet("ns/{ns}")]
	public async Task<IActionResult> ListNamespacePages(
		string ns, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await Wiki.GetByNamespaceAsync(ParseNamespace(ns), skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/pages?skip=0&amp;take=50&amp;ns=&amp;lang=fr
	/// Paginated listing of all pages (optionally restricted to a namespace), with localized titles.
	/// The X-Total-Count response header carries the unpaginated count of the pages this caller may see.
	/// Anonymous callers only see published pages.
	/// </summary>
	/// <remarks>
	/// The count takes the same visibility flag the rows are filtered by, so the total matches the
	/// collection and discloses no drafts; it can be sent to everyone, and has to be — a paginated listing
	/// without a total cannot be paged through.
	/// <para>
	/// One caller is counted slightly short: <see cref="CanSee"/> also passes a caller's own drafts, which
	/// no count can express. Such a caller sees rows the total does not include — their own, so nothing is
	/// disclosed.
	/// </para>
	/// </remarks>
	[HttpGet("pages")]
	public async Task<IActionResult> ListAllPages(
		[FromQuery] int skip = 0, [FromQuery] int take = 50,
		[FromQuery] string? ns = null, [FromQuery] string? lang = null)
	{
		var nsFilter = ParseOptionalNamespace(ns);
		var pages = await Wiki.GetAllPagesAsync(skip, take, nsFilter);
		Response.Headers["X-Total-Count"] =
			(await Wiki.CountPagesAsync(nsFilter, CanSeeUnpublished)).ToString();
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/category/{category}?skip=0&amp;take=50&amp;lang=fr
	/// Lists pages in a category with localized titles. Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("category/{category}")]
	public async Task<IActionResult> ListCategoryPages(
		string category, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await Wiki.GetByCategoryAsync(category, skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/tag/{tag}?skip=0&amp;take=50&amp;lang=fr
	/// Lists pages carrying a tag with localized titles. Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("tag/{tag}")]
	public async Task<IActionResult> ListTagPages(
		string tag, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await Wiki.GetByTagAsync(tag, skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}

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
			result[reference] = await Wiki.GetBySlugAsync(slug, category, ns) is WikiPage page && CanSee(page);
		}

		return Ok(result);
	}
}
