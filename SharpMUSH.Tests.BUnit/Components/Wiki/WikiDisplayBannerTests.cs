using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// Answers the two calls the page aside makes: the existence check (every link exists) and the
/// category listing (three sibling pages plus this one).
/// </summary>
file sealed class PageAsideHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		string? body = path switch
		{
			"/api/wiki/exists" => """{"character/tomas_reyes":true,"character/magister_oake":true,"main/tidewater_chapel":false}""",
			"/http/characters" => """[{"name":"Tomas Reyes","objid":"#312:1","created":1,"category":"","image":"/api/wiki-assets/t/tomas.jpg"},{"name":"Magister Oake","objid":"#316:1","created":1,"category":""}]""",
			"/api/wiki/category/theme" => WikiApiFake.Paged(request, [
				WikiApiFake.Page("1", "harbour_ward", "Harbour Ward", "theme", "/api/wiki-assets/h/ward.jpg", "Ilsa Varn"),
				WikiApiFake.Page("2", "setting_overview", "Setting Overview", "theme", "/api/wiki-assets/h/overview.jpg", "Wren"),
				WikiApiFake.Page("3", "tone_and_content", "Tone and Content", "theme", null, "Wren"),
				WikiApiFake.Page("4", "the_tides", "The Tides", "theme", "/api/wiki-assets/h/tides.jpg", "Dace"),
				WikiApiFake.Page("5", "harbour_ward", "Harbour Ward (help)", "theme", null, "Dace", ns: "help")]),
			_ => null,
		};
		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// The D1 wiki page (boards 23–24): a glass banner when the page opens with an image (kicker, title,
/// "Last edited by {mention} · when · anyone can edit", Back, History, Edit), the plain header
/// otherwise, that lead image stripped from the body, character links as mentions, the category bar, and an aside of
/// the table of contents, the characters mentioned and more pages in the category. Embedded
/// biographies get none of that chrome.
/// </summary>
public class WikiDisplayBannerTests : TrackingBunitContext
{
	private readonly BunitAuthorizationContext _auth;

