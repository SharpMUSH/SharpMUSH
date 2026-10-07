using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Wiki;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// The D1 wiki page sidebar (board 21): "Wiki · namespace · N pages", search, Browse (home, recent
/// changes), Categories with covers and counts, and a New page button for those who may create.
/// </summary>
public class WikiSidebarTests : TrackingBunitContext
{
	private readonly BunitAuthorizationContext _auth;
	private readonly BunitNavigationManager _nav;

	public WikiSidebarTests()
	{
		(_, _auth) = WikiApiFake.Install(this);
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	private IRenderedComponent<WikiSidebar> RenderAt(string path)
	{
		_nav.NavigateTo(path);
		var cut = Render<WikiSidebar>();
		cut.WaitForAssertion(() => cut.Find(".wiki-side-cats a.kit-row"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task ListsNamedCategoriesWithCountsAndCovers()
	{
		var cut = RenderAt("/wiki");
		var rows = cut.FindAll(".wiki-side-cats .kit-row");
		await Assert.That(rows.Count).IsEqualTo(2)
			.Because("pages in no category have no category page, so the strip does not offer a row for them");
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/wiki/category/guides");
		await Assert.That(rows[0].QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("2");
		await Assert.That(rows[0].QuerySelector("img.kit-row-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/intro.jpg");
		await Assert.That(rows[1].GetAttribute("href")).IsEqualTo("/wiki/category/lore");
		await Assert.That(rows.Any(r => r.TextContent.Contains("Uncategorized"))).IsFalse();
	}

	[Test]
	public async Task HeaderCountsThePages_AndBrowseLinksHomeAndRecent()
	{
		var cut = RenderAt("/wiki");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).StartsWith("Main namespace").Because("board 21: \"Main namespace · N pages\"");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("4");
		await Assert.That(cut.Find(".wiki-side-browse a[href='/wiki']").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.Find(".wiki-side-browse a[href='/wiki/recent']")).IsNotNull();
	}

	[Test]
	public async Task TheCurrentCategoryRowIsCurrent()
	{
		var cut = RenderAt("/wiki/category/lore");
		await Assert.That(cut.Find(".wiki-side-cats a[href='/wiki/category/lore']").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.FindAll(".wiki-side-browse a[aria-current]").Count).IsEqualTo(0);
	}

	[Test]
	public async Task SearchSubmitsToTheWikiHome()
	{
		var cut = RenderAt("/wiki/category/lore");
		await cut.Find(".kit-side-search input").InputAsync("harbour");
		await cut.Find(".kit-side-search").SubmitAsync();
		await Assert.That(_nav.Uri).EndsWith("/wiki?q=harbour");
	}

	[Test]
	public async Task NewPage_OnlyForCreators_TakesATitleAndOpensTheEditor()
	{
		var reader = RenderAt("/wiki");
		await Assert.That(reader.FindAll(".wiki-side-new").Count).IsEqualTo(0);

		_auth.SetPolicies("wiki.create");
		var creator = RenderAt("/wiki");
		await creator.Find("button.wiki-side-new").ClickAsync();
		await creator.Find(".wiki-side-new-form input").InputAsync("Salt Market");
		await creator.Find(".wiki-side-new-form").SubmitAsync();
		await Assert.That(_nav.Uri).EndsWith("/wiki/main/salt_market/edit?title=Salt%20Market")
			.Because("the editor starts from the title the creator typed, not the slug made from it");

		await creator.Find("button.wiki-side-new").ClickAsync();
		await creator.Find(".wiki-side-new-form input").InputAsync("What? / Why#");
		await creator.Find(".wiki-side-new-form").SubmitAsync();
		await Assert.That(_nav.Uri).EndsWith("/wiki/main/what%3F_%2F_why%23/edit?title=What%3F%20%2F%20Why%23")
			.Because("a title with ?, / or # must not break the route");
	}

	[Test]
	public async Task Collapsed_NewPage_LinksToTheEditor()
	{
		_auth.SetPolicies("wiki.create");
		_nav.NavigateTo("/wiki");
		var cut = Render<WikiSidebar>(p => p.Add(x => x.Collapsed, true));
		cut.WaitForAssertion(() => cut.Find("a.kit-row--collapsed[href='/wiki/main/new-page/edit']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("button.kit-row--collapsed").Count(b => b.Closest(".wiki-side-cats") is null)).IsEqualTo(0)
			.Because("the strip has no room for the title form, so its New page is a link to the editor, not a toggle");
	}

	[Test]
	public async Task Collapsed_KeepsCategoryCoversOnly()
	{
		_nav.NavigateTo("/wiki");
		var cut = Render<WikiSidebar>(p => p.Add(x => x.Collapsed, true));
		cut.WaitForAssertion(() => cut.Find(".wiki-side-cats .kit-row--collapsed"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-side-search").Count).IsEqualTo(0);
	}
}
