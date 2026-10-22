using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Database.Lightning;

namespace SharpMUSH.Tests.Wiki;

/// <summary>Exact category identity through the ordinary facade and reopened native storage.</summary>
public class WikiCategoryIdentifierTests : LightningDatabaseFixture
{
	private const string Category = "a";
	private const string NulCategory = "a\0b";
	private WikiStoreService Wiki => new(Db, new WikiMarkdigPipeline());

	private async Task<WikiPage> Add(string title, string slug, string[] categories,
		bool published = true, string author = "#1", WikiNamespace ns = WikiNamespace.Main)
	{
		var page = (await Wiki.CreateAsync(title, "body", author, ns, categories: categories, slug: slug)).Expect<WikiPage>();
		if (!published)
			page = (await Wiki.SetMetadataAsync(page.Id, categories, false)).Expect<WikiPage>();
		return page;
	}

	private async Task<WikiPage> SeedAndReopen(string[] categories)
	{
		var page = await Add("Member", "member", categories);
		await ReopenAsync();
		var stored = (await Wiki.GetByIdAsync(page.Id)).Expect<WikiPage>();
		await Assert.That(stored.Categories).IsEquivalentTo(categories);
		return stored;
	}

	[Test]
	public async Task NulExtendedCategoryAloneIsAbsentFromShortCategoryListing()
	{
		var page = await SeedAndReopen([NulCategory]);
		await Assert.That((await Wiki.GetByCategoryAsync(NulCategory)).Single().Id).IsEqualTo(page.Id);
		await Assert.That(await Wiki.GetByCategoryAsync(Category)).IsEmpty();
	}

	[Test]
	public async Task NulExtendedCategoryAloneDoesNotContributeToShortCategoryCount()
	{
		await SeedAndReopen([NulCategory]);
		await Assert.That(await Wiki.CountPagesInCategoryAsync(NulCategory, WikiVisibility.PublishedOnly)).IsEqualTo(1);
		await Assert.That(await Wiki.CountPagesInCategoryAsync(Category, WikiVisibility.PublishedOnly)).IsEqualTo(0);
	}

	[Test]
	public async Task BothExactCategoriesListOneStablePageEach()
	{
		var page = await SeedAndReopen([Category, NulCategory]);
		foreach (var category in new[] { Category, NulCategory })
		{
			var listed = await Wiki.GetByCategoryAsync(category);
			await Assert.That(listed.Count).IsEqualTo(1);
			await Assert.That(listed[0].Id).IsEqualTo(page.Id);
		}
	}

	[Test]
	public async Task BothExactCategoriesCountOneMembershipEach()
	{
		await SeedAndReopen([Category, NulCategory]);
		foreach (var category in new[] { Category, NulCategory })
			await Assert.That(await Wiki.CountPagesInCategoryAsync(category, WikiVisibility.PublishedOnly)).IsEqualTo(1);
	}

	[Test]
	public async Task CategoryDictionaryRetainsBothCompleteLabelsAndExactCounts()
	{
		await SeedAndReopen([Category, NulCategory]);
		var counts = await Wiki.CountPagesByCategoryAsync(WikiVisibility.PublishedOnly);
		await Assert.That(counts.Keys).IsEquivalentTo(new[] { Category, NulCategory });
		await Assert.That(counts[Category]).IsEqualTo(1);
		await Assert.That(counts[NulCategory]).IsEqualTo(1);
	}

	[Test]
	[Arguments(0, 2)]
	[Arguments(0, 1)]
	[Arguments(1, 1)]
	[Arguments(2, 1)]
	public async Task CategoryTitleOrderAndPagingUseTheExactMembershipRow(int skip, int take)
	{
		// P's aliased row sorts as U+4200 inside the short prefix. Metadata filtering and
		// deduplication alone would admit P ahead of Q despite their actual title order.
		var p = await Add("\u6000high", "p", [Category, NulCategory]);
		var q = await Add("\u5000middle", "q", [Category]);
		await ReopenAsync();
		var expected = new[] { q.Id, p.Id }.Skip(skip).Take(take).ToArray();
		var actual = await Wiki.GetByCategoryAsync(Category, skip, take, WikiVisibility.PublishedOnly);
		await Assert.That(actual.Count).IsEqualTo(expected.Length);
		for (var i = 0; i < expected.Length; i++)
			await Assert.That(actual[i].Id).IsEqualTo(expected[i]);
	}

	[Test]
	public async Task ExactCategorySelectionPreservesDraftRestrictionsAndStableIdAdmission()
	{
		var visible = await Add("A public", "public", [Category]);
		var own = await Add("B own draft", "own", [Category, NulCategory], false, "#42");
		var other = await Add("C other draft", "other", [Category], false, "#43");
		var hiddenPage = await Add("D hidden page", "hidden-page", [Category]);
		var hiddenNamespace = await Add("E hidden namespace", "hidden-namespace", [Category], ns: WikiNamespace.Help);
		var hiddenCategory = await Add("F hidden category", "hidden-category", [Category, NulCategory]);
		var aliasOnly = await Add("G alias only", "alias-only", [NulCategory]);
		await ReopenAsync();
		var pages = new[] { visible, own, other, hiddenPage, hiddenNamespace, hiddenCategory, aliasOnly };
		var hidden = new WikiReadRestrictions(false, new HashSet<string> { "help" },
			new HashSet<string> { NulCategory }, new HashSet<string> { hiddenPage.Id });
		var visibilities = new[]
		{
			WikiVisibility.PublishedOnly, new WikiVisibility(false, "#42"), WikiVisibility.All,
			WikiVisibility.All with { Hidden = hidden },
			WikiVisibility.PublishedOnly with { Hidden = hidden },
			WikiVisibility.All with { IncludedPageIds = new HashSet<string> { visible.Id, own.Id, aliasOnly.Id } },
			WikiVisibility.All with { ExcludedPageIds = new HashSet<string> { own.Id, hiddenCategory.Id } },
			WikiVisibility.All with { Hidden = WikiReadRestrictions.All }
		};
		foreach (var visibility in visibilities)
		{
			foreach (var category in new[] { Category, NulCategory })
			{
				var expected = pages.Where(page => visibility.Admits(page)
					&& page.Categories.Contains(category, StringComparer.Ordinal))
					.OrderBy(page => page.Title, StringComparer.Ordinal).Select(page => page.Id).ToArray();
				await Assert.That(await Wiki.CountPagesInCategoryAsync(category, visibility)).IsEqualTo(expected.Length);
				var listed = await Wiki.GetByCategoryAsync(category, visibility: visibility);
				await Assert.That(listed.Count).IsEqualTo(expected.Length);
				for (var i = 0; i < expected.Length; i++)
					await Assert.That(listed[i].Id).IsEqualTo(expected[i]);
				var window = await Wiki.GetByCategoryAsync(category, skip: 1, take: 1, visibility: visibility);
				await Assert.That(window.Select(page => page.Id)).IsEquivalentTo(expected.Skip(1).Take(1));
				var counts = await Wiki.CountPagesByCategoryAsync(visibility);
				await Assert.That(counts.GetValueOrDefault(category)).IsEqualTo(expected.Length);
			}
		}
	}
}