	public WikiDisplayBannerTests()
	{
		var apiClient = Track(new HttpClient(new PageAsideHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services.AddMudServices();
		Services.AddSingleton(factory);
		Services.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));
		Services.AddSingleton<WikiMarkdigPipeline>();
		Services.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance));
		Services.AddLocalization();
		_auth = AddAuthorization();
		_auth.SetAuthorized("reader");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private const string BodyHtml =
		"<p><img class=\"wiki-img\" src=\"/api/wiki-assets/h/ward.jpg\" alt=\"A ward of slate roofs beside the harbour\" loading=\"lazy\" /></p>" +
		"<p>Harbour Ward runs along the waterfront.</p>" +
		"<h2 id=\"geography\">Geography</h2><p>Reclaimed land.</p>" +
		"<h2 id=\"notable-residents\">Notable residents</h2>" +
		"<h2 id=\"numbered\">1. <em>Numbered</em></h2><p>n</p>" +
		"<p><a href=\"/character/tomas_reyes\">Tomas Reyes</a> and <a href=\"/character/magister_oake\">Magister Oake</a> keep a pew at the " +
		"<a href=\"/wiki/main/tidewater_chapel\">Tidewater Chapel</a>.</p>";

	/// <summary>The same body without the image it opens with: nothing for a banner to take.</summary>
	private static readonly string BodyHtmlWithoutLeadImage =
		BodyHtml.Replace("<p><img class=\"wiki-img\" src=\"/api/wiki-assets/h/ward.jpg\" alt=\"A ward of slate roofs beside the harbour\" loading=\"lazy\" /></p>", string.Empty);

	private static WikiArticle Ward(string? image, string? html = null) =>
		new("Harbour Ward", "![Harbour Ward](/api/wiki-assets/h/ward.jpg)\n\nIntro\n\n## Geography\n\ntext\n\n## Notable residents\n\ntext", image, html ?? BodyHtml)
		{
			Id = "1",
			Slug = "harbour_ward",
			Categories = ["theme"],
			Locale = "en",
			RequestedLocale = "en",
			AvailableLocales = ["en", "de"],
			LastEditedBy = "Ilsa Varn",
			UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-12),
			// What the server says this reader may do; the Edit button follows it.
			Access = new SharpMUSH.Library.API.WikiAccessDto(Read: true, Edit: true, Delete: false, Manage: false),
		};

	private IRenderedComponent<WikiDisplay> RenderWard(string? image = "/api/wiki-assets/h/ward.jpg", bool embedded = false) =>
		RenderArticle(Ward(image), embedded);

	private IRenderedComponent<WikiDisplay> RenderArticle(WikiArticle article, bool embedded = false) =>
		Render<WikiDisplay>(p => p
			.Add(c => c.Slug, article.Slug)
			.Add(c => c.Namespace, "main")
			.Add(c => c.Article, article)
			.Add(c => c.Embedded, embedded)
			.Add(c => c.ActivateEditMode, () => Task.CompletedTask));

	[Test]
	public async Task WithAnImage_TheBannerCarriesKickerTitleEditorAndActions()
	{
		_auth.SetPolicies("wiki.edit");
		var cut = RenderWard();
		await Assert.That(cut.Find(".kit-banner h1.kit-banner-title").TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(cut.Find(".kit-banner-kicker").TextContent).IsEqualTo("Theme");
		await Assert.That(cut.Find(".kit-banner img.kit-banner-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/h/ward.jpg");
		await Assert.That(cut.Find(".kit-banner img.kit-banner-img").GetAttribute("alt")).IsEqualTo("A ward of slate roofs beside the harbour");
		var secondary = cut.Find(".kit-banner-secondary");
		await Assert.That(secondary.TextContent).Contains("Last edited by");
		await Assert.That(secondary.QuerySelector("a.mention")!.TextContent).IsEqualTo("Ilsa Varn");
		await Assert.That(secondary.QuerySelector("a.mention")!.GetAttribute("href")).IsEqualTo("/character/Ilsa%20Varn");
		await Assert.That(secondary.TextContent).Contains("anyone can edit");
		await Assert.That(cut.Find(".kit-banner-back a.kit-capsule").GetAttribute("href")).IsEqualTo("/wiki");
		await Assert.That(cut.Find(".kit-banner-actions a.kit-capsule").GetAttribute("href")).IsEqualTo("/wiki/main/harbour_ward/history");
		await Assert.That(cut.Find(".kit-banner-actions button.kit-capsule--primary").TextContent).Contains("Edit");
	}

	[Test]
	public async Task WithAnImage_TheBodyDoesNotRepeatIt()
	{
		var cut = RenderWard();
		await Assert.That(cut.FindAll(".wiki-article-body img").Count).IsEqualTo(0)
			.Because("the banner shows the first image; showing it twice is the README's explicit no");
		await Assert.That(cut.Find(".wiki-article-body").TextContent).Contains("Harbour Ward runs along the waterfront.");
	}

	[Test]
	public async Task WithoutAnImage_ThePlainHeaderCarriesTheSameFacts()
	{
		var cut = RenderArticle(Ward(image: null, html: BodyHtmlWithoutLeadImage));
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(cut.Find(".kit-page-kicker").TextContent).IsEqualTo("Theme");
		await Assert.That(cut.Find(".kit-page-desc a.mention").TextContent).IsEqualTo("Ilsa Varn");
		await Assert.That(cut.Find(".kit-page-actions a.kit-capsule[href$='/history']").GetAttribute("href")).IsEqualTo("/wiki/main/harbour_ward/history");
		await Assert.That(cut.Find(".kit-page-actions a.kit-capsule[href='/wiki']")).IsNotNull();
	}

	[Test]
	public async Task CharacterLinksInTheBody_BecomeMentions_AndWikiRedlinksStayRed()
	{
		var cut = RenderWard();
		cut.WaitForAssertion(() => cut.Find(".wiki-article-body a.mention"), TimeSpan.FromSeconds(5));
		var mentions = cut.FindAll(".wiki-article-body a.mention");
		await Assert.That(mentions.Count).IsEqualTo(2);
		await Assert.That(mentions[0].GetAttribute("href")).IsEqualTo("/character/tomas_reyes");
		await Assert.That(cut.Find(".wiki-article-body a[href='/wiki/main/tidewater_chapel']").ClassList).Contains("wiki-redlink");
	}

	[Test]
	public async Task TheAside_ListsTheTocTheMentionedCharactersAndMoreInTheCategory()
	{
		var cut = RenderWard();
		cut.WaitForAssertion(() => cut.Find(".wiki-page-aside .wiki-more a.kit-row"), TimeSpan.FromSeconds(5));
		var aside = cut.Find(".wiki-page-aside");
		var toc = aside.QuerySelectorAll(".wiki-toc a");
		await Assert.That(toc.Length).IsEqualTo(4).Because("board 23: an Overview entry for the intro leads the headings");
		await Assert.That(toc[0].GetAttribute("href")).IsEqualTo("/wiki/main/harbour_ward#wiki-top");
		await Assert.That(toc[0].GetAttribute("aria-current")).IsEqualTo("location");
		await Assert.That(cut.Find(".wiki-article-body").GetAttribute("id")).IsEqualTo("wiki-top");
		await Assert.That(toc[1].GetAttribute("href")).IsEqualTo("/wiki/main/harbour_ward#geography");
		await Assert.That(toc[1].GetAttribute("aria-current")).IsNull();
		await Assert.That(toc[3].GetAttribute("href")).IsEqualTo("/wiki/main/harbour_ward#numbered")
			.Because("anchors come from the rendered <h2 id>, not a second slugify of the Markdown");
		await Assert.That(toc[3].TextContent).IsEqualTo("1. Numbered");
		await Assert.That(aside.QuerySelector(".wiki-locales a[lang='de']")).IsNotNull();

		var mentioned = aside.QuerySelectorAll(".wiki-mentioned .kit-portrait");
		await Assert.That(mentioned.Length).IsEqualTo(2);
		await Assert.That(mentioned[0].GetAttribute("href")).IsEqualTo("/character/tomas_reyes");
		await Assert.That(mentioned[0].QuerySelector(".kit-portrait-label")!.TextContent).IsEqualTo("Tomas Reyes");
		cut.WaitForAssertion(() => cut.Find(".wiki-mentioned img.kit-portrait-img"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".wiki-mentioned img.kit-portrait-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg")
			.Because("the directory row carries the character's IMAGE (profile-handler 1.5)");
		await Assert.That(aside.QuerySelector(".wiki-mentioned .kit-card-title")!.TextContent).Contains("2");

		var more = aside.QuerySelectorAll(".wiki-more a.kit-row");
		await Assert.That(more.Length).IsEqualTo(3).Because("three siblings, never this page itself");
		await Assert.That(more.Select(a => a.GetAttribute("href")).ToList()).DoesNotContain("/wiki/main/harbour_ward");
		await Assert.That(more.Select(a => a.GetAttribute("href")!).Any(h => h.StartsWith("/wiki/help/"))).IsFalse()
			.Because("a same-named page in another namespace is not a sibling");
		await Assert.That(aside.QuerySelector(".wiki-more a.wiki-more-all")!.TextContent).Contains("4");
		await Assert.That(aside.QuerySelector(".wiki-more .kit-card-head a.wiki-more-all")!.GetAttribute("href")).IsEqualTo("/wiki/category/theme")
			.Because("board 23 puts 'All N' in the card header, beside the title");
		await Assert.That(more.Select(a => a.QuerySelector(".kit-row-fallback svg")).Count(x => x is not null)).IsEqualTo(1)
			.Because("Tone and Content has no image, so its row leads with the document icon");
	}

	[Test]
	public async Task TocLinks_KeepTheQueryString()
	{
		// A translated page is /path?lang=de; a TOC link that drops the query re-keys WikiView and
		// reloads the source locale.
		Services.GetRequiredService<NavigationManager>().NavigateTo("/wiki/main/harbour_ward?lang=de");
		var cut = RenderWard();
		await Assert.That(cut.FindAll(".wiki-toc a")[1].GetAttribute("href")).IsEqualTo("/wiki/main/harbour_ward?lang=de#geography");
	}

	[Test]
	public async Task TheBodyCard_DoesNotOpenWithTheParagraphTheBannerEmptied()
	{
		var cut = RenderWard();
		var first = cut.Find(".wiki-article-body .mud-card-content").FirstElementChild!;
		await Assert.That(first.TextContent).Contains("Harbour Ward runs along the waterfront.");
	}

	[Test]
	public async Task AnImageOnlyPage_HasABannerAndNoBodyCard()
	{
		var article = new WikiArticle("Just a picture", "![x](/api/wiki-assets/h/only.jpg)", "/api/wiki-assets/h/only.jpg",
			"<p><img class=\"wiki-img\" src=\"/api/wiki-assets/h/only.jpg\" alt=\"x\" /></p>")
		{
			Id = "9", Slug = "just_a_picture", Categories = ["theme"], Locale = "en", RequestedLocale = "en", AvailableLocales = ["en"],
		};
		var cut = RenderArticle(article);
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".wiki-article-card").Count).IsEqualTo(0).Because("nothing is left to show under the banner");
	}

	[Test]
	public async Task ADirectiveOnlyPage_KeepsItsBodyCard()
	{
		// "::: recent 10" alone renders to nothing but the placeholder the body hydrates; stripping
		// tags leaves no text, yet the listing it stands for is the page's whole content.
		var article = new WikiArticle("Latest changes", "::: recent 10", null,
			"<div class=\"wiki-directive\" data-directive=\"recent\" data-arg=\"10\"></div>")
		{
			Id = "10", Slug = "latest_changes", Categories = ["theme"], Locale = "en", RequestedLocale = "en", AvailableLocales = ["en"],
		};
		var cut = RenderArticle(article);
		await Assert.That(cut.FindAll(".wiki-article-card").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".wiki-article-card .wiki-directive-block").Count).IsEqualTo(1);
	}

	[Test]
	public async Task AnImageLaterInTheBody_IsNotABanner()
	{
		// The banner is the page's lead image only: a page that opens with text keeps the plain header,
		// and a picture further down stays where its author put it.
		const string html =
			"<p>Harbour Ward runs along the waterfront.</p>" +
			"<p><img class=\"wiki-img\" src=\"/api/wiki-assets/h/ward.jpg\" alt=\"Harbour Ward\" loading=\"lazy\" /></p>" +
			"<p>More text.</p>";
		var cut = RenderArticle(Ward("/api/wiki-assets/h/ward.jpg", html));
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Harbour Ward");
		var images = cut.FindAll(".wiki-article-body img");
		await Assert.That(images.Count).IsEqualTo(1).Because("an image that does not open the page is not taken out of the body");
		await Assert.That(images[0].GetAttribute("src")).IsEqualTo("/api/wiki-assets/h/ward.jpg");
	}

	[Test]
	public async Task TheCategoryBar_LinksEachCategoryToItsPage()
	{
		var article = new WikiArticle("Harbour Ward", "Text.", null,
			"<p>Text.</p>")
		{
			Id = "1", Slug = "harbour_ward", Categories = ["harbour_districts", "theme"], Locale = "en", RequestedLocale = "en", AvailableLocales = ["en"],
		};
		var cut = RenderArticle(article);
		var links = cut.FindAll("nav.wiki-catlinks a.wiki-catlink");
		await Assert.That(links.Count).IsEqualTo(2);
		await Assert.That(links[0].GetAttribute("href")).IsEqualTo("/wiki/category/harbour_districts");
		await Assert.That(links[0].TextContent).IsEqualTo("Harbour districts");
		await Assert.That(links[1].GetAttribute("href")).IsEqualTo("/wiki/category/theme");
		await Assert.That(links[1].TextContent).IsEqualTo("Theme");
	}

	[Test]
	public async Task APageInNoCategory_HasNoCategoryBar()
	{
		var article = new WikiArticle("Loose", "Text.", null, "<p>Text.</p>")
		{
			Id = "2", Slug = "loose", Locale = "en", RequestedLocale = "en", AvailableLocales = ["en"],
		};
		var cut = RenderArticle(article);
		await Assert.That(cut.FindAll("nav.wiki-catlinks").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ACategoryLinkInTheText_IsALinkNotMembership()
	{
		// Rendered through the real pipeline, so the body is what the server would store.
		var markdown = "Before the link [[Category:Theme]] and after it.";
		var html = Services.GetRequiredService<WikiMarkdigPipeline>().RenderToHtml(markdown);
		var article = new WikiArticle("Harbour Ward", markdown, null, html)
		{
			Id = "1", Slug = "harbour_ward", Locale = "en", RequestedLocale = "en", AvailableLocales = ["en"],
		};
		var cut = RenderArticle(article);
		var body = cut.Find(".wiki-article-body");
		await Assert.That(body.QuerySelector("a")!.GetAttribute("href")).IsEqualTo("/wiki/category/theme");
		await Assert.That(cut.FindAll("nav.wiki-catlinks").Count).IsEqualTo(0)
			.Because("the page's categories are its own list, which this page leaves empty");
	}

	[Test]
	public async Task Embedded_HasNoBannerAsideOrBack()
	{
		var cut = RenderWard(embedded: true);
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".wiki-page-aside").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-capsule").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".wiki-article-body").Count).IsEqualTo(1);
	}
}
