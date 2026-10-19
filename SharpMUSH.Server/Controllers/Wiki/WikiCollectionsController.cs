using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>Collections use durable package ownership, without rewriting page keys or existing links.</summary>
[ApiController]
[Route("api/wiki")]
public sealed class WikiCollectionsController(IWikiService wiki, IWikiLocalizationService localization,
	IWikiAccessService access, IWikiNameResolver names, IPackageRegistryService packages,
	ILogger<WikiCollectionsController> logger) : WikiControllerBase(wiki, localization, access, names, logger)
{
	private sealed record Membership(IReadOnlyDictionary<string, HashSet<string>> Packages, HashSet<string> All,
		IReadOnlyDictionary<string, string> Labels);

	private async Task<Membership> MembershipAsync()
	{
		var records = await packages.GetAllPackageWikiPagesAsync();
		// Ownership is a stable page ID, never a title/slug heuristic. A recreated page has a different ID.
		var ambiguous = records.GroupBy(record => record.PageId, StringComparer.Ordinal)
			.Any(group => group.Select(record => record.PackageId).Distinct(StringComparer.Ordinal).Skip(1).Any());
		if (ambiguous) throw new InvalidDataException("A wiki page has conflicting package ownership.");
		var groups = records.GroupBy(record => "package:" + record.PackageId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Select(record => record.PageId).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
		var labels = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var installed in await packages.GetInstalledPackagesAsync())
		{
			if (!string.IsNullOrWhiteSpace(installed.WikiCollectionName))
				labels["package:" + installed.Id] = installed.WikiCollectionName.Trim();
		}
		return new Membership(groups, records.Select(record => record.PageId).ToHashSet(StringComparer.Ordinal), labels);
	}

	private static string CollectionLabel(Membership membership, string collection) =>
		membership.Labels.GetValueOrDefault(collection) ?? collection["package:".Length..].Replace('-', ' ');

	private static WikiVisibility Select(WikiVisibility visibility, Membership membership, string collection)
		=> collection switch
		{
			"all" => visibility,
			"game" => visibility with { ExcludedPageIds = membership.All },
			_ => visibility with { IncludedPageIds = membership.Packages.GetValueOrDefault(collection) ?? [] }
		};

	[HttpGet("navigation")]
	public async Task<IActionResult> Navigation([FromQuery] string? collection = null, [FromQuery] string? lang = null,
		[FromQuery] string? page = null)
	{
		var visibility = await VisibilityAsync();
		var membership = await MembershipAsync();
		if (collection is null && page is { Length: > 0 and <= 512 })
		{
			var (ns, slug) = ParseRef(page);
			if (await Wiki.GetBySlugAsync(slug, ns) is WikiPage current && visibility.Admits(current))
				collection = membership.Packages.FirstOrDefault(pair => pair.Value.Contains(current.Id)).Key;
		}
		collection ??= "game";
		var selected = Select(visibility, membership, collection);
		var total = await Wiki.CountPagesAsync(null, selected);
		if (collection is not ("game" or "all") && total == 0) return NotFound();
		var collections = new List<WikiCollectionSummaryDto>();
		foreach (var key in membership.Packages.Keys.Order(StringComparer.Ordinal))
		{
			var count = await Wiki.CountPagesAsync(null, Select(visibility, membership, key));
			if (count > 0) collections.Add(new(key, CollectionLabel(membership, key), count));
		}
		var counts = await Wiki.CountPagesByCategoryAsync(selected);
		var labels = await Localization.GetCategoryNamesAsync(lang, selected);
		var categoryPages = await Wiki.GetByNamespaceAsync(WikiNamespace.Category, 0, int.MaxValue, selected);
		var titled = categoryPages.ToDictionary(row => WikiHelpers.CategoryKey(row.Slug), row => row.Title, StringComparer.Ordinal);
		var pinned = (await Wiki.GetPinnedCategoriesAsync()).ToHashSet(StringComparer.Ordinal);
		var categories = new List<WikiCategorySummaryDto>();
		foreach (var key in counts.Keys.Union(titled.Keys).Order(StringComparer.Ordinal))
		{
			var preview = pinned.Contains(key) ? (await Wiki.GetByCategoryAsync(key, 0, 1, selected)).FirstOrDefault() : null;
			categories.Add(new(key, labels.GetValueOrDefault(key) ?? titled.GetValueOrDefault(key) ?? WikiHelpers.CategoryLabel(key),
				counts.GetValueOrDefault(key), titled.ContainsKey(key), pinned.Contains(key))
			{ Image = preview is null ? null : WikiImages.FirstImageUrl(preview.RenderedHtml) });
		}
		return Ok(new WikiNavigationDto(collection, total, collections, categories.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList()));
	}

	private async Task<WikiVisibility> SearchAsync(WikiVisibility visibility, string? search, string? lang)
	{
		if (string.IsNullOrWhiteSpace(search)) return visibility;
		var term = search.Trim();
		var names = await Localization.GetCategoryNamesAsync(lang, visibility);
		var matching = new HashSet<string>(StringComparer.Ordinal);
		const int batchSize = 128;
		for (var skip = 0; ; skip += batchSize)
		{
			var batch = await Wiki.GetAllPagesAsync(skip, batchSize, visibility: visibility);
			var localized = await Localization.LocalizeAllAsync(batch, lang, await IncludeDraftsAsync());
			foreach (var row in localized.Where(row => row.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.Page.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.Page.Categories.Any(key => WikiHelpers.CategoryLabel(key, names).Contains(term, StringComparison.OrdinalIgnoreCase))))
				matching.Add(row.Page.Id);
			if (batch.Count < batchSize) break;
			HttpContext.RequestAborted.ThrowIfCancellationRequested();
		}
		return visibility with { IncludedPageIds = matching };
	}

	[HttpGet("collections/{collection}/pages")]
	public async Task<IActionResult> Pages(string collection, [FromQuery] int skip = 0, [FromQuery] int take = 50,
		[FromQuery] string? category = null, [FromQuery] string? search = null, [FromQuery] string? lang = null)
	{
		if (search?.Length > 200) return BadRequest("Search must be at most 200 characters.");
		var membership = await MembershipAsync();
		var visibility = Select(await VisibilityAsync(), membership, collection);
		// Test collection visibility before search: zero search matches must be an empty page, not a missing collection.
		if (collection is not ("game" or "all") && await Wiki.CountPagesAsync(null, visibility) == 0) return NotFound();
		visibility = await SearchAsync(visibility, search, lang);
		(skip, take) = ClampPage(skip, take);
		IReadOnlyList<WikiPage> rows;
		int total;
		if (string.IsNullOrWhiteSpace(category))
		{
			rows = await Wiki.GetAllPagesAsync(skip, take, visibility: visibility);
			total = await Wiki.CountPagesAsync(null, visibility);
		}
		else
		{
			rows = await Wiki.GetByCategoryAsync(category, skip, take, visibility);
			total = await Wiki.CountPagesInCategoryAsync(category, visibility);
		}
		Response.Headers["X-Total-Count"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
		return Ok((await LocalizedListAsync(rows, lang)).Select(row =>
		{
			var owner = membership.Packages.FirstOrDefault(pair => pair.Value.Contains(row.Id)).Key;
			return row with { CollectionId = owner ?? "game", CollectionName = owner is null ? null : CollectionLabel(membership, owner) };
		}).ToList());
	}
}
