using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Models.Wiki;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>Exercises the route contract before the browse-state helper is introduced.</summary>
public class WikiBrowseStateTests : TrackingBunitContext
{
	private readonly WikiApiFake _api;
	private readonly NavigationManager _nav;

	public WikiBrowseStateTests()
	{
		(_api, _) = WikiApiFake.Install(this);
		_api.CollectionRows = Enumerable.Range(0, 611)
			.Select(i => WikiApiFake.Page("1", $"rule_{i:D4}", $"Rule {i:D4}", "guides")).ToArray();
		_nav = Services.GetRequiredService<NavigationManager>();
	}

	private IRenderedComponent<WikiCollection> Browse(string query)
	{
		_nav.NavigateTo("/wiki/collections/package%3Areference" + query);
		var cut = Render<WikiCollection>(parameters => parameters.Add(p => p.Collection, "package:reference"));
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-range"));
		return cut;
	}

	private Uri[] RowRequests => _api.Requests.Select(path => new Uri(new Uri(_nav.BaseUri), path))
		.Where(uri => uri.AbsolutePath.Contains("/collections/", StringComparison.Ordinal)).ToArray();

	private static string? Query(Uri uri, string key) => System.Web.HttpUtility.ParseQueryString(uri.Query)[key];

	[Test]
	public async Task DeepLinkRequestsTheRequestedPackagePageWithEveryFilter()
	{
		var cut = Browse("?q=Rule&category=guides&skip=500&lang=fr");
		var first = RowRequests[0];
		await Assert.That(Uri.UnescapeDataString(first.AbsolutePath)).IsEqualTo("/api/wiki/collections/package:reference/pages");
		await Assert.That(Query(first, "skip")).IsEqualTo("500");
		await Assert.That(Query(first, "take")).IsEqualTo("50");
		await Assert.That(Query(first, "search")).IsEqualTo("Rule");
		await Assert.That(Query(first, "category")).IsEqualTo("guides");
		await Assert.That(Query(first, "lang")).IsEqualTo("fr");
		await Assert.That(cut.Find(".wiki-collection-page").TextContent).Contains("Rule 0500");
	}

	[Test]
	public async Task NextWritesTheFullStateIntoTheRoute()
	{
		var cut = Browse("?q=Rule&category=guides&skip=500&lang=fr");
		await cut.FindAll(".wiki-collection-paging button")[1].ClickAsync(new());
		var location = new Uri(_nav.Uri);
		await Assert.That(Query(location, "skip")).IsEqualTo("550");
		await Assert.That(Query(location, "q")).IsEqualTo("Rule");
		await Assert.That(Query(location, "category")).IsEqualTo("guides");
		await Assert.That(Query(location, "lang")).IsEqualTo("fr");
		await Assert.That(Uri.UnescapeDataString(location.AbsolutePath)).IsEqualTo("/wiki/collections/package:reference");
	}

	[Test]
	public async Task ReloadRestoresTheSameResultRange()
	{
		using (var first = Browse("?q=Rule&category=guides&skip=500&lang=fr"))
		{
			await Assert.That(first.FindAll(".wiki-collection-page").Count).IsEqualTo(50);
		}
		var reloaded = Render<WikiCollection>(parameters => parameters.Add(p => p.Collection, "package:reference"));
		reloaded.WaitForAssertion(() => reloaded.Find(".wiki-collection-page"));
		await Assert.That(reloaded.Find(".wiki-collection-page").TextContent).Contains("Rule 0500");
	}

	[Test]
	public async Task SearchSubmissionResetsOnlyTheOffset()
	{
		var cut = Browse("?q=Rule&category=guides&skip=500&lang=fr");
		await cut.Find(".wiki-collection-search input").InputAsync("  Rule 0610  ");
		await cut.Find(".wiki-collection-search").SubmitAsync();
		var location = new Uri(_nav.Uri);
		await Assert.That(Query(location, "q")).IsEqualTo("Rule 0610");
		await Assert.That(Query(location, "skip")).IsNull();
		await Assert.That(Query(location, "category")).IsEqualTo("guides");
		await Assert.That(Query(location, "lang")).IsEqualTo("fr");
	}

