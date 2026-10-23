using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;
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
///   GET  /api/wiki/categories      — every category, with its name, how many pages it holds and whether it is pinned
///   GET  /api/wiki/pinned-categories — the keys of the categories pinned to the wiki home
///   GET  /api/wiki/fact-properties — the properties facts may state, with their types
///   GET  /api/wiki/query           — the pages whose facts meet every condition
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

	/// <summary>Category rows read at a time by <see cref="NewestInCategory"/>.</summary>
	private const int NewestBatch = 200;

	/// <summary>
	/// GET /api/wiki/category/{category}/newest?ns=main&amp;exclude=dragons&amp;take=3&amp;lang=fr
	/// The <c>take</c> most recently updated pages of <c>{category}</c> in namespace <c>ns</c>, leaving out
	/// <c>exclude</c>, and how many pages the category holds in that namespace. The rows are read here, a
	/// batch at a time, so only the newest few and the count leave the server.
	/// </summary>
	[HttpGet("category/{category}/newest")]
	public async Task<IActionResult> NewestInCategory(string category, [FromQuery] string ns = "main",
		[FromQuery] string? exclude = null, [FromQuery] int take = 3, [FromQuery] string? lang = null)
	{
		take = Math.Clamp(take, 0, MaxListTake);
		var visibility = await VisibilityAsync();
		var includeDrafts = await IncludeDraftsAsync();
		var total = 0;
		var newest = new List<LocalizedWikiPage>(take + 1);
		for (var skip = 0; ; skip += NewestBatch)
		{
			var batch = await Wiki.GetByCategoryAsync(category, skip, NewestBatch, visibility);
			var visible = await FilterVisibleAsync(batch);
			var localized = await Localization.LocalizeAllAsync(visible, lang, includeDrafts);
			foreach (var page in localized.Where(page => page.Page.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase)))
			{
				total++;
				if (exclude is not null && page.Page.Slug.Equals(exclude, StringComparison.OrdinalIgnoreCase)) continue;
				var at = newest.FindIndex(kept => kept.UpdatedAt < page.UpdatedAt);
				newest.Insert(at < 0 ? newest.Count : at, page);
				if (newest.Count > take) newest.RemoveAt(take);
			}
			if (batch.Count < NewestBatch) break;
			HttpContext.RequestAborted.ThrowIfCancellationRequested();
		}
		return Ok(new WikiCategoryNewestDto([.. await SummariesAsync(newest)], total));
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
	/// GET /api/wiki/categories?lang=fr
	/// Every category a page the caller may see is filed in, every category with a page of its own, and every
	/// pinned category, ordered by name.
	/// </summary>
	[HttpGet("categories")]
	public async Task<IActionResult> GetCategories([FromQuery] string? lang = null)
	{
		var visibility = await VisibilityAsync();
		var counts = await Wiki.CountPagesByCategoryAsync(visibility);
		// Names come from published category pages; a draft one the caller may see still has its page, and
		// shows its own title.
		var names = await Localization.GetCategoryNamesAsync(lang, visibility);
		var pages = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var page in await FilterVisibleAsync(await Wiki.GetByNamespaceAsync(WikiNamespace.Category, 0, int.MaxValue, visibility)))
			pages.TryAdd(WikiHelpers.CategoryKey(page.Slug), page.Title);
		var pinned = (await Wiki.GetPinnedCategoriesAsync()).ToHashSet(StringComparer.Ordinal);
		return Ok(counts.Keys.Union(names.Keys).Union(pages.Keys).Union(pinned)
			.Select(key => new WikiCategorySummaryDto(key,
				names.TryGetValue(key, out var name) && name.Length > 0 ? name
				: pages.TryGetValue(key, out var title) && title.Length > 0 ? title
				: WikiHelpers.CategoryLabel(key),
				counts.GetValueOrDefault(key), names.ContainsKey(key) || pages.ContainsKey(key), pinned.Contains(key)))
			.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
			.ToList());
	}

	/// <summary>
	/// GET /api/wiki/fact-properties
	/// The properties a page's facts may state: each one's id, type, whether it takes several values, a keyword
	/// property's allowed values, and its label and description.
	/// </summary>
	[HttpGet("fact-properties")]
	public async Task<IActionResult> GetFactProperties()
	{
		var schema = await Wiki.GetFactSchemaAsync();
		return Ok((schema.Properties.IsDefault ? [] : schema.Properties)
			.Select(ToDto)
			.OrderBy(p => p.Id, StringComparer.Ordinal)
			.ToList());
	}

	/// <summary>
	/// GET /api/wiki/query?condition=pf2e:level=1..3&amp;condition=category=Spells&amp;take=50&amp;after=&amp;lang=fr
	/// The pages whose facts meet every <c>condition</c>, written as <c>wikiquery()</c> takes them, one page at a time:
	/// <c>next</c> is the next request's <c>after</c>. 400 for a query the wiki cannot answer; 503 while the fact index
	/// is building.
	/// </summary>
	[HttpGet("query")]
	public async Task<IActionResult> QueryFacts([FromQuery] string[] condition, [FromQuery] int take = 50,
		[FromQuery] string? after = null, [FromQuery] string? lang = null)
	{
		var visibility = await VisibilityAsync();
		return await WikiFactQueryText.ParseAsync(Wiki, await Wiki.GetFactSchemaAsync(), condition,
				Math.Clamp(take, 1, WikiFactQuery.MaxTake), visibility) switch
		{
			WikiFactQuery query => await AnsweredAsync(query with { After = after }, visibility, lang),
			Error<string> error => BadRequest(error.Value)
		};
	}

	private async Task<IActionResult> AnsweredAsync(WikiFactQuery query, WikiVisibility visibility, string? lang)
		=> await Wiki.QueryFactsAsync(query, visibility) switch
		{
			WikiFactQueryPage found => Ok(new WikiFactQueryResultDto([.. await LocalizedListAsync(found.Pages, lang)], found.Next)),
			WikiFactIndexBuilding building => StatusCode(StatusCodes.Status503ServiceUnavailable,
				$"The fact index is building: {building.Status.Indexed} of {building.Status.Pages} pages."),
			Error<string> error => BadRequest(error.Value)
		};

	/// <summary>
	/// GET /api/wiki/pinned-categories
	/// The keys of the categories pinned to the wiki home. Only these get a card there.
	/// </summary>
	[HttpGet("pinned-categories")]
	public async Task<IActionResult> GetPinnedCategories() => Ok(await Wiki.GetPinnedCategoriesAsync());

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
