using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Pages;
using SharpMUSH.Tests.BUnit.Components.Wiki;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The D1 wiki routes added beside the sidebar: the layout that hosts it, <c>/wiki/recent</c> and
/// a category page (<c>/wiki/category/{key}</c>, shown by <see cref="WikiDisplay"/>).
/// </summary>
public class WikiRoutesD1Tests : TrackingBunitContext
{
	private readonly WikiApiFake _fake;
	private readonly BunitNavigationManager _nav;

	public WikiRoutesD1Tests()
	{
		(_fake, _) = WikiApiFake.Install(this);
		// WikiDisplay, which shows a category page, also reads the character directory and the Markdown pipeline.
		Services.AddSingleton<SharpMUSH.Library.Services.WikiMarkdigPipeline>();
		Services.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance));
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	[Test]
	public async Task WikiLayout_HostsTheSidebarBesideTheBody()
	{
		_nav.NavigateTo("/wiki");
		var cut = Render<PageSidebarHost>(h => h.AddChildContent<WikiLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>"))));
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .wiki-shell .wiki-side-cats a.kit-row"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-section-body.wiki-shell #body")).IsNotNull();
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
		await Assert.That(rows[1].GetAttribute("href")).IsEqualTo("/wiki/main/harbour");
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

	/// <summary>
	/// A category page is an ordinary page in the <c>category</c> namespace, shown by <see cref="WikiDisplay"/>;
	/// a category nobody has described yet has no article and still lists its members.
	/// </summary>
	private IRenderedComponent<WikiDisplay> RenderCategory(string category, WikiArticle? article = null)
	{
		_nav.NavigateTo($"/wiki/category/{category}");
		return Render<WikiDisplay>(p => p
			.Add(x => x.Slug, category)
			.Add(x => x.Namespace, "category")
			.Add(x => x.Article, article)
			.Add(x => x.ActivateEditMode, () => Task.CompletedTask));
	}

	[Test]
	public async Task Category_ShowsItsPagesAsTiles_WithDraftAndLockedTags()
	{
		var cut = RenderCategory("guides");
		cut.WaitForAssertion(() => cut.Find(".kit-tile"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Guides");
		await Assert.That(cut.Find(".kit-page-kicker").TextContent).IsEqualTo("Category");
		var tiles = cut.FindAll(".kit-tile");
		await Assert.That(tiles.Count).IsEqualTo(2);
		// newest first: the draft "Combat Basics" (updated 01-02) leads "Getting Started" (01-01)
		await Assert.That(tiles[0].GetAttribute("href")).IsEqualTo("/wiki/main/combat");
		await Assert.That(tiles[0].QuerySelector(".wiki-cat-tag--draft")).IsNotNull();
		await Assert.That(tiles[0].QuerySelector(".wiki-cat-tag--draft")!.ClassList).Contains("kit-pill--warn")
			.Because("the tag styles come from the kit pill; the widget's scoped .wiki-cat-tag rules do not reach this page");
		await Assert.That(tiles[1].GetAttribute("href")).IsEqualTo("/wiki/main/intro");
		await Assert.That(tiles[1].QuerySelector("img.kit-tile-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/intro.jpg");
		await Assert.That(cut.Find(".wiki-cat").TextContent).Contains("2 pages in this category");
	}

	[Test]
	public async Task Category_ListsEveryPage_NotJustTheFirstRequest()
	{
		var cut = RenderCategory("big");
		cut.WaitForAssertion(() => cut.Find(".kit-tile"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-tile").Count).IsEqualTo(WikiApiFake.BigCategory.Length);
		await Assert.That(cut.Find(".wiki-cat").TextContent).Contains("250 pages in this category");
	}

	[Test]
	public async Task Category_WithNoPages_ShowsTheEmptyState()
	{
		var cut = RenderCategory("empty");
		cut.WaitForAssertion(() => cut.Find(".wiki-cat-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-tile").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Empty");
	}

	[Test]
	public async Task Category_ListsCategoryPagesAsSubcategories_NotAsTiles()
	{
		var cut = RenderCategory("realms");
		cut.WaitForAssertion(() => cut.Find(".wiki-cat-subs a"), TimeSpan.FromSeconds(5));
		var subs = cut.FindAll(".wiki-cat-subs a.wiki-catlink");
		await Assert.That(subs.Count).IsEqualTo(1);
		await Assert.That(subs[0].GetAttribute("href")).IsEqualTo("/wiki/category/harbour_lore");
		await Assert.That(subs[0].TextContent).IsEqualTo("Harbour lore");
		var tiles = cut.FindAll(".kit-tile");
		await Assert.That(tiles.Count).IsEqualTo(1).Because("a subcategory is listed as a link, not as a page tile");
		await Assert.That(tiles[0].GetAttribute("href")).IsEqualTo("/wiki/main/harbour");
	}

	[Test]
	public async Task Category_WithADescription_ShowsItAboveItsMembers()
	{
		var article = new WikiArticle("Guides", "How-to pages.", null, "<p>How-to pages.</p>")
		{
			Id = "c1", Slug = "guides", Locale = "en", RequestedLocale = "en", AvailableLocales = ["en"],
		};
		var cut = RenderCategory("guides", article);
		cut.WaitForAssertion(() => cut.Find(".kit-tile"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".wiki-article-body").TextContent).Contains("How-to pages.");
		await Assert.That(cut.FindAll(".kit-tile").Count).IsEqualTo(2);
	}
}