	[Test]
	public async Task CategorySelectionDoesNotSubmitAnUnsubmittedSearchDraft()
	{
		var cut = Browse("?q=Rule&skip=500&lang=fr");
		await cut.Find(".wiki-collection-search input").InputAsync("unsubmitted draft");
		await cut.Find(".wiki-collection-featured button[aria-label$=': Guides']").ClickAsync(new());
		await Assert.That(Query(RowRequests[^1], "search")).IsEqualTo("Rule");
		var location = new Uri(_nav.Uri);
		await Assert.That(Query(location, "category")).IsEqualTo("guides");
		await Assert.That(Query(location, "skip")).IsNull();
		await Assert.That(Query(location, "lang")).IsEqualTo("fr");
	}

	[Test]
	public async Task ChangingLocaleRetainsTheRequestedPage()
	{
		var cut = Browse("?q=Rule&category=guides&skip=500&lang=fr");
		_nav.NavigateTo("/wiki/collections/package%3Areference?q=Rule&category=guides&skip=500&lang=de");
		cut.WaitForAssertion(() =>
		{
			if (Query(RowRequests[^1], "lang") != "de") throw new InvalidOperationException("Locale was not reloaded.");
		});
		await Assert.That(Query(RowRequests[^1], "skip")).IsEqualTo("500");
		await Assert.That(cut.Find(".wiki-collection-page").TextContent).Contains("Rule 0500");
	}

	[Test]
	[Arguments("-1", null)]
	[Arguments("not-a-number", null)]
	[Arguments("2147483648", null)]
	[Arguments("", null)]
	[Arguments("599", "550")]
	public async Task RawOffsetsAreNormalizedBeforeRequestsAndCanonicalized(string offset, string? expected)
	{
		Browse("?skip=" + offset + "&lang=fr");
		await Assert.That(Query(RowRequests[0], "skip")).IsEqualTo(expected ?? "0");
		await Assert.That(Query(new Uri(_nav.Uri), "skip")).IsEqualTo(expected);
		await Assert.That(Query(new Uri(_nav.Uri), "lang")).IsEqualTo("fr");
	}

	[Test]
	public async Task OutOfRangeOffsetRequestsOneCorrectionAndWritesTheFinalPage()
	{
		var cut = Browse("?q=Rule&skip=900&lang=fr");
		await Assert.That(RowRequests.Length).IsEqualTo(2);
		await Assert.That(Query(RowRequests[0], "skip")).IsEqualTo("900");
		await Assert.That(Query(RowRequests[1], "skip")).IsEqualTo("600");
		await Assert.That(Query(new Uri(_nav.Uri), "skip")).IsEqualTo("600");
		await Assert.That(cut.Find(".wiki-collection-page").TextContent).Contains("Rule 0600");
	}

	[Test]
	public async Task OversizedRouteSearchNeverReachesTheApiUnbounded()
	{
		var bounded = "Rule " + new string('x', 195);
		Browse("?q=" + Uri.EscapeDataString("  " + bounded + "overflow  ") + "&lang=fr");
		await Assert.That(Query(RowRequests[0], "search")).IsEqualTo(bounded);
		await Assert.That(Query(new Uri(_nav.Uri), "q")).IsEqualTo(bounded);
	}

	[Test]
	public async Task ResultArticleLinksCarryStructuredReturnAndIndependentMemberScope()
	{
		var cut = Browse("?q=Rule&category=guides&skip=500&lang=fr");
		var article = new Uri(new Uri(_nav.Uri), cut.Find(".wiki-collection-page").GetAttribute("href"));
		await Assert.That(Query(article, "browseCollection")).IsEqualTo("package:reference");
		await Assert.That(Query(article, "browseQ")).IsEqualTo("Rule");
		await Assert.That(Query(article, "browseCategory")).IsEqualTo("guides");
		await Assert.That(Query(article, "browseSkip")).IsEqualTo("500");
		await Assert.That(Query(article, "membersCollection")).IsEqualTo("package:reference");
		await Assert.That(Query(article, "lang")).IsEqualTo("fr");
		await Assert.That(Query(article, "returnUrl")).IsNull();
	}

	[Test]
	public async Task LegacyHomeSearchUsesTheLastRepeatedValue()
	{
		_nav.NavigateTo("/wiki?q=missing&q=Rule%200610&lang=fr");
		var cut = Render<WikiIndexWidget>();
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-range"));
		await Assert.That(Query(RowRequests[^1], "search")).IsEqualTo("Rule 0610");
		await Assert.That(cut.FindAll(".wiki-collection-page").Count).IsEqualTo(1);
	}

