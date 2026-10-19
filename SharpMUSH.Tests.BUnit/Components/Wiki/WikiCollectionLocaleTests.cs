using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

public class WikiCollectionLocaleTests : TrackingBunitContext
{
	private readonly LocaleApi _api = new();

	public WikiCollectionLocaleTests()
	{
		WikiApiFake.Install(this);
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost/") });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
	}

	[Test]
	public async Task QueryLocaleReachesBothEndpointsThroughPagingCategorySearchAndArticleLinks()
	{
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki/collections/all?lang=fr&q=R%C3%A8gle");
		var cut = Render<WikiCollection>(parameters => parameters.Add(p => p.Collection, "all"));
		cut.WaitForAssertion(() => RequireContains("Règle 00", cut.Find(".wiki-collection-page").TextContent));
		await AssertCollectionArticleLink(cut.Find(".wiki-collection-page").GetAttribute("href"), "fr", "Règle");
		await Assert.That(cut.Find("h1 + a").GetAttribute("href")).IsEqualTo("/wiki?lang=fr");
		await cut.FindAll(".wiki-collection-paging button")[1].ClickAsync(new());
		cut.WaitForAssertion(() => RequireContains("Règle 50", cut.Find(".wiki-collection-page").TextContent));
		await cut.Find(".wiki-collection-featured button[aria-label$=': Guides français']").ClickAsync(new());
		await cut.Find(".wiki-collection-search input").InputAsync("Règle 60");
		await cut.Find(".wiki-collection-search").SubmitAsync();
		cut.WaitForAssertion(() => RequireContains("Règle 60", cut.Find(".wiki-collection-page").TextContent));
		await Assert.That(_api.Requests.All(uri => System.Web.HttpUtility.ParseQueryString(uri.Query)["lang"] == "fr")).IsTrue();
		await Assert.That(_api.Requests.Any(uri => uri.Query.Contains("skip=50", StringComparison.Ordinal))).IsTrue();
		await Assert.That(_api.Requests.Any(uri => uri.Query.Contains("category=guides", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task ChangingOnlyTheRequestedLocaleReloadsTheCollection()
	{
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki/collections/all?lang=fr");
		var cut = Render<WikiCollection>(parameters => parameters.Add(p => p.Collection, "all"));
		cut.WaitForAssertion(() => RequireContains("Règle 00", cut.Find(".wiki-collection-page").TextContent));
		nav.NavigateTo("/wiki/collections/all?lang=de");
		cut.WaitForAssertion(() => RequireContains("Regel 00", cut.Find(".wiki-collection-page").TextContent));
		await Assert.That(_api.Requests.Any(uri => uri.AbsolutePath == "/api/wiki/navigation" && uri.Query.Contains("lang=de", StringComparison.Ordinal))).IsTrue();
		await AssertCollectionArticleLink(cut.Find(".wiki-collection-page").GetAttribute("href"), "de", null);
	}

	[Test]
	public async Task SidebarForwardsLocaleAndKeepsItOnItsNavigationAndSearch()
	{
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki/collections/all?lang=fr");
		var cut = Render<WikiSidebar>();
		cut.WaitForAssertion(() => RequireContains("Guides français", cut.Find(".wiki-side-cats").TextContent));
		await Assert.That(cut.FindAll(".wiki-side-browse a, .wiki-side-collections a, .wiki-side-cats a")
			.All(link => System.Web.HttpUtility.ParseQueryString(new Uri(new Uri(nav.Uri), link.GetAttribute("href")).Query)["lang"] == "fr")).IsTrue();
		await cut.Find(".kit-side-search input").InputAsync("Règle");
		await cut.Find(".kit-side-search").SubmitAsync();
		await Assert.That(System.Web.HttpUtility.ParseQueryString(new Uri(nav.Uri).Query)["lang"]).IsEqualTo("fr");
		await Assert.That(_api.Requests.All(uri => System.Web.HttpUtility.ParseQueryString(uri.Query)["lang"] == "fr")).IsTrue();
	}

	[Test]
	public async Task HomeUsesLocalizedPreviewsAndPreservesLocaleAcrossSearchAndLinks()
	{
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki?lang=fr");
		var cut = Render<WikiIndexWidget>();
		cut.WaitForAssertion(() => RequireContains("Règle 00", cut.Find(".wiki-cat-page").TextContent));
		await Assert.That(cut.FindAll(".wiki-collection-entry, .wiki-browse-all, .wiki-cat-page, .wiki-cat-all")
			.All(link => System.Web.HttpUtility.ParseQueryString(new Uri(new Uri(nav.Uri), link.GetAttribute("href")).Query)["lang"] == "fr")).IsTrue();
		await cut.Find(".wiki-hero-search input").InputAsync("Règle 60");
		await cut.Find(".wiki-hero-search").SubmitAsync();
		cut.WaitForAssertion(() => RequireContains("Règle 60", cut.Find(".wiki-collection-page").TextContent));
		await Assert.That(_api.Requests.All(uri => System.Web.HttpUtility.ParseQueryString(uri.Query)["lang"] == "fr")).IsTrue();
	}

	[Test]
	public async Task HomeSearchNavigatesToTheAllCollectionBrowserWithLocale()
	{
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki?lang=fr");
		var cut = Render<WikiIndexWidget>();
		cut.WaitForAssertion(() => cut.Find(".wiki-hero-search"));
		await cut.Find(".wiki-hero-search input").InputAsync("  Règle & C++  ");
		await cut.Find(".wiki-hero-search").SubmitAsync();
		var location = new Uri(nav.Uri);
		var query = System.Web.HttpUtility.ParseQueryString(location.Query);
		await Assert.That(location.AbsolutePath).IsEqualTo("/wiki/collections/all");
		await Assert.That(query["q"]).IsEqualTo("Règle & C++");
		await Assert.That(query["lang"]).IsEqualTo("fr");
		await Assert.That(query["skip"]).IsNull();
	}

	[Test]
	public async Task CategoryUsesExplicitArticleLocaleForRequestsAndMemberLinks()
	{
		var cut = Render<WikiCategoryMembers>(parameters => parameters
			.Add(p => p.Category, "guides").Add(p => p.Locale, "fr"));
		cut.WaitForAssertion(() => RequireContains("Règle 00", cut.Markup));
		await Assert.That(cut.FindAll("a").All(link => link.GetAttribute("href")!.EndsWith("?lang=fr", StringComparison.Ordinal))).IsTrue();
		await Assert.That(_api.Requests.Count).IsEqualTo(2);
		await Assert.That(_api.Requests.All(uri => System.Web.HttpUtility.ParseQueryString(uri.Query)["lang"] == "fr")).IsTrue();
	}

	[Test]
	public async Task MalformedCollectionJsonIsAResponseFailureRatherThanATransportFailure()
	{
		_api.Malformed = true;
		var result = await Services.GetRequiredService<WikiService>().GetCollectionPagesAsync("all");
		await Assert.That(result is ApiFailure { Kind: ApiFailureKind.Unexpected }).IsTrue();
	}

	private static async Task AssertCollectionArticleLink(string? href, string locale, string? search)
	{
		var article = new Uri(new Uri("https://localhost/"), href ?? throw new InvalidOperationException("Article link missing."));
		var query = System.Web.HttpUtility.ParseQueryString(article.Query);
		await Assert.That(article.AbsolutePath).IsEqualTo("/wiki/main/rule_00");
		await Assert.That(query["lang"]).IsEqualTo(locale);
		await Assert.That(query["browseCollection"]).IsEqualTo("all");
		await Assert.That(query["browseQ"]).IsEqualTo(search);
		await Assert.That(query["browseCategory"]).IsNull();
		await Assert.That(query["browseSkip"]).IsNull();
		await Assert.That(query["membersCollection"]).IsEqualTo("all");
	}

	private static void RequireContains(string expected, string actual)
	{
		if (!actual.Contains(expected, StringComparison.Ordinal)) throw new InvalidOperationException($"Expected {expected} in {actual}.");
	}

	private sealed class LocaleApi : HttpMessageHandler
	{
		public List<Uri> Requests { get; } = [];
		public bool Malformed { get; set; }

		private static bool MatchesSearch(string json, string? search)
		{
			if (string.IsNullOrWhiteSpace(search)) return true;
			using var document = JsonDocument.Parse(json);
			return document.RootElement.GetProperty("title").GetString()!.Contains(search, StringComparison.OrdinalIgnoreCase);
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			Requests.Add(uri);
			var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
			var locale = query["lang"];
			if (uri.AbsolutePath == "/api/wiki/navigation")
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new WikiNavigationDto(query["collection"] ?? "game", 61,
						[new WikiCollectionSummaryDto("package:reference", "Reference", 61)],
						[new WikiCategorySummaryDto("guides", locale == "fr" ? "Guides français" : "Guides", 61, true, true)]))
				});
			if (!uri.AbsolutePath.Contains("/collections/", StringComparison.Ordinal))
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			var prefix = locale == "fr" ? "Règle" : locale == "de" ? "Regel" : "Rule";
			var rows = Enumerable.Range(0, 61).Select(i => WikiApiFake.Page("1", $"rule_{i:D2}", $"{prefix} {i:D2}", "guides"))
				.Where(json => MatchesSearch(json, query["search"])).ToArray();
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(Malformed ? "{" : WikiApiFake.Paged(request, rows))
			};
			response.Headers.Add("X-Total-Count", rows.Length.ToString());
			return Task.FromResult(response);
		}
	}
}
