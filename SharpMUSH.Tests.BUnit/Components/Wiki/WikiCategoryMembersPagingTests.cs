using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>Real article routing and HTTP requests, with 611 authorized category members.</summary>
public class WikiCategoryMembersPagingTests : TrackingBunitContext
{
	private const string Journey = "browseCollection=package%3Aorigin&browseQ=R%C3%A8gle%20%26%20C%2B%2B&browseCategory=fighter%20%26%20feats&browseSkip=100&membersCollection=package%3Arules&lang=fr-CA&extra=a%26b#costs";
	private readonly CategoryApi _api = new();
	private readonly NavigationManager _nav;

	public WikiCategoryMembersPagingTests()
	{
		WikiApiFake.Install(this);
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
		Services.AddSingleton<WikiMarkdigPipeline>()
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance));
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
		_nav = Services.GetRequiredService<NavigationManager>();
	}

	[Test]
	public async Task CharacterBiographyMemberKeepsCanonicalProfileDestinationAndRequestedLocale()
	{
		_api.Members = _ => Task.FromResult(CategoryApi.Batch([CategoryApi.Row("Ada", "Ada biography", "character")], 1));
		var cut = Open("/wiki/category/guides?" + Journey);
		WaitForMember(cut, "Ada biography");

		var destination = Address(cut.Find(".wiki-cat a.kit-tile"));
		await Assert.That(destination.AbsolutePath).IsEqualTo("/character/ada");
		await Assert.That(Query(destination)["lang"]).IsEqualTo("fr-CA");
	}

	[Test]
	public async Task ArticleSidebarIntroductionReturnsToOriginalSearchWhileMembersUseAuthorizedSidebarOwner()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Journey);
		var sidebar = Render<WikiSidebar>();
		sidebar.WaitForAssertion(() => sidebar.Find(".wiki-side-cats a.kit-row"));
		var introduction = sidebar.Find(".wiki-side-cats a.kit-row").GetAttribute("href")!;
		var query = Query(new Uri(new Uri(_nav.Uri), introduction));
		await Assert.That(query["browseCollection"]).IsEqualTo("package:origin");
		await Assert.That(query["browseQ"]).IsEqualTo("Règle & C++");
		await Assert.That(query["browseCategory"]).IsEqualTo("fighter & feats");
		await Assert.That(query["browseSkip"]).IsEqualTo("100");
		await Assert.That(query["membersCollection"]).IsEqualTo("package:owner");
		await Assert.That(query["membersSkip"]).IsNull();
		await Assert.That(query["lang"]).IsEqualTo("fr-CA");

		var article = Open(introduction);
		WaitForMember(article, "Owner member 000");
		article.WaitForAssertion(() => article.Find(".wiki-back-results"));
		var back = Address(article.Find(".wiki-back-results"));
		await Assert.That(back.AbsolutePath).IsEqualTo("/wiki/collections/package%3Aorigin");
		await Assert.That(Query(back)["q"]).IsEqualTo("Règle & C++");
		await Assert.That(Query(back)["category"]).IsEqualTo("fighter & feats");
		await Assert.That(Query(back)["skip"]).IsEqualTo("100");
		await Assert.That(Query(back)["lang"]).IsEqualTo("fr-CA");
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["collection"] == "package:origin")).IsTrue();
	}

	[Test]
	[Arguments(null)]
	[Arguments("package:hidden")]
	[Arguments("package:unknown")]
	[Arguments("https://outside.invalid/private")]
	public async Task ArticleSidebarIntroductionDoesNotInventOrPropagateAnUnauthorizedReturn(string? origin)
	{
		_nav.NavigateTo("/wiki/main/intro?lang=fr-CA" + (origin is null ? string.Empty
			: "&browseCollection=" + Uri.EscapeDataString(origin) + "&browseQ=PRIVATE_QUERY&browseCategory=PRIVATE_CATEGORY&browseSkip=100"));
		var sidebar = Render<WikiSidebar>();
		sidebar.WaitForAssertion(() => sidebar.Find(".wiki-side-cats a.kit-row"));
		var introduction = sidebar.Find(".wiki-side-cats a.kit-row").GetAttribute("href")!;
		var query = Query(new Uri(new Uri(_nav.Uri), introduction));
		await Assert.That(query["browseCollection"]).IsNull();
		await Assert.That(query["browseQ"]).IsNull();
		await Assert.That(query["browseCategory"]).IsNull();
		await Assert.That(query["browseSkip"]).IsNull();
		await Assert.That(query["membersCollection"]).IsEqualTo("package:owner");
		await Assert.That(query["lang"]).IsEqualTo("fr-CA");
		await Assert.That(sidebar.Markup).DoesNotContain("PRIVATE_QUERY");
		await Assert.That(sidebar.Markup).DoesNotContain("PRIVATE_CATEGORY");

		var article = Open(introduction);
		WaitForMember(article, "Owner member 000");
		await Assert.That(article.FindAll(".wiki-back-results")).IsEmpty();
	}

	[Test]
	public async Task CollectionSidebarIntroductionSeedsTheSubmittedOriginAndKeepsItsSelectedMembers()
	{
		_nav.NavigateTo("/wiki/collections/package%3Arules?q=R%C3%A8gle&category=fighter&skip=100&lang=fr-CA");
		var sidebar = Render<WikiSidebar>();
		sidebar.WaitForAssertion(() => sidebar.Find(".wiki-side-cats a.kit-row"));
		var query = Query(Address(sidebar.Find(".wiki-side-cats a.kit-row")));
		await Assert.That(query["browseCollection"]).IsEqualTo("package:rules");
		await Assert.That(query["browseQ"]).IsEqualTo("Règle");
		await Assert.That(query["browseCategory"]).IsEqualTo("fighter");
		await Assert.That(query["browseSkip"]).IsEqualTo("100");
		await Assert.That(query["membersCollection"]).IsEqualTo("package:rules");
		await Assert.That(query["lang"]).IsEqualTo("fr-CA");
	}

	[Test]
	public async Task TextOnlyCategoryMemberKeepsNavigationMetadataAndAccessBadges()
	{
		_api.Members = _ => Task.FromResult(CategoryApi.Batch(
			[CategoryApi.Row("archive", "Archive category", "category") with
			{
				Published = false,
				IsRestricted = true,
				LastEditedBy = "Mara",
			}], 1));
		var cut = Open("/wiki/category/guides?membersCollection=package%3Arules");
		WaitForMember(cut, "Archive category");
		var link = cut.Find(".wiki-cat a.kit-tile");
		await Assert.That(Address(link).AbsolutePath).IsEqualTo("/wiki/category/archive");
		await Assert.That(link.QuerySelector(".kit-tile-label")!.TextContent).IsEqualTo("Archive category");
		await Assert.That(link.QuerySelector(".kit-tile-sub")!.TextContent).Contains("Mara");
		await Assert.That(link.QuerySelectorAll(".wiki-cat-tag").Length).IsEqualTo(3);
		await Assert.That(link.QuerySelectorAll(".kit-tile-media, .kit-tile-fallback, img.kit-tile-img").Length).IsEqualTo(0);
		await Assert.That(link.QuerySelector(".kit-tile-overlay")!.Closest(".kit-tile-media")).IsNull();
	}

	[Test]
	public async Task Initial611MemberCategoryRequestsOnlyOneFiftyRowPage()
	{
		var cut = Open("/wiki/category/guides?" + Journey);
		WaitForMember(cut, "Package member 000");
		await Assert.That(MemberLinks(cut).Count).IsEqualTo(50);
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(1);
		await AssertRequest(_api.MemberRequests.Single(), "package:rules", "0", "guides", "fr-CA");
		await Assert.That(cut.Find(".wiki-cat-range").TextContent.Replace(",", "")).Contains("611");
		await Assert.That(cut.Markup).DoesNotContain("Package member 050");
	}

	[Test]
	public async Task NextAndPreviousKeepIndependentReturnOffsetAndDoNotReloadTheBody()
	{
		var cut = Open("/wiki/category/guides?" + Journey);
		WaitForMember(cut, "Package member 000");
		await Pager(cut, 1).ClickAsync(new());
		WaitForMember(cut, "Package member 050");
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(2);
		await AssertRequest(_api.MemberRequests.Last(), "package:rules", "50", "guides", "fr-CA");
		await Assert.That(Query(new Uri(_nav.Uri))["membersSkip"]).IsEqualTo("50");
		await Assert.That(Query(new Uri(_nav.Uri))["browseSkip"]).IsEqualTo("100");
		await Assert.That(Query(new Uri(_nav.Uri))["extra"]).IsEqualTo("a&b");
		await Assert.That(new Uri(_nav.Uri).Fragment).IsEqualTo("#costs");
		await Assert.That(_api.ArticleRequests.Count).IsEqualTo(1);
		var linkQuery = Query(Address(MemberLinks(cut).First()));
		await Assert.That(linkQuery["browseCollection"]).IsEqualTo("package:origin");
		await Assert.That(linkQuery["browseQ"]).IsEqualTo("Règle & C++");
		await Assert.That(linkQuery["browseCategory"]).IsEqualTo("fighter & feats");
		await Assert.That(linkQuery["browseSkip"]).IsEqualTo("100");
		await Assert.That(linkQuery["membersCollection"]).IsEqualTo("package:rules");
		await Assert.That(linkQuery["membersSkip"]).IsEqualTo("50");
		await Assert.That(linkQuery["lang"]).IsEqualTo("fr-CA");
		await Pager(cut, 0).ClickAsync(new());
		WaitForMember(cut, "Package member 000");
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(3);
		await Assert.That(Query(new Uri(_nav.Uri))["membersSkip"]).IsNull();
		await Assert.That(_api.ArticleRequests.Count).IsEqualTo(1);
	}

	[Test]
	public async Task DeepLinkStartsAt500AndUsesTheServersMixedTitleOrder()
	{
		_api.MixedPage = true;
		var cut = Open("/wiki/category/guides?membersSkip=500&" + Journey);
		WaitForMember(cut, "Package member 500");
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(1);
		await AssertRequest(_api.MemberRequests.Single(), "package:rules", "500", "guides", "fr-CA");
		var titles = MemberLinks(cut).Select(link => link.TextContent).ToArray();
		await Assert.That(titles[0]).Contains("Package member 500");
		await Assert.That(titles[1]).Contains("Package member 501");
		await Assert.That(titles[2]).Contains("Package member 502");
		await Assert.That(cut.Find(".wiki-cat-range").TextContent).Contains("501");
		await Assert.That(cut.Find(".wiki-cat-range").TextContent).Contains("550");
		await Assert.That(cut.Markup).DoesNotContain("Package member 499");
	}

	[Test]
	[Arguments("-1", "0")]
	[Arguments("garbage", "0")]
	[Arguments("999999999999999999999999", "0")]
	[Arguments("549", "500")]
	[Arguments("0&membersSkip=500", "500")]
	public async Task RawOffsetsNormalizeBeforeTheFirstMemberRequest(string input, string expected)
	{
		var cut = Open("/wiki/category/guides?membersCollection=package%3Arules&membersSkip=" + input);
		WaitForMember(cut, expected == "500" ? "Package member 500" : "Package member 000");
		await AssertRequest(_api.MemberRequests.Single(), "package:rules", expected, "guides", null);
		await Assert.That(Query(new Uri(_nav.Uri))["membersSkip"]).IsEqualTo(expected == "0" ? null : expected);
	}

	[Test]
	public async Task ShrunkLastPageMakesOneCorrectionAndCanonicalReplacementReusesTheResponse()
	{
		_api.Total = 511;
		var cut = Open("/wiki/category/guides?membersSkip=600&" + Journey);
		WaitForMember(cut, "Package member 500");
		await Assert.That(_api.MemberRequests.Select(uri => Query(uri)["skip"]).ToArray()).IsEquivalentTo(new[] { "600", "500" });
		await Assert.That(Query(new Uri(_nav.Uri))["membersSkip"]).IsEqualTo("500");
		await Assert.That(Query(new Uri(_nav.Uri))["browseSkip"]).IsEqualTo("100");
		await Assert.That(MemberLinks(cut).Count).IsEqualTo(11);
		await Assert.That(_api.ArticleRequests.Count).IsEqualTo(1);
	}

	[Test]
	public async Task EmptyCategoryCanonicalizesToZeroWithoutACorrectionRequest()
	{
		_api.Total = 0;
		var cut = Open("/wiki/category/guides?membersSkip=600&" + Journey);
		cut.WaitForAssertion(() => cut.Find(".wiki-cat-empty"));
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(1);
		await Assert.That(Query(new Uri(_nav.Uri))["membersSkip"]).IsNull();
		await Assert.That(cut.Find(".wiki-cat-range").TextContent).Contains("0");
		await Assert.That(MemberLinks(cut)).IsEmpty();
	}

	[Test]
	public async Task ConcurrentSecondShrinkStopsAfterOneCorrectionAndOffersRetry()
	{
		_api.Members = uri => Task.FromResult(CategoryApi.Batch([], Query(uri)["skip"] == "600" ? 511 : 1));
		var cut = Open("/wiki/category/guides?membersSkip=600&" + Journey);
		cut.WaitForAssertion(() => cut.Find(".wiki-cat [role='alert']"));
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(2);
		await Assert.That(MemberLinks(cut)).IsEmpty();
		await Assert.That(cut.FindAll(".wiki-cat [role='alert'] button").Count).IsEqualTo(1);
		await Assert.That(cut.Markup).DoesNotContain("501–");
	}

	[Test]
	public async Task MemberFailureStaysHonestAndRetryLoadsOnlyTheRequestedPage()
	{
		_api.RefuseMembers = true;
		var cut = Open("/wiki/category/guides?membersSkip=500&" + Journey);
		cut.WaitForAssertion(() => cut.Find(".wiki-cat [role='alert']"));
		await Assert.That(MemberLinks(cut)).IsEmpty();
		await Assert.That(cut.FindAll(".wiki-cat-empty")).IsEmpty();
		_api.RefuseMembers = false;
		await cut.Find(".wiki-cat [role='alert'] button").ClickAsync(new());
		WaitForMember(cut, "Package member 500");
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(2);
		await AssertRequest(_api.MemberRequests.Last(), "package:rules", "500", "guides", "fr-CA");
	}

	[Test]
	[Arguments("game", "Game member 000")]
	[Arguments("package:rules", "Package member 000")]
	public async Task ExplicitMemberScopeIsIndependentOfTheReturnOrigin(string scope, string title)
	{
		var cut = Open("/wiki/category/guides?browseCollection=package%3Aorigin&browseSkip=100&membersCollection=" + Uri.EscapeDataString(scope) + "&lang=fr-CA");
		WaitForMember(cut, title);
		await AssertRequest(_api.MemberRequests.Single(), scope, "0", "guides", "fr-CA");
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["collection"] == scope && Query(uri)["lang"] == "fr-CA")).IsTrue();
		await Assert.That(cut.FindAll(".wiki-cat a").All(link => Query(Address(link))["membersCollection"] == scope)).IsTrue();
	}

	[Test]
	public async Task AbsentMemberHintResolvesAuthorizedOwnerRatherThanReturnOriginOrTranslatedTitle()
	{
		var cut = Open("/wiki/category/guides?browseCollection=package%3Aorigin&lang=fr-CA");
		WaitForMember(cut, "Owner member 000");
		await AssertRequest(_api.MemberRequests.Single(), "package:owner", "0", "guides", "fr-CA");
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["page"] == "category/guides" && Query(uri)["lang"] == "fr-CA")).IsTrue();
	}

	[Test]
	public async Task MissingIntroductionAndOwnerFallbackHonestlyToGameMembers()
	{
		_api.HiddenIntroduction = true;
		_api.MissingOwner = true;
		var cut = Open("/wiki/category/guides?browseCollection=package%3Aorigin&lang=fr-CA");
		WaitForMember(cut, "Game member 000");
		await AssertRequest(_api.MemberRequests.Single(), "game", "0", "guides", "fr-CA");
		await Assert.That(cut.Markup).DoesNotContain("Private authored introduction");
		await Assert.That(cut.FindAll(".wiki-article-body")).IsEmpty();
	}

	[Test]
	public async Task OwnerNavigationOutageShowsAnHonestRetryInsteadOfSwitchingToGameMembers()
	{
		_api.OwnerUnavailable = true;
		var cut = Open("/wiki/category/guides?browseCollection=package%3Aorigin&lang=fr-CA");
		cut.WaitForAssertion(() => cut.Find(".wiki-cat [role='alert']"));
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["page"] == "category/guides")).IsTrue();
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["collection"] == "game")).IsFalse();
		await Assert.That(_api.MemberRequests).IsEmpty();
		await Assert.That(cut.FindAll(".wiki-cat-empty")).IsEmpty();
		_api.OwnerUnavailable = false;
		await cut.Find(".wiki-cat [role='alert'] button").ClickAsync(new());
		WaitForMember(cut, "Owner member 000");
		await AssertRequest(_api.MemberRequests.Single(), "package:owner", "0", "guides", "fr-CA");
	}

	[Test]
	public async Task HiddenIntroductionIsNotRevealedByAuthorizedVisibleMembers()
	{
		_api.HiddenIntroduction = true;
		var cut = Open("/wiki/category/guides?lang=fr-CA");
		WaitForMember(cut, "Owner member 000");
		await Assert.That(cut.Markup).DoesNotContain("Private authored introduction");
		await Assert.That(cut.Markup).DoesNotContain("Authored category body");
		await Assert.That(cut.FindAll(".wiki-article-body")).IsEmpty();
	}

	[Test]
	public async Task AuthorizedTitleAndAuthoredBodyPrecedeTheMemberList()
	{
		var cut = Open("/wiki/category/guides?lang=fr-CA");
		WaitForMember(cut, "Owner member 000");
		var content = cut.Find(".wiki-page-main").TextContent;
		var title = content.IndexOf("Private authored introduction", StringComparison.Ordinal);
		var body = content.IndexOf("Authored category body", StringComparison.Ordinal);
		var members = content.IndexOf("Owner member 000", StringComparison.Ordinal);
		await Assert.That(title >= 0 && title < body && body < members).IsTrue();
	}

	[Test]
	[Arguments("game", "lore", "Game member 000")]
	[Arguments("package:rules", "lore", "Package member 000")]
	public async Task DelayedOldScopeAndCategoryCannotOverwriteTheNewSelection(string collection, string category, string title)
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		_api.Members = uri => Query(uri)["category"] == "guides" ? pending.Task : Task.FromResult(_api.DefaultBatch(uri));
		var cut = Open("/wiki/category/guides?" + Journey);
		try
		{
			cut.WaitForAssertion(() => Require(_api.MemberRequests.Count == 1, "Initial member request did not start."));
			_nav.NavigateTo("/wiki/category/" + category + "?membersCollection=" + Uri.EscapeDataString(collection) + "&lang=de");
			WaitForMember(cut, title);
			await cut.InvokeAsync(() => CompletePending(pending, CategoryApi.Batch([CategoryApi.Row("private", "STALE PRIVATE MEMBER")], 1)));
			await Assert.That(cut.Markup).DoesNotContain("STALE PRIVATE MEMBER");
			await Assert.That(new Uri(_nav.Uri).AbsolutePath).IsEqualTo("/wiki/category/lore");
			await AssertRequest(_api.MemberRequests.Last(), collection, "0", "lore", "de");
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, CategoryApi.Batch([], 0))); }
	}

	[Test]
	[Arguments("browser", true)]
	[Arguments("sidebar", true)]
	[Arguments("browser", false)]
	[Arguments("sidebar", false)]
	public async Task CategoryNameOpensOnlyAnAuthorizedIntroductionAndMembersRemainASeparateAction(string surface, bool hasPage)
	{
		_api.HasPage = hasPage;
		_nav.NavigateTo("/wiki/collections/package%3Arules?q=R%C3%A8gle&category=lore&skip=100&lang=fr-CA");
		var cut = RenderSurface(surface);
		cut.WaitForAssertion(() => Require(cut.Markup.Contains("Guides français", StringComparison.Ordinal), "The authorized category directory is not visible."));
		var introductions = cut.FindAll("a").Where(link => Address(link).AbsolutePath == "/wiki/category/guides").ToArray();
		await Assert.That(hasPage ? introductions.Length > 0 : introductions.Length == 0).IsTrue();
		if (hasPage)
		{
			var query = Query(Address(introductions.First()));
			await Assert.That(query["membersCollection"]).IsEqualTo("package:rules");
			await Assert.That(query["browseCollection"]).IsEqualTo("package:rules");
			await Assert.That(query["browseSkip"]).IsEqualTo("100");
			await Assert.That(query["lang"]).IsEqualTo("fr-CA");
		}
		var memberActions = cut.FindAll("a,button").Where(element => element.TagName == "BUTTON"
			? (element.GetAttribute("aria-label") ?? element.TextContent).Contains("Guides français", StringComparison.Ordinal)
			: Query(Address(element))["category"] == "guides").ToArray();
		await Assert.That(memberActions.Length > 0).IsTrue();
	}

	[Test]
	[Arguments("browser", false)]
	[Arguments("browser", true)]
	[Arguments("sidebar", false)]
	[Arguments("sidebar", true)]
	[Arguments("members", false)]
	[Arguments("members", true)]
	public async Task ReaderChangeClearsCompletedPrivateDirectoryAndMembersAtTheSameUri(string surface, bool characterChange)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		_nav.NavigateTo(surface == "members" ? "/wiki/category/guides?" + Journey : "/wiki/collections/package%3Arules?lang=fr-CA");
		var cut = RenderSurface(surface);
		cut.WaitForAssertion(() => Require(cut.Markup.Contains(surface == "sidebar" ? "Guides français" : surface == "members" ? "Owner member 000" : "Package member 000", StringComparison.Ordinal), "Initial private content did not render."));
		var unchanged = _nav.Uri;
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		_api.Directory = _ => pending.Task;
		_api.RefuseMembers = true;
		try
		{
			var before = _api.NavigationRequests.Count;
			await cut.InvokeAsync(() => RaiseReaderChanged(auth, characterChange));
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count > before, "Reader change did not reauthorize the directory."));
			await Assert.That(cut.Markup).DoesNotContain("Package member 000");
			await Assert.That(cut.Markup).DoesNotContain("Owner member 000");
			await Assert.That(cut.Markup).DoesNotContain("Private rules collection");
			await Assert.That(cut.Markup).DoesNotContain("Guides français");
			await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.Forbidden)));
			await Assert.That(_nav.Uri).IsEqualTo(unchanged);
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.Forbidden))); }
	}

	[Test]
	[Arguments("browser", false)]
	[Arguments("browser", true)]
	[Arguments("sidebar", false)]
	[Arguments("sidebar", true)]
	[Arguments("members", false)]
	[Arguments("members", true)]
	public async Task DelayedOldAuthorizationCannotPublishAfterReaderChange(string surface, bool characterChange)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		_api.Directory = _ => pending.Task;
		_nav.NavigateTo(surface == "members" ? "/wiki/category/guides?" + Journey : "/wiki/collections/package%3Arules");
		var cut = RenderSurface(surface);
		try
		{
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count > 0, "Initial directory request did not start."));
			_api.Directory = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
			_api.RefuseMembers = true;
			var before = _api.NavigationRequests.Count;
			await cut.InvokeAsync(() => RaiseReaderChanged(auth, characterChange));
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count > before, "Reader change did not start current authorization."));
			await cut.InvokeAsync(() => CompletePending(pending, CategoryApi.Navigation("package:rules")));
			await Assert.That(cut.Markup).DoesNotContain("Private rules collection");
			await Assert.That(cut.Markup).DoesNotContain("Guides français");
			await Assert.That(cut.Markup).DoesNotContain("Package member");
			await Assert.That(cut.Markup).DoesNotContain("Owner member");
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.Forbidden))); }
	}

	[Test]
	public async Task UnknownExplicitMemberScopeCannotBorrowTheVisibleOwnerOrReturnCollection()
	{
		var cut = Open("/wiki/category/guides?browseCollection=package%3Aorigin&membersCollection=package%3Ahidden&lang=fr-CA");
		cut.WaitForAssertion(() => cut.Find(".wiki-cat [role='alert']"));
		await Assert.That(MemberLinks(cut)).IsEmpty();
		await Assert.That(_api.MemberRequests).IsEmpty();
		await Assert.That(cut.Markup).DoesNotContain("Owner member");
	}

	[Test]
	public async Task SameCategoryScopeChangeDiscardsTheOldPendingMemberResponse()
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		_api.Members = uri => Uri.UnescapeDataString(uri.AbsolutePath).Contains("package:rules", StringComparison.Ordinal)
			? pending.Task : Task.FromResult(_api.DefaultBatch(uri));
		var cut = Open("/wiki/category/guides?" + Journey);
		try
		{
			cut.WaitForAssertion(() => Require(_api.MemberRequests.Count == 1, "Initial package member request did not start."));
			_nav.NavigateTo("/wiki/category/guides?membersCollection=game&lang=fr-CA");
			WaitForMember(cut, "Game member 000");
			await cut.InvokeAsync(() => CompletePending(pending, CategoryApi.Batch([CategoryApi.Row("old", "OLD PACKAGE SECRET")], 1)));
			await Assert.That(cut.Markup).DoesNotContain("OLD PACKAGE SECRET");
			await Assert.That(_api.ArticleRequests.Count).IsEqualTo(1);
			await AssertRequest(_api.MemberRequests.Last(), "game", "0", "guides", "fr-CA");
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, CategoryApi.Batch([], 0))); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OldPendingMemberResponseCannotPublishAfterReaderAuthorizationWasRevoked(bool characterChange)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		_api.Members = _ => pending.Task;
		_nav.NavigateTo("/wiki/category/guides?lang=fr-CA");
		var cut = Render<WikiCategoryMembers>(p => p.Add(member => member.Category, "guides").Add(member => member.Locale, "fr-CA"));
		try
		{
			cut.WaitForAssertion(() => Require(_api.MemberRequests.Count == 1, "Initial private member request did not start."));
			_api.Directory = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
			_api.RefuseMembers = true;
			await cut.InvokeAsync(() => RaiseReaderChanged(auth, characterChange));
			cut.WaitForAssertion(() => cut.Find(".wiki-cat [role='alert']"));
			await cut.InvokeAsync(() => CompletePending(pending, CategoryApi.Batch([CategoryApi.Row("old", "OLD CHARACTER SECRET")], 1)));
			await Assert.That(cut.Markup).DoesNotContain("OLD CHARACTER SECRET");
			await Assert.That(MemberLinks(cut)).IsEmpty();
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, CategoryApi.Batch([], 0))); }
	}

	[Test]
	[Arguments("browser", false)]
	[Arguments("browser", true)]
	[Arguments("sidebar", false)]
	[Arguments("sidebar", true)]
	[Arguments("members", false)]
	[Arguments("members", true)]
	public async Task DisposedComponentsDoNotReloadOnLaterReaderSignals(string surface, bool characterChange)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		_nav.NavigateTo(surface == "members" ? "/wiki/category/guides?" + Journey : "/wiki/collections/package%3Arules");
		var cut = RenderSurface(surface);
		cut.WaitForAssertion(() => Require(cut.Markup.Contains(surface == "sidebar" ? "Guides français" : surface == "members" ? "Owner member 000" : "Package member 000", StringComparison.Ordinal), "Initial content did not render."));
		// Removing the child through the renderer exercises actual component disposal; disposing
		// bUnit's rendered-root handle alone does not unmount this host's children.
		await cut.InvokeAsync(() => cut.Render(p => p.Add(host => host.Visible, false)));
		cut.WaitForAssertion(() => Require(string.IsNullOrEmpty(cut.Markup), "The component was not unmounted."));
		var directories = _api.NavigationRequests.Count;
		var members = _api.MemberRequests.Count;
		await cut.InvokeAsync(() => RaiseReaderChanged(auth, characterChange));
		await Assert.That(_api.NavigationRequests.Count).IsEqualTo(directories);
		await Assert.That(_api.MemberRequests.Count).IsEqualTo(members);
	}

	private IRenderedComponent<SurfaceHost> RenderSurface(string surface) => Render<SurfaceHost>(p => p.Add(host => host.Surface, surface));

	/// <summary>Hosts the actual selected component behind one strongly typed bUnit root.</summary>
	public sealed class SurfaceHost : ComponentBase
	{
		[Parameter] public string Surface { get; set; } = string.Empty;
		[Parameter] public bool Visible { get; set; } = true;

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			if (!Visible) return;
			if (Surface == "browser")
			{
				builder.OpenComponent<WikiCollection>(0);
				builder.AddAttribute(1, nameof(WikiCollection.Collection), "package:rules");
			}
			else if (Surface == "sidebar") builder.OpenComponent<WikiSidebar>(0);
			else
			{
				builder.OpenComponent<WikiCategoryMembers>(0);
				builder.AddAttribute(1, nameof(WikiCategoryMembers.Category), "guides");
				builder.AddAttribute(2, nameof(WikiCategoryMembers.Locale), "fr-CA");
			}
			builder.CloseComponent();
		}
	}

	private IRenderedComponent<Router> Open(string uri) { _nav.NavigateTo(uri); return RenderRoute(); }
	private IRenderedComponent<Router> RenderRoute() => Render<Router>(parameters => parameters
		.Add(p => p.AppAssembly, typeof(WikiPage).Assembly)
		.Add(p => p.Found, (RenderFragment<RouteData>)(route => builder =>
		{
			builder.OpenComponent(0, route.PageType);
			foreach (var value in route.RouteValues) builder.AddAttribute(1, value.Key, value.Value);
			builder.CloseComponent();
		})));
	private static void WaitForMember<T>(IRenderedComponent<T> cut, string title) where T : IComponent => cut.WaitForAssertion(() => Require(cut.Find(".wiki-cat").TextContent.Contains(title, StringComparison.Ordinal), "Expected current member: " + title));
	private static IReadOnlyList<IElement> MemberLinks<T>(IRenderedComponent<T> cut) where T : IComponent => cut.FindAll(".wiki-cat a").Where(link => Address(link).AbsolutePath.StartsWith("/wiki/main/", StringComparison.Ordinal) || Address(link).AbsolutePath.StartsWith("/wiki/category/", StringComparison.Ordinal)).ToArray();
	private static IElement Pager<T>(IRenderedComponent<T> cut, int index) where T : IComponent => cut.FindAll(".wiki-cat-paging button")[index];
	private static Uri Address(IElement link) => new(new Uri("http://localhost/"), link.GetAttribute("href") ?? string.Empty);
	private static NameValueCollection Query(Uri uri) => System.Web.HttpUtility.ParseQueryString(uri.Query);
	// A successful completion transfers ownership to the HTTP consumer; a rejected retry does not.
	private static void CompletePending(TaskCompletionSource<HttpResponseMessage> pending, HttpResponseMessage response)
	{
		if (!pending.TrySetResult(response)) response.Dispose();
	}

	private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
	private static async Task AssertRequest(Uri uri, string collection, string skip, string category, string? locale)
	{
		await Assert.That(Uri.UnescapeDataString(uri.AbsolutePath)).IsEqualTo("/api/wiki/collections/" + collection + "/pages");
		await Assert.That(Query(uri)["skip"]).IsEqualTo(skip);
		await Assert.That(Query(uri)["take"]).IsEqualTo("50");
		await Assert.That(Query(uri)["category"]).IsEqualTo(category);
		await Assert.That(Query(uri)["lang"]).IsEqualTo(locale);
	}
	private static void RaiseReaderChanged(IAccountAuthState auth, bool characterChange)
	{
		if (characterChange) auth.ActiveCharacterChanged += Raise.Event<Action>();
		else auth.AuthStateChanged += Raise.Event<Action>();
	}

	private sealed class CategoryApi : HttpMessageHandler
	{
		public List<Uri> MemberRequests { get; } = [];
		public List<Uri> NavigationRequests { get; } = [];
		public List<Uri> ArticleRequests { get; } = [];
		public Func<Uri, Task<HttpResponseMessage>>? Members { get; set; }
		public Func<Uri, Task<HttpResponseMessage>>? Directory { get; set; }
		public int Total { get; set; } = 611;
		public bool RefuseMembers { get; set; }
		public bool HiddenIntroduction { get; set; }
		public bool MissingOwner { get; set; }
		public bool OwnerUnavailable { get; set; }
		public bool MixedPage { get; set; }
		public bool HasPage { get; set; } = true;

		public static HttpResponseMessage Navigation(string collection, bool hasPage = true) => Json(new WikiNavigationDto(collection, 611,
			[new WikiCollectionSummaryDto("package:rules", "Private rules collection", 611), new WikiCollectionSummaryDto("package:owner", "Owner collection", 611), new WikiCollectionSummaryDto("package:origin", "Return origin", 611)],
			[new WikiCategorySummaryDto("guides", "Guides français", 611, hasPage, true)]));
		public static WikiPageSummaryDto Row(string slug, string title, string ns = "main") => new(slug, slug, title, ns, DateTimeOffset.UnixEpoch, false, 1, ["guides"], true, "en", true, null, null);
		public static HttpResponseMessage Batch(WikiPageSummaryDto[] rows, int total)
		{
			var response = Json(rows);
			response.Headers.Add("X-Total-Count", total.ToString(System.Globalization.CultureInfo.InvariantCulture));
			return response;
		}
		public HttpResponseMessage DefaultBatch(Uri uri)
		{
			var query = Query(uri);
			var skip = int.TryParse(query["skip"], out var value) ? value : 0;
			var take = int.TryParse(query["take"], out value) ? value : 50;
			var collection = Uri.UnescapeDataString(uri.AbsolutePath.Split('/')[4]);
			var label = collection == "game" ? "Game" : collection == "package:owner" ? "Owner" : "Package";
			return Batch(Enumerable.Range(0, Total).Skip(skip).Take(take)
				.Select(i => Row("member_" + i.ToString("D3"), label + " member " + i.ToString("D3"), MixedPage && i == 501 ? "category" : "main")).ToArray(), Total);
		}
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			var query = Query(uri);
			if (uri.AbsolutePath == "/api/wiki/navigation")
			{
				NavigationRequests.Add(uri);
				if (Directory is { } directory) return await directory(uri);
				if (OwnerUnavailable && query["page"] is not null) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
				if (MissingOwner && query["page"] is not null) return new HttpResponseMessage(HttpStatusCode.NotFound);
				var collection = query["collection"] ?? "package:owner";
				return collection is "game" or "all" or "package:rules" or "package:owner" or "package:origin" ? Navigation(collection, HasPage) : new HttpResponseMessage(HttpStatusCode.Forbidden);
			}
			if (uri.AbsolutePath.StartsWith("/api/wiki/collections/", StringComparison.Ordinal))
			{
				MemberRequests.Add(uri);
				if (RefuseMembers) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
				return Members is { } members ? await members(uri) : DefaultBatch(uri);
			}
			if (uri.AbsolutePath.StartsWith("/api/wiki/ns/category/", StringComparison.Ordinal))
			{
				ArticleRequests.Add(uri);
				if (HiddenIntroduction) return new HttpResponseMessage(HttpStatusCode.NotFound);
				var slug = uri.AbsolutePath.Split('/').Last();
				return Json(new WikiPageDto(slug, slug, "Private authored introduction", "category", "Source", "<p>Authored category body</p>", "body", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, false, 1, [], true)
				{ Locale = "en", RequestedLocale = "fr-CA", IsFallback = true, AvailableLocales = ["en", "de"], Access = new WikiAccessDto(true, false, false, false) });
			}
			if (uri.AbsolutePath == "/api/wiki/category-names") return Json(new Dictionary<string, string>());
			return new HttpResponseMessage(HttpStatusCode.NotFound);
		}
		private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
	}
}
