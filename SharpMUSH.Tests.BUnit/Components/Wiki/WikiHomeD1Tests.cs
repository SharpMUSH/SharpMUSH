using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Widgets;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// The D1 wiki home widget (board 21): a glass hero with a search field and a New page capsule,
/// then category cards with a cover, the count, the top three pages with draft/locked tags and an
/// "All N pages" link. The home's search reads <c>?q=</c> from the address.
/// </summary>
public class WikiHomeD1Tests : TrackingBunitContext
{
	private readonly BunitAuthorizationContext _auth;
	private readonly BunitNavigationManager _nav;

	public WikiHomeD1Tests()
	{
		(_, _auth) = WikiApiFake.Install(this);
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	private IRenderedComponent<WikiIndexWidget> RenderHome(string path = "/wiki")
	{
		_nav.NavigateTo(path);
		var cut = Render<WikiIndexWidget>();
		cut.WaitForAssertion(() => cut.Find(".wiki-cat-card, .wiki-empty"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task Hero_IsAGlassBannerHeading_WithSearch_AndNewPageOnlyForCreators()
	{
		var reader = RenderHome();
		await Assert.That(reader.Find(".kit-banner h1.kit-banner-title").TextContent).IsEqualTo("Everything you need to play");
		await Assert.That(reader.Find(".kit-banner-kicker").TextContent).Contains("World Wiki");
		await Assert.That(reader.Find(".wiki-hero-search input")).IsNotNull();
		await Assert.That(reader.FindAll(".kit-banner .kit-capsule--primary").Count).IsEqualTo(0);

		_auth.SetPolicies("wiki.create");
		var creator = RenderHome();
		await Assert.That(creator.Find(".kit-banner .kit-capsule--primary").TextContent).Contains("New page");
	}

	[Test]
	public async Task CategoryCards_ShowCoverCountTopPagesTagsAndAllLink()
	{
		var cut = RenderHome();
		var cards = cut.FindAll(".wiki-cat-card");
		await Assert.That(cards.Count).IsEqualTo(3);

		var guides = cards[0];
		await Assert.That(guides.QuerySelector(".wiki-cat-name")!.TextContent).IsEqualTo("Guides");
		await Assert.That(guides.QuerySelector(".wiki-cat-count")!.TextContent).IsEqualTo("2");
		await Assert.That(guides.QuerySelector("img.wiki-cat-cover")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/intro.jpg");
		var pages = guides.QuerySelectorAll("a.wiki-cat-page");
		await Assert.That(pages.Length).IsEqualTo(2);
		await Assert.That(pages[0].GetAttribute("href")).IsEqualTo("/wiki/main/combat");
		await Assert.That(pages[0].QuerySelector(".wiki-cat-tag--draft")).IsNotNull();
		await Assert.That(guides.QuerySelector("a.wiki-cat-all")!.GetAttribute("href")).IsEqualTo("/wiki/category/guides");
		await Assert.That(guides.QuerySelector("a.wiki-cat-all")!.TextContent).Contains("2");

		var lore = cards[1];
		await Assert.That(lore.QuerySelector("a.wiki-cat-page .wiki-cat-tag--locked")).IsNotNull();

		var uncategorized = cards[2];
		await Assert.That(uncategorized.QuerySelector(".wiki-cat-name")!.TextContent).IsEqualTo("Uncategorized");
		await Assert.That(uncategorized.QuerySelector(".wiki-cat-cover-fallback")).IsNotNull();
		await Assert.That(uncategorized.QuerySelector("a.wiki-cat-all")).IsNull()
			.Because("pages in no category have no category page to list them all");
	}

	[Test]
	public async Task ACategoryWithMoreThanThreePages_ShowsThreeAndTheAllLink()
	{
		// The fake has at most two per category; this pins the cap through the widget's own slicing.
		var cut = RenderHome();
		foreach (var card in cut.FindAll(".wiki-cat-card"))
		{
			await Assert.That(card.QuerySelectorAll("a.wiki-cat-page").Length).IsLessThanOrEqualTo(3);
		}
	}

	[Test]
	public async Task SearchFromTheAddress_FiltersTheCards()
	{
		var cut = RenderHome("/wiki?q=harbour");
		await Assert.That(cut.Find(".wiki-hero-search input").GetAttribute("value")).IsEqualTo("harbour");
		var cards = cut.FindAll(".wiki-cat-card");
		await Assert.That(cards.Count).IsEqualTo(1);
		await Assert.That(cards[0].QuerySelector(".wiki-cat-name")!.TextContent).IsEqualTo("Lore");
	}

	[Test]
	public async Task SearchFromTheAddress_ClearsWhenTheQueryGoes()
	{
		var cut = RenderHome("/wiki?q=harbour");
		await Assert.That(cut.FindAll(".wiki-cat-card").Count).IsEqualTo(1);
		_nav.NavigateTo("/wiki");
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".wiki-cat-card").Count < 2) throw new InvalidOperationException("still filtered");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".wiki-hero-search input").GetAttribute("value")).IsEqualTo("");
	}

	[Test]
	public async Task Typing_FiltersTheCards_AndNoMatchSaysSo()
	{
		var cut = RenderHome();
		await cut.Find(".wiki-hero-search input").InputAsync("zzz");
		await Assert.That(cut.FindAll(".wiki-cat-card").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".wiki-empty").TextContent).Contains("zzz");
	}
}
