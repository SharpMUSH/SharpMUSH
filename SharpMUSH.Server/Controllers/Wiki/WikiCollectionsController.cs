using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Server.Authentication;
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
	private readonly WikiLandingReaderResolver? _landingReaders;
	private readonly WikiLandingReadService? _landingRead;

	[ActivatorUtilitiesConstructor]
	public WikiCollectionsController(IWikiService wiki, IWikiLocalizationService localization,
		IWikiAccessService access, IWikiNameResolver names, IPackageRegistryService packages,
		ILogger<WikiCollectionsController> logger, WikiLandingReaderResolver landingReaders, WikiLandingReadService landingRead)
		: this(wiki, localization, access, names, packages, logger)
	{
		_landingReaders = landingReaders;
		_landingRead = landingRead;
	}

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
		Response.Headers.CacheControl = "private, no-store";
		if (_landingReaders is { } readers)
		{
			if (await readers.AuthenticateAsync(HttpContext) is not ClaimsPrincipal principal) return NotFound();
			HttpContext.User = principal;
			UseReader(await readers.ResolveAsync(User, HttpContext.RequestAborted));
		}
		else if (OAuthBearerAuthenticationHandler.AppToken(Request) is not null) return NotFound();
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
		WikiLandingPageDto? landing = null;
		if (_landingRead is { } read
			&& await read.LandingAsync(User, collection, lang, HttpContext.RequestAborted) is WikiLandingPageDto resolvedLanding)
			landing = resolvedLanding;
		return Ok(new WikiNavigationDto(collection, total, collections, categories.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList())
		{ LandingPage = landing });
	}

	private Task<Result<WikiPageWindow>> WindowAsync(string? category, WikiPageCursor cursor, int take, WikiVisibility visibility)
		=> string.IsNullOrWhiteSpace(category)
			? Wiki.GetAllPagesWindowAsync(cursor, take, visibility)
			: Wiki.GetByCategoryWindowAsync(category, cursor, take, visibility);

	private async Task<WikiVisibility> SearchAsync(WikiVisibility visibility, string? search, string? lang)
	{
		if (string.IsNullOrWhiteSpace(search)) return visibility;
		var term = search.Trim();
		var names = await Localization.GetCategoryNamesAsync(lang, visibility);
		var matching = new HashSet<string>(StringComparer.Ordinal);
		const int batchSize = 128;
		WikiPageScanPosition position = new WikiPageScanStart();
		while (true)
		{
			var batch = await Wiki.ScanPagesAsync(visibility, position, batchSize);
			var localized = await Localization.LocalizeAllAsync(batch.Pages, lang, await IncludeDraftsAsync());
			foreach (var row in localized.Where(row => row.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.Page.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.Page.Categories.Any(key => WikiHelpers.CategoryLabel(key, names).Contains(term, StringComparison.OrdinalIgnoreCase))))
				matching.Add(row.Page.Id);
			if (batch.Next is not WikiPageScanContinuation continuation) break;
			position = continuation;
			HttpContext.RequestAborted.ThrowIfCancellationRequested();
		}
		return visibility with { IncludedPageIds = matching };
	}

	/// <summary>
	/// GET /api/wiki/collections/{collection}/pages?skip=0&amp;take=50&amp;category=Spells&amp;after=…
	/// One page of a collection, or of one category in it, with the count in <c>X-Total-Count</c>. <c>X-Next-Page</c>
	/// and <c>X-Previous-Page</c> carry the tokens for the pages either side, sent back as <c>after</c> and
	/// <c>before</c>; a token reads from where the last page stopped, where <c>skip</c> reads every row before it.
	/// A token the wiki did not write is read as <c>skip</c>.
	/// </summary>
	[HttpGet("collections/{collection}/pages")]
	public async Task<IActionResult> Pages(string collection, [FromQuery] int skip = 0, [FromQuery] int take = 50,
		[FromQuery] string? category = null, [FromQuery] string? search = null, [FromQuery] string? lang = null,
		[FromQuery] string? after = null, [FromQuery] string? before = null)
	{
		if (search?.Length > 200) return BadRequest("Search must be at most 200 characters.");
		// Use only the already-authenticated principal here. This endpoint does not opt into OAuth.
		if (_landingReaders is { } readers)
			UseReader(await readers.ResolveAsync(User, HttpContext.RequestAborted));
		var membership = await MembershipAsync();
		var visibility = Select(await VisibilityAsync(), membership, collection);
		// Test collection visibility before search: zero search matches must be an empty page, not a missing collection.
		if (collection is not ("game" or "all") && await Wiki.CountPagesAsync(null, visibility) == 0) return NotFound();
		visibility = await SearchAsync(visibility, search, lang);
		(skip, take) = ClampPage(skip, take);
		WikiPageCursor cursor = after is { Length: > 0 } ? new WikiPageAfter(after)
			: before is { Length: > 0 } ? new WikiPageBefore(before)
			: new WikiPageAt(skip);
		var read = await WindowAsync(category, cursor, take, visibility);
		if (read is not WikiPageWindow) read = await WindowAsync(category, new WikiPageAt(skip), take, visibility);
		if (read is not WikiPageWindow window) return Problem("The wiki listing could not be read.");
		var rows = window.Pages;
		var total = string.IsNullOrWhiteSpace(category)
			? await Wiki.CountPagesAsync(null, visibility)
			: await Wiki.CountPagesInCategoryAsync(category, visibility);
		if (window.Next is { } next) Response.Headers["X-Next-Page"] = next;
		if (window.Previous is { } previous) Response.Headers["X-Previous-Page"] = previous;
		Response.Headers["X-Total-Count"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
		return Ok((await LocalizedListAsync(rows, lang)).Select(row =>
		{
			var owner = membership.Packages.FirstOrDefault(pair => pair.Value.Contains(row.Id)).Key;
			return row with { CollectionId = owner ?? "game", CollectionName = owner is null ? null : CollectionLabel(membership, owner) };
		}).ToList());
	}
}
