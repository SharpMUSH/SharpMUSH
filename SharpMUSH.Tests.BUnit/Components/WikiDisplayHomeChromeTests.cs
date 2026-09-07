using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Answers every wiki API call with a 404 — these tests supply the article as a parameter.</summary>
file sealed class NotFoundHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>
/// The home page is displayed as a centred hero — no back link, no title bar, no edit or history
/// controls. That framing belongs to the widget that embeds it on the front page; on the
/// <c>/wiki/...</c> route the home page is an article like any other and must carry the same chrome,
/// or it is the one page in the wiki nobody can edit from the wiki.
/// </summary>
public class WikiDisplayHomeChromeTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public WikiDisplayHomeChromeTests()
	{
		var apiClient = Track(new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services.AddMudServices();
		Services.AddSingleton(apiClient);
		Services.AddSingleton(factory);
		Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));
		Services.AddSingleton<WikiMarkdigPipeline>();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		Auth = AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static WikiArticle Home() =>
		new("Home", "## Welcome\n\nbody", null, "<h2 id=\"welcome\">Welcome</h2><p>body</p>")
		{
			Id = "1",
			Slug = "home",
			Category = "general",
			Locale = "en",
			RequestedLocale = "en",
			AvailableLocales = ["en"],
		};

	private IRenderedComponent<WikiDisplay> RenderHome(bool embedded) =>
		Render<WikiDisplay>(p => p
			.Add(c => c.Slug, "home")
			.Add(c => c.Namespace, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Article, Home())
			.Add(c => c.Embedded, embedded)
			.Add(c => c.ActivateEditMode, () => Task.CompletedTask));

	[Test]
	public async Task OnTheWikiRoute_theHomePageOffersHistory()
	{
		var cut = RenderHome(embedded: false);

		var hrefs = cut.FindAll("a.wiki-btn").Select(a => a.GetAttribute("href")).ToList();

		await Assert.That(hrefs).Contains("/wiki/main/general/home/history");
	}

	[Test]
	public async Task OnTheWikiRoute_theHomePageOffersEditToAnEditor()
	{
		Auth.SetAuthorized("editor");
		Auth.SetPolicies("wiki.edit");

		var cut = RenderHome(embedded: false);

		await Assert.That(cut.FindAll("button.wiki-btn--solid")).IsNotEmpty();
	}

	[Test]
	public async Task OnTheWikiRoute_theHomePageCarriesTheArticleFrame()
	{
		var cut = RenderHome(embedded: false);

		await Assert.That(cut.FindAll(".wiki-article-title")).IsNotEmpty();
		await Assert.That(cut.FindAll(".WikiContent--hero")).IsEmpty();
	}

	[Test]
	public async Task Embedded_theHomePageStaysAHeroWithNoChrome()
	{
		Auth.SetAuthorized("editor");
		Auth.SetPolicies("wiki.edit");

		var cut = RenderHome(embedded: true);

		await Assert.That(cut.FindAll(".WikiContent--hero")).IsNotEmpty();
		await Assert.That(cut.FindAll(".wiki-btn")).IsEmpty();
		await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
	}

	[Test]
	public async Task Embedded_anOrdinaryPageDoesNotOfferAWayBackToTheWiki()
	{
		// The widget puts this page on somebody else's page; there is no wiki the reader came from.
		var article = Home();
		article.Slug = "dragons";

		var cut = Render<WikiDisplay>(p => p
			.Add(c => c.Slug, "dragons")
			.Add(c => c.Namespace, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Article, article)
			.Add(c => c.Embedded, true)
			.Add(c => c.ActivateEditMode, () => Task.CompletedTask));

		await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
	}
}
