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
/// Read-only listings over the wiki: recent changes, a namespace, a category, the paginated
/// index, the counts by state, and the batch existence check the reader uses to mark redlinks.
/// Listings return <see cref="WikiPageSummaryDto"/> rows (no bodies), at most
/// <see cref="WikiControllerBase.MaxListTake"/> per request.
///
/// Routes:
///   GET  /api/wiki/recent          — recently updated pages
///   GET  /api/wiki/ns/{ns}         — pages in a namespace
///   GET  /api/wiki/pages           — paginated listing of all pages (X-Total-Count header)
///   GET  /api/wiki/counts          — page counts by state (published, draft), and restricted pages
///   GET  /api/wiki/category/{cat}  — pages in a category (subcategories are its category-namespace rows)
///   GET  /api/wiki/category-names  — each category's name in the reader's locale
///   POST /api/wiki/exists          — batch page-existence check (redlinks)
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiBrowseController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	IWikiNameResolver names,
	ILogger<WikiBrowseController> logger) : WikiControllerBase(wikiService, localization, access, names, logger)
{
	/// <summary>
	/// GET /api/wiki/recent?count=20&amp;lang=fr
	/// Returns recently updated pages with titles resolved into the reader's locale, one row per page.
	/// </summary>
	[HttpGet("recent")]
	public async Task<IActionResult> GetRecentChanges([FromQuery] int count = 20, [FromQuery] string? lang = null)
	{
		var pages = await Wiki.GetRecentChangesAsync(Math.Clamp(count, 0, MaxListTake), await VisibilityAsync());
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
		(skip, take) = ClampPage(skip, take);
		var pages = await Wiki.GetByNamespaceAsync(ParseNamespace(ns), skip, take, await VisibilityAsync());
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/pages?skip=0&amp;take=50&amp;ns=&amp;lang=fr
	/// Paginated listing of all pages (optionally restricted to a namespace), with localized titles.
	/// The X-Total-Count response header carries the unpaginated count of the pages this caller may see.
	/// Anonymous callers only see published pages.
	/// </summary>
	/// <remarks>
	/// The count takes the same visibility the rows are filtered by, so the total matches the collection
	/// and discloses no draft or restricted page; it can be sent to everyone, and has to be — a paginated
	/// listing without a total cannot be paged through.
	/// </remarks>
	[HttpGet("pages")]
	public async Task<IActionResult> ListAllPages(
		[FromQuery] int skip = 0, [FromQuery] int take = 50,
		[FromQuery] string? ns = null, [FromQuery] string? lang = null)
	{
		var nsFilter = ParseOptionalNamespace(ns);
		(skip, take) = ClampPage(skip, take);
		var visibility = await VisibilityAsync();
		var pages = await Wiki.GetAllPagesAsync(skip, take, nsFilter, visibility);
		Response.Headers["X-Total-Count"] = (await Wiki.CountPagesAsync(nsFilter, visibility)).ToString();
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/counts
	/// The pages the caller may see by state, the same population <see cref="ListAllPages"/>' total counts,
	/// and how many pages carry requirements of their own.
	/// </summary>
	[HttpGet("counts")]
	public async Task<IActionResult> GetCounts()
	{
		var visibility = await VisibilityAsync();
		var counts = await Wiki.CountPagesByStateAsync(visibility);
		// Only pages this caller may see: counting a hidden draft's rule would tell them it exists.
		var restricted = await (await Access.RequirementsAsync()).Sets
			.Where(set => set.Target.Scope == WikiRuleScope.Page)
			.ToAsyncEnumerable()
			.CountAsync(async (set, _) => await Wiki.GetByIdAsync(set.Target.Key) is WikiPage page && visibility.Admits(page));

		return Ok(new WikiPageCountsDto(counts.Total, counts.Published, counts.Drafts, restricted));
	}

	/// <summary>
	/// GET /api/wiki/category/{category}?skip=0&amp;take=50&amp;lang=fr
	/// Lists the pages in <c>{category}</c>, with localized titles. Rows in the
	/// <c>category</c> namespace are its subcategories. Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("category/{category}")]
	public async Task<IActionResult> ListCategoryPages(
		string category, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		(skip, take) = ClampPage(skip, take);
		var pages = await Wiki.GetByCategoryAsync(category, skip, take, await VisibilityAsync());
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/category-names?lang=fr
	/// Each category's name, keyed by category key: the title of its published page in the category
	/// namespace, translated into <c>lang</c> where that page has a published translation. A category
	/// with no page is absent; the client shows its key.
	/// </summary>
	[HttpGet("category-names")]
	public async Task<IActionResult> GetCategoryNames([FromQuery] string? lang = null) =>
		Ok(await Localization.GetCategoryNamesAsync(lang, await VisibilityAsync()));

	/// <summary>
	/// POST /api/wiki/exists
	/// Batch existence check used by the client to mark redlinks at view time.
	/// Returns a map of each requested ref to whether the page exists (and is
	/// visible to the caller — drafts and pages the caller may not read count as missing).
	/// </summary>
	[HttpPost("exists")]
	[AllowAnonymous]
	public async Task<IActionResult> CheckExists([FromBody] ExistsRequest request)
	{
		const int maxRefs = 200;
		var result = new Dictionary<string, bool>(StringComparer.Ordinal);

		foreach (var reference in request.Refs.Distinct(StringComparer.Ordinal).Take(maxRefs))
		{
			var (ns, slug) = ParseRef(reference);
			result[reference] = await Wiki.GetBySlugAsync(slug, ns) is WikiPage page && await CanSeeAsync(page);
		}

		return Ok(result);
	}
}
