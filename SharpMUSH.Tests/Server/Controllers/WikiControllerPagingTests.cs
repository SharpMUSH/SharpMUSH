using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// A listing page is a page of the rows the caller may see. Paging over stored rows and dropping
/// hidden drafts afterwards returned a short or empty page whenever drafts sorted first, and a
/// client paging until an empty page stopped before the visible rows.
/// </summary>
public class WikiControllerPagingTests
{
	private const int HiddenDrafts = 60;
	private const string Category = "lore";

	/// <summary>
	/// Sixty drafts by someone else whose titles and slugs sort first, then five published pages and
	/// two drafts by the caller (#42), all in one category.
	/// </summary>
	private static async Task<(WikiStoreService Wiki, string[] Published, string[] Own)> SeedAsync()
	{
		var wiki = InMemoryWikiStore.CreateService();
		for (var i = 0; i < HiddenDrafts; i++)
		{
			await AddAsync(wiki, $"A hidden {i:D2}", "#1", published: false);
		}

		var published = new string[5];
		for (var i = 0; i < published.Length; i++)
		{
			published[i] = await AddAsync(wiki, $"B visible {i}", "#1", published: true);
		}

		string[] own = [await AddAsync(wiki, "C own 0", "#42", published: false), await AddAsync(wiki, "C own 1", "#42", published: false)];
		return (wiki, published, own);
	}

	private static async Task<string> AddAsync(WikiStoreService wiki, string title, string author, bool published)
	{
		var page = (await wiki.CreateAsync(title, $"# {title}", author)).Expect<WikiPage>();
		await wiki.SetMetadataAsync(page.Id, [Category], published);
		return page.Slug;
	}

	private static WikiEndpoints Anonymous(WikiStoreService wiki) =>
		WikiControllerTestHarness.Build(wiki, authenticated: false, "#42").Wiki;

	/// <summary>Signed in as #42 without wiki.drafts: sees published pages and its own drafts.</summary>
	private static WikiEndpoints Author(WikiStoreService wiki) =>
		WikiControllerTestHarness.Build(wiki, authenticated: true, "#42").Wiki;

	private static WikiEndpoints Reader(WikiStoreService wiki) =>
		WikiControllerTestHarness.Build(wiki, authenticated: true, "#42", PortalPermission.WikiDrafts).Wiki;

	private static readonly Func<WikiEndpoints, int, int, Task<IActionResult>>[] Listings =
	[
		(e, skip, take) => e.Browse.ListCategoryPages(Category, skip, take),
		(e, skip, take) => e.Browse.ListNamespacePages("main", skip, take),
		(e, skip, take) => e.Browse.ListAllPages(skip, take),
	];

	private static async Task<List<string>> SlugsAsync(Task<IActionResult> listing)
	{
		var ok = (await listing) as OkObjectResult;
		return ((IEnumerable<WikiPageSummaryDto>)ok!.Value!).Select(p => p.Slug).ToList();
	}

	/// <summary>Pages through a listing until it returns an empty page, as a client does.</summary>
	private static async Task<List<string>> PageThroughAsync(WikiEndpoints endpoints, int listing, int take)
	{
		var all = new List<string>();
		for (var skip = 0; ; skip += take)
		{
			var page = await SlugsAsync(Listings[listing](endpoints, skip, take));
			if (page.Count == 0) return all;
			all.AddRange(page);
		}
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task TheFirstPage_IsFullOfVisibleRows(int listing)
	{
		var (wiki, published, _) = await SeedAsync();

		var first = await SlugsAsync(Listings[listing](Anonymous(wiki), 0, 3));

		await Assert.That(first.Count).IsEqualTo(3)
			.Because("sixty hidden drafts sort first, and must not take the page's places");
		await Assert.That(published).Contains(first[0]);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task PagingUntilEmpty_ReachesEveryVisibleRow(int listing)
	{
		var (wiki, published, own) = await SeedAsync();

		await Assert.That(await PageThroughAsync(Anonymous(wiki), listing, 50)).IsEquivalentTo(published);
		await Assert.That(await PageThroughAsync(Author(wiki), listing, 2)).IsEquivalentTo(published.Concat(own))
			.Because("a caller's own drafts are visible to them, so they are part of their pages");
		await Assert.That((await PageThroughAsync(Reader(wiki), listing, 50)).Count).IsEqualTo(HiddenDrafts + published.Length + own.Length);
	}

	[Test]
	public async Task RecentChanges_CountsVisibleRows()
	{
		// The published pages first, then sixty hidden drafts: every draft is more recent.
		var wiki = InMemoryWikiStore.CreateService();
		var published = new string[5];
		for (var i = 0; i < published.Length; i++)
		{
			published[i] = await AddAsync(wiki, $"B visible {i}", "#1", published: true);
		}

		for (var i = 0; i < HiddenDrafts; i++)
		{
			await AddAsync(wiki, $"A hidden {i:D2}", "#1", published: false);
		}

		var recent = await SlugsAsync(Anonymous(wiki).Browse.GetRecentChanges(count: 5));

		await Assert.That(recent).IsEquivalentTo(published);
	}
}
