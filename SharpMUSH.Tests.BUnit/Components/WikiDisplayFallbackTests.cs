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
/// The fallback notice is the whole user-visible payoff of "fallback, not 404": without it a French
/// reader silently gets English and never learns a translation is missing. These tests assert it renders
/// exactly when <see cref="WikiArticle.IsFallback"/> is set — including on the hero (home) branch, which
/// has its own body markup and is easy to forget.
/// </summary>
public class WikiDisplayFallbackTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public WikiDisplayFallbackTests()
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

	private static WikiArticle Article(bool isFallback, string locale = "en", string requested = "fr") =>
		new("Dragons", "body", null, "<p>body</p>")
		{
			Id = "1",
			Slug = "dragons",
			Category = "general",
			Locale = locale,
			RequestedLocale = requested,
			IsFallback = isFallback,
			AvailableLocales = ["en", "fr"],
		};

	private IRenderedComponent<WikiDisplay> RenderDisplay(WikiArticle article, string slug = "dragons") =>
		Render<WikiDisplay>(p => p
			.Add(c => c.Slug, slug)
			.Add(c => c.Namespace, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Locale, "fr")
			.Add(c => c.Article, article)
			.Add(c => c.ActivateEditMode, () => Task.CompletedTask));

	[Test]
	public async Task Notice_rendersWhenTheArticleIsAFallback()
	{
		var cut = RenderDisplay(Article(isFallback: true));

		await Assert.That(cut.Markup).Contains("WikiFallbackNotice");
	}

	[Test]
	public async Task Notice_isAbsentWhenTheRequestedLanguageWasServed()
	{
		var cut = RenderDisplay(Article(isFallback: false, locale: "fr", requested: "fr"));

		await Assert.That(cut.Markup).DoesNotContain("WikiFallbackNotice");
	}

	[Test]
	public async Task Notice_rendersOnTheHeroBranchToo()
	{
		// DisplayAsHero is slug == "home"; it has its own body markup, so it needs its own notice.
		var cut = RenderDisplay(Article(isFallback: true), slug: "home");

		await Assert.That(cut.Markup).Contains("WikiFallbackNotice");
	}

	[Test]
	public async Task Notice_canBeDismissedForTheSession()
	{
		var cut = RenderDisplay(Article(isFallback: true));

		cut.Find(".wiki-fallback-dismiss").Click();

		await Assert.That(cut.Markup).DoesNotContain("WikiFallbackNotice");
	}

	[Test]
	public async Task LanguageChips_listEveryAvailableLocaleExceptTheOneBeingServed()
	{
		var cut = RenderDisplay(Article(isFallback: true, locale: "en", requested: "fr"));

		var hrefs = cut.FindAll(".wiki-lang-chips a").Select(a => a.GetAttribute("href")).ToList();

		await Assert.That(hrefs).Contains("/wiki/main/general/dragons?lang=fr");
		await Assert.That(hrefs.Any(h => h!.EndsWith("lang=en")))
			.IsFalse()
			.Because("the locale already on screen is not a link to somewhere else");
	}

	[Test]
	public async Task LanguageChips_areAbsentWhenOnlyOneLocaleExists()
	{
		var article = Article(isFallback: false, locale: "en", requested: "en");
		article.AvailableLocales = ["en"];

		var cut = RenderDisplay(article);

		await Assert.That(cut.FindAll(".wiki-lang-chips")).IsEmpty();
	}

	[Test]
	public async Task TranslateLink_TargetsTheLocaleTheReaderAskedFor()
	{
		// The raw ?lang= wins over RequestedLocale. A reader who asked for a locale the server normalised
		// away (or resolved past) must still land in an editor for the language THEY asked for — otherwise
		// "Translate this page" offers to re-edit the English they are already looking at.
		Auth.SetAuthorized("editor");
		Auth.SetPolicies("wiki.edit");
		var article = Article(isFallback: true, locale: "en", requested: "en");

		var cut = Render<WikiDisplay>(p => p
			.Add(c => c.Slug, "dragons")
			.Add(c => c.Namespace, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Locale, "de")
			.Add(c => c.Article, article)
			.Add(c => c.ActivateEditMode, () => Task.CompletedTask));

		await Assert.That(cut.Markup).Contains("/wiki/main/general/dragons/edit?lang=de");
	}
}
