using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
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
			"/api/wiki/exists" => """{"character/general/tomas_reyes":true,"character/general/magister_oake":true,"main/theme/tidewater_chapel":false}""",
			"/api/wiki/category/theme" => "[" + string.Join(",",
				WikiApiFake.Page("1", "harbour_ward", "Harbour Ward", "theme", "/api/wiki-assets/h/ward.jpg", "Ilsa Varn"),
				WikiApiFake.Page("2", "setting_overview", "Setting Overview", "theme", "/api/wiki-assets/h/overview.jpg", "Wren"),
				WikiApiFake.Page("3", "tone_and_content", "Tone and Content", "theme", null, "Wren"),
				WikiApiFake.Page("4", "the_tides", "The Tides", "theme", "/api/wiki-assets/h/tides.jpg", "Dace")) + "]",
			_ => null,
		};
		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// The D1 wiki page (boards 23–24): a glass banner when the page has an image (kicker, title,
/// "Last edited by {mention} · when · anyone can edit", Back, History, Edit), the plain header
/// otherwise, the first image stripped from the body, character links as mentions, and an aside of
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
		Services.AddLocalization();
		_auth = AddAuthorization();
		_auth.SetAuthorized("reader");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private const string BodyHtml =
		"<p><img class=\"wiki-img\" src=\"/api/wiki-assets/h/ward.jpg\" alt=\"Harbour Ward\" loading=\"lazy\" /></p>" +
		"<p>Harbour Ward runs along the waterfront.</p>" +
		"<h2 id=\"geography\">Geography</h2><p>Reclaimed land.</p>" +
		"<h2 id=\"notable-residents\">Notable residents</h2>" +
		"<p><a href=\"/character/tomas_reyes\">Tomas Reyes</a> and <a href=\"/character/magister_oake\">Magister Oake</a> keep a pew at the " +
		"<a href=\"/wiki/main/theme/tidewater_chapel\">Tidewater Chapel</a>.</p>";

	private static WikiArticle Ward(string? image) =>
		new("Harbour Ward", "![Harbour Ward](/api/wiki-assets/h/ward.jpg)\n\nIntro\n\n## Geography\n\ntext\n\n## Notable residents\n\ntext", image, BodyHtml)
		{
			Id = "1",
			Slug = "harbour_ward",
			Category = "theme",
			Locale = "en",
			RequestedLocale = "en",
			AvailableLocales = ["en", "de"],
			LastEditedBy = "Ilsa Varn",
			UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-12),
		};

	private IRenderedComponent<WikiDisplay> RenderWard(string? image = "/api/wiki-assets/h/ward.jpg", bool embedded = false) =>
		Render<WikiDisplay>(p => p
			.Add(c => c.Slug, "harbour_ward")
			.Add(c => c.Namespace, "main")
			.Add(c => c.Category, "theme")
			.Add(c => c.Article, Ward(image))
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
		var secondary = cut.Find(".kit-banner-secondary");
		await Assert.That(secondary.TextContent).Contains("Last edited by");
		await Assert.That(secondary.QuerySelector("a.mention")!.TextContent).IsEqualTo("Ilsa Varn");
		await Assert.That(secondary.QuerySelector("a.mention")!.GetAttribute("href")).IsEqualTo("/character/Ilsa%20Varn");
		await Assert.That(secondary.TextContent).Contains("anyone can edit");
		await Assert.That(cut.Find(".kit-banner-back a.kit-capsule").GetAttribute("href")).IsEqualTo("/wiki");
		await Assert.That(cut.Find(".kit-banner-actions a.kit-capsule").GetAttribute("href")).IsEqualTo("/wiki/main/theme/harbour_ward/history");
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
		var cut = RenderWard(image: null);
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(cut.Find(".kit-page-kicker").TextContent).IsEqualTo("Theme");
		await Assert.That(cut.Find(".kit-page-desc a.mention").TextContent).IsEqualTo("Ilsa Varn");
		await Assert.That(cut.Find(".kit-page-actions a.kit-capsule[href$='/history']").GetAttribute("href")).IsEqualTo("/wiki/main/theme/harbour_ward/history");
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
		await Assert.That(cut.Find(".wiki-article-body a[href='/wiki/main/theme/tidewater_chapel']").ClassList).Contains("wiki-redlink");
	}

	[Test]
	public async Task TheAside_ListsTheTocTheMentionedCharactersAndMoreInTheCategory()
	{
		var cut = RenderWard();
		cut.WaitForAssertion(() => cut.Find(".wiki-page-aside .wiki-more a.kit-row"), TimeSpan.FromSeconds(5));
		var aside = cut.Find(".wiki-page-aside");
		var toc = aside.QuerySelectorAll(".wiki-toc a");
		await Assert.That(toc.Length).IsEqualTo(2);
		await Assert.That(toc[0].GetAttribute("href")).IsEqualTo("/wiki/main/theme/harbour_ward#geography");
		await Assert.That(aside.QuerySelector(".wiki-locales a[lang='de']")).IsNotNull();

		var mentioned = aside.QuerySelectorAll(".wiki-mentioned .kit-portrait");
		await Assert.That(mentioned.Length).IsEqualTo(2);
		await Assert.That(mentioned[0].GetAttribute("href")).IsEqualTo("/character/tomas_reyes");
		await Assert.That(mentioned[0].QuerySelector(".kit-portrait-label")!.TextContent).IsEqualTo("Tomas Reyes");
		await Assert.That(aside.QuerySelector(".wiki-mentioned .kit-card-title")!.TextContent).Contains("2");

		var more = aside.QuerySelectorAll(".wiki-more a.kit-row");
		await Assert.That(more.Length).IsEqualTo(3).Because("three siblings, never this page itself");
		await Assert.That(more.Select(a => a.GetAttribute("href")).ToList()).DoesNotContain("/wiki/main/theme/harbour_ward");
		await Assert.That(aside.QuerySelector(".wiki-more a.wiki-more-all")!.GetAttribute("href")).IsEqualTo("/wiki/category/theme");
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
