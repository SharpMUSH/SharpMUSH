using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The article aside's newest pages of its category. The server reads the category a batch at a time and
/// returns the newest few and the count; the answer is what the portal used to work out from every row.
/// </summary>
public class WikiCategoryNewestTests
{
	private const string Category = "lore";

	/// <summary>
	/// 230 published pages in main, past the server's 200-row batch, three in help, and two drafts the
	/// anonymous reader may not see.
	/// </summary>
	private static async Task<WikiStoreService> SeedAsync()
	{
		var wiki = InMemoryWikiStore.CreateService();
		for (var i = 0; i < 230; i++)
			await AddAsync(wiki, $"Main {i:D3}", WikiNamespace.Main, published: true);
		for (var i = 0; i < 3; i++)
			await AddAsync(wiki, $"Help {i}", WikiNamespace.Help, published: true);
		for (var i = 0; i < 2; i++)
			await AddAsync(wiki, $"Zz draft {i}", WikiNamespace.Main, published: false);
		return wiki;
	}

	private static async Task AddAsync(WikiStoreService wiki, string title, WikiNamespace ns, bool published)
	{
		var page = (await wiki.CreateAsync(title, $"# {title}", "#1", ns, "en")).Expect<WikiPage>();
		await wiki.SetMetadataAsync(page.Id, [Category], published);
	}

	/// <summary>The portal's old answer: every visible row of the category, filtered, sorted, the first three.</summary>
	private static async Task<(List<string> Slugs, int Total)> FromEveryRowAsync(WikiEndpoints wiki, string ns, string exclude)
	{
		var rows = new List<WikiPageSummaryDto>();
		for (var skip = 0; ; skip += 100)
		{
			var batch = ((IEnumerable<WikiPageSummaryDto>)((OkObjectResult)await wiki.Browse.ListCategoryPages(Category, skip, 100)).Value!).ToList();
			rows.AddRange(batch);
			if (batch.Count < 100) break;
		}
		var siblings = rows
			.Where(p => p.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase))
			.Where(p => !p.Slug.Equals(exclude, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(p => p.UpdatedAt)
			.ToList();
		return ([.. siblings.Take(3).Select(p => p.Slug)], siblings.Count + 1);
	}

	[Test]
	[Arguments("main", "Main 100")]
	[Arguments("help", "Help 0")]
	public async Task NewestMatchesWhatEveryRowWouldGive(string ns, string excludedTitle)
	{
		var exclude = WikiHelpers.Slugify(excludedTitle);
		var wiki = WikiControllerTestHarness.Build(await SeedAsync(), authenticated: false, "#42").Wiki;

		var result = (WikiCategoryNewestDto)((OkObjectResult)await wiki.Browse.NewestInCategory(Category, ns, exclude, 3)).Value!;
		var (slugs, total) = await FromEveryRowAsync(wiki, ns, exclude);

		await Assert.That(result.Pages.Select(p => p.Slug)).IsEquivalentTo(slugs, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(result.Total).IsEqualTo(total);
		await Assert.That(result.Total).IsEqualTo(ns == "main" ? 230 : 3);
		await Assert.That(result.Pages.Any(p => p.Slug.StartsWith(WikiHelpers.Slugify("Zz draft")))).IsFalse();
	}
}