	[Test]
	public async Task OlderOutOfRangeResponseCannotReplaceANewerRoute()
	{
		var handler = new DelayedRowsApi();
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri(_nav.BaseUri) });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
		_nav.NavigateTo("/wiki/collections/package%3Areference?q=Old&skip=900&lang=fr");
		var cut = Render<WikiCollection>(parameters => parameters.Add(p => p.Collection, "package:reference"));
		cut.WaitForAssertion(() =>
		{
			if (!handler.OldRequested) throw new InvalidOperationException("Old request was not started.");
		});
		_nav.NavigateTo("/wiki/collections/package%3Areference?q=New&skip=50&lang=de");
		cut.WaitForAssertion(() =>
		{
			if (!cut.Find(".wiki-collection-page").TextContent.Contains("New result", StringComparison.Ordinal))
				throw new InvalidOperationException("Newer result was not rendered.");
		});
		var newerRoute = _nav.Uri;
		await cut.InvokeAsync(handler.ReleaseOld);
		await Assert.That(_nav.Uri).IsEqualTo(newerRoute);
		await Assert.That(cut.Find(".wiki-collection-page").TextContent).Contains("New result");
		await Assert.That(handler.OldRequests).IsEqualTo(1)
			.Because("the stale out-of-range response must not trigger a corrective request for the obsolete state");
	}

	[Test]
	public async Task CollectionLinkRoundTripsUnicodeAndQueryPunctuation()
	{
		var state = new WikiBrowseState("package:référence", "  Étoile & C++ \"quoted\"  ", "sages & mages", 599, "fr");
		var path = WikiJourneyLinks.Collection(state);
		var uri = new Uri(new Uri(_nav.BaseUri), path);
		await Assert.That(Uri.UnescapeDataString(uri.AbsolutePath)).IsEqualTo("/wiki/collections/package:référence");
		await Assert.That(Query(uri, "q")).IsEqualTo("Étoile & C++ \"quoted\"");
		await Assert.That(Query(uri, "category")).IsEqualTo("sages & mages");
		await Assert.That(Query(uri, "skip")).IsEqualTo("550");
		await Assert.That(Query(uri, "lang")).IsEqualTo("fr");
		await Assert.That(System.Web.HttpUtility.ParseQueryString(uri.Query).Count).IsEqualTo(4);
		await Assert.That(WikiBrowseState.FromUri(uri.AbsoluteUri).Search).IsEqualTo("Étoile & C++ \"quoted\"");
	}

	[Test]
	public async Task ArticleLinksReplaceDuplicateContextAndPreserveFragmentsAndArticleFields()
	{
		var path = WikiJourneyLinks.Article("/wiki/main/intro?membersSkip=100&lang=en&lang=de&browseQ=old&browseQ=forged&returnUrl=https%3A%2F%2Fevil.example#sheet%20one",
			new WikiBrowseState("package:reference", "Étoile & C++", "guides", 500, "fr"), membersCollection: "package:reference");
		var uri = new Uri(new Uri(_nav.BaseUri), path);
		await Assert.That(uri.AbsolutePath).IsEqualTo("/wiki/main/intro");
		await Assert.That(uri.Fragment).IsEqualTo("#sheet%20one");
		await Assert.That(Query(uri, "membersSkip")).IsEqualTo("100");
		await Assert.That(Query(uri, "browseCollection")).IsEqualTo("package:reference");
		await Assert.That(Query(uri, "browseQ")).IsEqualTo("Étoile & C++");
		await Assert.That(Query(uri, "browseCategory")).IsEqualTo("guides");
		await Assert.That(Query(uri, "browseSkip")).IsEqualTo("500");
		await Assert.That(Query(uri, "membersCollection")).IsEqualTo("package:reference");
		await Assert.That(Query(uri, "lang")).IsEqualTo("fr");
		await Assert.That(Query(uri, "returnUrl")).IsNull();
		await Assert.That(uri.Query.TrimStart('?').Split('&').Length).IsEqualTo(7);
	}

	[Test]
	public async Task CollectionRouteUsesTheLastRawValuesBeforeOffsetConversion()
	{
		Browse("?q=missing&q=Rule&category=missing&category=guides&skip=50&skip=599&lang=en&lang=fr");
		await Assert.That(Query(RowRequests[0], "search")).IsEqualTo("Rule");
		await Assert.That(Query(RowRequests[0], "category")).IsEqualTo("guides");
		await Assert.That(Query(RowRequests[0], "skip")).IsEqualTo("550");
		await Assert.That(Query(RowRequests[0], "lang")).IsEqualTo("fr");
		await Assert.That(new Uri(_nav.Uri).Query.TrimStart('?').Split('&').Length).IsEqualTo(4);
	}

	[Test]
	public async Task LastEmptyAndOverflowValuesOverrideEarlierValidValues()
	{
		var state = WikiBrowseState.FromUri("https://localhost/wiki/collections/all?q=Rule&q=&category=guides&category=&skip=500&skip=999999999999999999999&lang=fr&lang=de");
		await Assert.That(state.Search).IsNull();
		await Assert.That(state.Category).IsNull();
		await Assert.That(state.Skip).IsEqualTo(0);
		await Assert.That(state.Locale).IsEqualTo("de");
		await Assert.That(WikiJourneyLinks.Collection(state)).IsEqualTo("/wiki/collections/all?lang=de");
	}

	[Test]
	public async Task NextAtTheLargestOffsetDoesNotOverflowOrWrapToTheFirstPage()
	{
		var state = new WikiBrowseState("all", Skip: int.MaxValue).Next();
		await Assert.That(state.Skip).IsEqualTo(2147483600);
		await Assert.That(state.Previous().Skip).IsEqualTo(2147483550);
		await Assert.That(new WikiBrowseState("all", Skip: -1).Previous().Skip).IsEqualTo(0);
	}

	[Test]
	public async Task AbsoluteArticleDestinationsCannotBecomeReturnUrls()
	{
		var path = WikiJourneyLinks.Article("https://evil.example/wiki/main/intro?returnUrl=https%3A%2F%2Fevil.example", new WikiBrowseState("game"));
		var uri = new Uri(new Uri(_nav.BaseUri), path);
		await Assert.That(uri.Host).IsEqualTo(new Uri(_nav.BaseUri).Host);
		await Assert.That(uri.AbsolutePath).IsEqualTo("/wiki");
		await Assert.That(Query(uri, "returnUrl")).IsNull();
	}

	[Test]
	public async Task CanonicalizationReplacesHistoryAndUserPagingPushesHistory()
	{
		var cut = Browse("?q=Rule&skip=599&lang=fr");
		var nav = Services.GetRequiredService<BunitNavigationManager>();
		await Assert.That(nav.History.First().Options.ReplaceHistoryEntry).IsTrue();
		await cut.FindAll(".wiki-collection-paging button")[0].ClickAsync(new());
		await Assert.That(Query(new Uri(nav.Uri), "skip")).IsEqualTo("500");
		await Assert.That(nav.History.First().Options.ReplaceHistoryEntry).IsFalse();
	}

	[Test]
	public async Task AChangedBrowseStateRevalidatesNavigationAfterAccessMayHaveChanged()
	{
		var cut = Browse("?q=Rule&lang=fr");
		var before = _api.Requests.Count(path => path.StartsWith("/api/wiki/navigation", StringComparison.Ordinal));
		await cut.FindAll(".wiki-collection-paging button")[1].ClickAsync(new());
		await Assert.That(_api.Requests.Count(path => path.StartsWith("/api/wiki/navigation", StringComparison.Ordinal))).IsEqualTo(before + 1);
		await Assert.That(RowRequests.Length).IsEqualTo(2);
	}

	[Test]
	public async Task ChangingTotalsCorrectsToTheLastPageOnceWithoutReloadingItAgain()
	{
		var cut = Browse("?q=Rule&skip=500&lang=fr");
		_api.CollectionRows = _api.CollectionRows![..61];
		await cut.FindAll(".wiki-collection-paging button")[1].ClickAsync(new());
		await Assert.That(RowRequests.Length).IsEqualTo(3);
		await Assert.That(Query(RowRequests[1], "skip")).IsEqualTo("550");
		await Assert.That(Query(RowRequests[2], "skip")).IsEqualTo("50");
		await Assert.That(Query(new Uri(_nav.Uri), "skip")).IsEqualTo("50");
		await Assert.That(cut.FindAll(".wiki-collection-page").Count).IsEqualTo(11);
		await Assert.That(cut.Find(".wiki-collection-page").TextContent).Contains("Rule 0050");
	}

	[Test]
	public async Task EmptyResultsClearTheOffsetWithoutASecondRequest()
	{
		var cut = Browse("?q=missing&skip=900&lang=fr");
		await Assert.That(RowRequests.Length).IsEqualTo(1);
		await Assert.That(Query(new Uri(_nav.Uri), "skip")).IsNull();
		await Assert.That(Query(new Uri(_nav.Uri), "q")).IsEqualTo("missing");
		await Assert.That(cut.FindAll(".wiki-collection-page")).IsEmpty();
		await Assert.That(cut.Find(".wiki-empty").TextContent).Contains("missing");
	}

	[Test]
	public async Task SearchTruncationDoesNotLeaveAnUnpairedUnicodeSurrogate()
	{
		var state = new WikiBrowseState("all", new string('x', 199) + "🌟overflow").Normalize();
		await Assert.That(state.Search).IsEqualTo(new string('x', 199));
		await Assert.That(WikiJourneyLinks.Collection(state)).IsEqualTo("/wiki/collections/all?q=" + new string('x', 199));
	}

	[Test]
	public async Task ASecondConcurrentShrinkOffersRecoveryWithoutAnInvalidRangeOrCorrectionLoop()
	{
		var handler = new ShrinkingRowsApi();
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri(_nav.BaseUri) });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
		_nav.NavigateTo("/wiki/collections/all?skip=900&lang=fr");
		var cut = Render<WikiCollection>(parameters => parameters.Add(p => p.Collection, "all"));
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-error"));
		await Assert.That(handler.RowsRequests).IsEqualTo(2);
		await Assert.That(Query(new Uri(_nav.Uri), "skip")).IsEqualTo("50");
		await Assert.That(cut.FindAll(".wiki-collection-range")).IsEmpty();
		await cut.Find(".wiki-collection-error button").ClickAsync(new());
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));
		await Assert.That(handler.RowsRequests).IsEqualTo(3);
	}

	[Test]
	[Arguments("search")]
	[Arguments("category")]
	public async Task BoundedTextNormalizationIsIdempotentWhenTheCapLandsOnWhitespace(string field)
	{
		var bounded = new string('x', 199);
		var raw = bounded + " overflow";
		var state = field == "search" ? new WikiBrowseState("all", Search: raw) : new WikiBrowseState("all", Category: raw);
		var normalized = state.Normalize();
		await Assert.That(normalized.Normalize()).IsEqualTo(normalized);
		await Assert.That(field == "search" ? normalized.Search : normalized.Category).IsEqualTo(bounded);
	}

	[Test]
	public async Task WhitespaceAtTheSearchCapUsesOneCanonicalIdentityForTheRequestAndRoute()
	{
		var bounded = new string('x', 199);
		Browse("?q=" + Uri.EscapeDataString("  " + bounded + " overflow  ") + "&lang=fr");
		var applied = Query(RowRequests.Single(), "search");
		var location = new Uri(_nav.Uri);
		await Assert.That(applied).IsEqualTo(bounded);
		await Assert.That(Query(location, "q")).IsEqualTo(bounded);
		await Assert.That(Query(location, "q")).IsEqualTo(applied);
		await Assert.That(Query(location, "lang")).IsEqualTo("fr");
		await Assert.That(Services.GetRequiredService<BunitNavigationManager>().History.First().Options.ReplaceHistoryEntry).IsTrue();
		await Assert.That(_api.Requests.Count(path => path.StartsWith("/api/wiki/navigation", StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(RowRequests.Length).IsEqualTo(1);
	}

	private sealed class ShrinkingRowsApi : HttpMessageHandler
	{
		public int RowsRequests { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath == "/api/wiki/navigation")
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new WikiNavigationDto("all", 611, [], []))
				});
			RowsRequests++;
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(RowsRequests <= 2 ? "[]" : "[" + WikiApiFake.Page("1", "last", "Last page", "guides") + "]")
			};
			response.Headers.Add("X-Total-Count", RowsRequests == 1 ? "611" : "61");
			return Task.FromResult(response);
		}
	}

	private sealed class DelayedRowsApi : HttpMessageHandler
	{
		private readonly TaskCompletionSource<HttpResponseMessage> _old = new();
		public bool OldRequested => OldRequests > 0;
		public int OldRequests { get; private set; }

		public void ReleaseOld() => _old.SetResult(Rows("Old result", 1));

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			if (uri.AbsolutePath == "/api/wiki/navigation")
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new WikiNavigationDto("package:reference", 611,
						[new WikiCollectionSummaryDto("package:reference", "Reference", 611)], []))
				});
			if (Query(uri, "search") == "Old")
			{
				OldRequests++;
				return _old.Task;
			}
			return Task.FromResult(Rows("New result", 611));
		}

		private static HttpResponseMessage Rows(string title, int total)
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("[" + WikiApiFake.Page("1", "result", title, "guides") + "]")
			};
			response.Headers.Add("X-Total-Count", total.ToString());
			return response;
		}
	}
}
