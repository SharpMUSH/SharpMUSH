using Bunit;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

public class WikiCollectionBrowserTests : TrackingBunitContext
{
	[Test]
	[Arguments("hidden_intro")]
	[Arguments("missing_intro")]
	public async Task PinnedCategoryWithoutAuthorizedIntroductionKeepsOnlyMemberBrowsing(string key)
	{
		var (api, _) = WikiApiFake.Install(this);
		api.NavigationResult = new WikiNavigationDto("game", 1, [],
			[new WikiCategorySummaryDto(key, "Public members", 1, false, true)]);
		api.CollectionRows = [WikiApiFake.Page("1", "public_procedure", "Public procedure", key)];
		var cut = Render<WikiCollectionBrowser>();
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));

		await Assert.That(cut.FindAll("a.wiki-collection-category-intro")).IsEmpty();
		await Assert.That(cut.FindAll(".wiki-collection-featured a")).IsEmpty();
		await Assert.That(cut.FindAll(".wiki-collection-directory button").Any(button =>
			(button.GetAttribute("aria-label") ?? "").Contains("Public members", StringComparison.Ordinal))).IsTrue();
		await Assert.That(cut.Markup).DoesNotContain("Private orientation");
		await Assert.That(api.Requests.Count).IsEqualTo(2);
	}

	[Test]
	public async Task AuthorizedPinnedIntroductionsAreFeaturedBeforeSearchWithSeparateMemberActions()
	{
		var (api, _) = WikiApiFake.Install(this);
		api.CollectionRows = Enumerable.Range(0, 151)
			.Select(i => WikiApiFake.Page("1", $"manual_{i:D3}", $"Workshop manual {i:D3}", "equipment")).ToArray();
		api.NavigationResult = new WikiNavigationDto("package:workshop", 151,
			[new WikiCollectionSummaryDto("package:workshop", "Workshop manual", 151)],
			[new WikiCategorySummaryDto("orientation", "Start here — Workshop manual", 1, true, true),
			 new WikiCategorySummaryDto("reference", "Torque reference", 6, true, true),
			 new WikiCategorySummaryDto("missing_intro", "Public members", 3, false, true),
			 new WikiCategorySummaryDto("equipment", "Equipment directory", 151, true, false)]);
		var cut = Render<WikiCollectionBrowser>(parameters => parameters
			.Add(p => p.Collection, "package:workshop").Add(p => p.Search, "Workshop manual")
			.Add(p => p.Category, "equipment").Add(p => p.Skip, 100).Add(p => p.Locale, "fr-CA"));
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));

		var featured = cut.Find(".wiki-collection-featured");
		await Assert.That(featured.GetAttribute("aria-label")).IsEqualTo("Featured topics");
		var links = featured.QuerySelectorAll("a.wiki-collection-category-intro");
		await Assert.That(links.Length).IsEqualTo(2);
		await Assert.That(links[0].TextContent).IsEqualTo("Start here — Workshop manual");
		await Assert.That(links[1].TextContent).IsEqualTo("Torque reference");
		await Assert.That(cut.Markup.IndexOf("wiki-collection-featured", StringComparison.Ordinal)
			< cut.Markup.IndexOf("wiki-collection-search", StringComparison.Ordinal)).IsTrue();
		var target = new Uri(new Uri("http://localhost/"), links[0].GetAttribute("href"));
		await Assert.That(target.AbsolutePath).IsEqualTo("/wiki/category/orientation");
		var query = System.Web.HttpUtility.ParseQueryString(target.Query);
		await Assert.That(query["browseCollection"]).IsEqualTo("package:workshop");
		await Assert.That(query["browseQ"]).IsEqualTo("Workshop manual");
		await Assert.That(query["browseCategory"]).IsEqualTo("equipment");
		await Assert.That(query["browseSkip"]).IsEqualTo("100");
		await Assert.That(query["membersCollection"]).IsEqualTo("package:workshop");
		await Assert.That(query["lang"]).IsEqualTo("fr-CA");
		await Assert.That(featured.QuerySelectorAll("button").Any(button =>
			(button.GetAttribute("aria-label") ?? "").Contains("Start here — Workshop manual", StringComparison.Ordinal))).IsTrue();
		await Assert.That(featured.TextContent).DoesNotContain("Public members");
		await Assert.That(featured.TextContent).DoesNotContain("Equipment directory");
		await Assert.That(cut.FindAll("a").Any(link =>
			new Uri(new Uri("http://localhost/"), link.GetAttribute("href")).AbsolutePath == "/wiki/category/missing_intro")).IsFalse();
		await Assert.That(cut.Find(".wiki-collection-directory").TextContent).Contains("Public members");
		await Assert.That(api.Requests.Count).IsEqualTo(2)
			.Because("featured navigation reuses the authorized category summaries without discovering an article slug");
	}

	[Test]
	public async Task CharacterBiographyResultKeepsCanonicalProfileDestinationAndRequestedLocale()
	{
		var (api, _) = WikiApiFake.Install(this);
		api.CollectionRows = [WikiApiFake.Page("1", "Ada", "Ada biography", "guides", ns: "character")];
		var cut = Render<WikiCollectionBrowser>(parameters => parameters
			.Add(p => p.Collection, "all").Add(p => p.Locale, "fr-CA"));
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));

		var destination = new Uri(new Uri("http://localhost/"), cut.Find(".wiki-collection-page").GetAttribute("href"));
		await Assert.That(destination.AbsolutePath).IsEqualTo("/character/ada");
		await Assert.That(System.Web.HttpUtility.ParseQueryString(destination.Query)["lang"]).IsEqualTo("fr-CA");
	}

	[Test]
	public async Task CollectionResultsPagePast500AndSearchTheWholeCollection()
	{
		var (api, _) = WikiApiFake.Install(this);
		api.CollectionRows = Enumerable.Range(0, 611).Select(i => WikiApiFake.Page("1", $"rule_{i:D4}", $"Rule {i:D4}", "guides")).ToArray();
		var cut = Render<WikiCollectionBrowser>(parameters => parameters.Add(p => p.Collection, "package:pf2e-remaster"));
		cut.WaitForAssertion(() => { if (cut.FindAll(".wiki-collection-page").Count != 50) throw new InvalidOperationException("first page missing"); });
		for (var i = 0; i < 10; i++)
		{
			await cut.FindAll(".wiki-collection-paging button")[1].ClickAsync(new());
			var expected = $"Rule {(i + 1) * 50:D4}";
			cut.WaitForAssertion(() => { if (!cut.Find(".wiki-collection-page").TextContent.Contains(expected)) throw new InvalidOperationException("page did not advance"); });
		}
		await Assert.That(cut.Find(".wiki-collection-page").TextContent).Contains("Rule 0500");
		await cut.Find(".wiki-collection-search input").InputAsync("Rule 0610");
		await cut.Find(".wiki-collection-search").SubmitAsync();
		cut.WaitForAssertion(() => { if (cut.FindAll(".wiki-collection-page").Count != 1) throw new InvalidOperationException("scoped search missing"); });
		await Assert.That(cut.Find(".wiki-collection-page").TextContent).Contains("Rule 0610");
		await Assert.That(api.Requests.Any(path => path.Contains("skip=500", StringComparison.Ordinal))).IsTrue();
	}
	[Test]
	public async Task RevokedNavigationRemovesPreviouslyVisibleCollectionChrome()
	{
		var (api, _) = WikiApiFake.Install(this);
		api.CollectionRows = Enumerable.Range(0, 61)
			.Select(i => WikiApiFake.Page("1", $"rule_{i:D2}", $"Rule {i:D2}", "guides")).ToArray();
		var cut = Render<WikiCollectionBrowser>(parameters => parameters.Add(p => p.Collection, "all"));
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));
		api.Refuse = true;
		await cut.FindAll(".wiki-collection-paging button")[1].ClickAsync(new());
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-error"));
		await Assert.That(cut.FindAll(".wiki-collection-page")).IsEmpty();
		await Assert.That(cut.FindAll(".wiki-collection-topics, .wiki-collection-directory")).IsEmpty()
			.Because("a revoked navigation read must remove titles and categories retained from the previous authorized response");
	}

}
