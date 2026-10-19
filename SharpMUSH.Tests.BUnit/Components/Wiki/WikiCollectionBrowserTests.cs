using Bunit;
using SharpMUSH.Client.Components.Wiki;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

public class WikiCollectionBrowserTests : TrackingBunitContext
{
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
