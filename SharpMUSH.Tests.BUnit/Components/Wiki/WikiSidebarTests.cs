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
	public async Task ListsCategoriesWithCountsAndCovers_GeneralLast()
	{
		var cut = RenderAt("/wiki");
		var rows = cut.FindAll(".wiki-side-cats a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(3);
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/wiki/category/guides");
		await Assert.That(rows[0].QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("2");
		await Assert.That(rows[0].QuerySelector("img.kit-row-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/intro.jpg");
		await Assert.That(rows[2].GetAttribute("href")).IsEqualTo("/wiki/category/general")
			.Because("uncategorised pages are 'General', listed after the named categories");
		await Assert.That(rows[2].QuerySelector(".kit-row-fallback svg")).IsNotNull().Because("no cover: the document icon, as on board 21");
	}

	[Test]
	public async Task HeaderCountsThePages_AndBrowseLinksHomeAndRecent()
	{
		var cut = RenderAt("/wiki");
		await Assert.That(cut.Find(".wiki-side-sub").TextContent).Contains("4");
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
		cut.Find(".wiki-side-search input").Input("harbour");
		cut.Find(".wiki-side-search").Submit();
		await Assert.That(_nav.Uri).EndsWith("/wiki?q=harbour");
	}

	[Test]
	public async Task NewPage_OnlyForCreators_TakesATitleAndOpensTheEditor()
	{
		var reader = RenderAt("/wiki");
		await Assert.That(reader.FindAll(".wiki-side-new").Count).IsEqualTo(0);

		_auth.SetPolicies("wiki.create");
		var creator = RenderAt("/wiki");
		creator.Find("button.wiki-side-new").Click();
		creator.Find(".wiki-side-new-form input").Input("Salt Market");
		creator.Find(".wiki-side-new-form").Submit();
		await Assert.That(_nav.Uri).EndsWith("/wiki/main/general/salt_market/edit");
	}

	[Test]
	public async Task Collapsed_KeepsCategoryCoversOnly()
	{
		_nav.NavigateTo("/wiki");
		var cut = Render<WikiSidebar>(p => p.Add(x => x.Collapsed, true));
		cut.WaitForAssertion(() => cut.Find(".wiki-side-cats .kit-row--collapsed"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".wiki-side-search").Count).IsEqualTo(0);
	}
}
