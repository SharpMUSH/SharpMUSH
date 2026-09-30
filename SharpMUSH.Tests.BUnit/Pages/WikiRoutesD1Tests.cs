using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Pages;
using SharpMUSH.Tests.BUnit.Components.Wiki;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The D1 wiki routes added beside the sidebar: the layout that hosts it, <c>/wiki/recent</c> and
/// <c>/wiki/category/{category}</c>.
/// </summary>
public class WikiRoutesD1Tests : TrackingBunitContext
{
	private readonly WikiApiFake _fake;
	private readonly BunitNavigationManager _nav;

	public WikiRoutesD1Tests()
	{
		(_fake, _) = WikiApiFake.Install(this);
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	[Test]
	public async Task WikiLayout_HostsTheSidebarBesideTheBody()
	{
		_nav.NavigateTo("/wiki");
		var cut = Render<WikiLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>")));
		cut.WaitForAssertion(() => cut.Find(".wiki-shell .wiki-side-cats a.kit-row"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".wiki-shell .kit-section-body #body")).IsNotNull();
	}

	[Test]
	public async Task Recent_ListsChangesNewestFirst_WithEditorAndThumbnail()
	{
		_nav.NavigateTo("/wiki/recent");
		var cut = Render<WikiRecent>();
		cut.WaitForAssertion(() => cut.Find(".wiki-recent-row"), TimeSpan.FromSeconds(5));
		var rows = cut.FindAll(".wiki-recent-row");
		await Assert.That(rows.Count).IsEqualTo(4);
		await Assert.That(rows[1].QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(rows[1].QuerySelector(".kit-row-sub")!.TextContent).Contains("Wren");
		await Assert.That(rows[1].QuerySelector("img.kit-row-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/b/harbour.jpg");
		await Assert.That(rows[1].GetAttribute("href")).IsEqualTo("/wiki/main/lore/harbour");
		await Assert.That(cut.Find(".kit-page-head h1")).IsNotNull();
	}

	[Test]
	public async Task Recent_WhenTheServerRefuses_SaysSo()
	{
		_fake.Refuse = true;
		_nav.NavigateTo("/wiki/recent");
		var cut = Render<WikiRecent>();
		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".wiki-recent-row").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Category_ShowsItsPagesAsTiles_WithDraftAndLockedTags()
	{
		_nav.NavigateTo("/wiki/category/guides");
		var cut = Render<WikiCategory>(p => p.Add(x => x.Category, "guides"));
		cut.WaitForAssertion(() => cut.Find(".kit-tile"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Guides");
		var tiles = cut.FindAll(".kit-tile");
		await Assert.That(tiles.Count).IsEqualTo(2);
		// newest first: the draft "Combat Basics" (updated 01-02) leads "Getting Started" (01-01)
		await Assert.That(tiles[0].GetAttribute("href")).IsEqualTo("/wiki/main/guides/combat");
		await Assert.That(tiles[0].QuerySelector(".wiki-cat-tag--draft")).IsNotNull();
		await Assert.That(tiles[0].QuerySelector(".wiki-cat-tag--draft")!.ClassList).Contains("kit-pill--warn")
			.Because("the tag styles come from the kit pill; the widget's scoped .wiki-cat-tag rules do not reach this page");
		await Assert.That(tiles[1].GetAttribute("href")).IsEqualTo("/wiki/main/guides/intro");
		await Assert.That(tiles[1].QuerySelector("img.kit-tile-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/intro.jpg");
		await Assert.That(cut.Find(".kit-page-desc").TextContent).Contains("2");
	}

	[Test]
	public async Task Category_ListsEveryPage_NotJustTheFirstRequest()
	{
		_nav.NavigateTo("/wiki/category/big");
		var cut = Render<WikiCategory>(p => p.Add(x => x.Category, "big"));
		cut.WaitForAssertion(() => cut.Find(".kit-tile"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-tile").Count).IsEqualTo(WikiApiFake.BigCategory.Length);
		await Assert.That(cut.Find(".kit-page-desc").TextContent).Contains("250");
	}

	[Test]
	public async Task Category_WithNoPages_ShowsTheEmptyState()
	{
		_nav.NavigateTo("/wiki/category/empty");
		var cut = Render<WikiCategory>(p => p.Add(x => x.Category, "empty"));
		cut.WaitForAssertion(() => cut.Find(".wiki-cat-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-tile").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Empty");
	}
}
