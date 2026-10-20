using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>Exercises article journeys through the real route, display and HTTP-backed wiki client.</summary>
public class WikiJourneyNavigationTests : TrackingBunitContext
{
	private const string Origin = "browseCollection=package%3Areference&browseQ=R%C3%A8gle%20%26%20C%2B%2B&browseCategory=fighter%20%26%20feats&browseSkip=100&membersCollection=game&membersSkip=50&lang=fr-CA";
	private readonly JourneyApi _api = new();
	private readonly NavigationManager _nav;

	public WikiJourneyNavigationTests()
	{
		Install(this, _api);
		_nav = Services.GetRequiredService<NavigationManager>();
	}

	[Test]
	[Arguments("same-fragment", true)]
	[Arguments("same-locale", true)]
	[Arguments("same-forged", true)]
	[Arguments("encoded-same", true)]
	[Arguments("same-case-namespace", true)]
	[Arguments("other-constrained", false)]
	[Arguments("other-namespace", false)]
	public async Task AuthoredBodyLinksKeepTrustedIdentityOnlyOnTheSameArticle(string anchorId, bool sameArticle)
	{
		_api.IntroHtmlOverride = JourneyApi.IntroHtml + """
			<a id="same-fragment" href="/wiki/main/intro#costs">Same article costs</a>
			<a id="same-locale" href="/wiki/main/intro?lang=de">Same article language link</a>
			<a id="same-forged" href="/wiki/main/intro?expectedPageId=forged#costs">Same article forged identity</a>
			<a id="encoded-same" href="/wiki/main/%69ntro#costs">Encoded same article</a>
			<a id="other-constrained" href="/wiki/main/guide?expectedPageId=forged">Other article</a>
			<a id="other-namespace" href="/wiki/help/intro?expectedPageId=forged">Other namespace</a>
			<a id="same-case-namespace" href="/wiki/MAIN/intro#costs">Same namespace casing</a>
			""";
		_nav.NavigateTo("/wiki/main/intro?" + Origin + "&expectedPageId=intro");
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		var anchor = cut.Find("#" + anchorId);
		var query = Query(anchor);
		if (sameArticle)
		{
			await Assert.That(query["expectedPageId"]).IsEqualTo("intro");
			await Assert.That(query.GetValues("expectedPageId")!.Length).IsEqualTo(1);
		}
		else await Assert.That(query["expectedPageId"]).IsNull();
		await Assert.That(query["expectedPageId"]).IsNotEqualTo("forged");
		await AssertJourney(anchor, "fr-CA");
		if (anchorId == "same-fragment") await Assert.That(Address(anchor).Fragment).IsEqualTo("#costs");
		await Assert.That(cut.Find("#local").GetAttribute("href")).IsEqualTo("#costs");
	}

	[Test]
	public async Task ArticleThenGuideReturnsToTheSameCollectionSearchCategoryOffsetAndRequestedLocale()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		var guide = cut.Find("#guide");
		await AssertJourney(guide, "fr-CA");
		_nav.NavigateTo(guide.GetAttribute("href")!);
		WaitForBody(cut, "Guide body");

		var back = ReturnLink(cut);
		await Assert.That(Path(back)).IsEqualTo("/wiki/collections/package%3Areference");
		await Assert.That(Query(back)["q"]).IsEqualTo("Règle & C++");
		await Assert.That(Query(back)["category"]).IsEqualTo("fighter & feats");
		await Assert.That(Query(back)["skip"]).IsEqualTo("100");
		await Assert.That(Query(back)["lang"]).IsEqualTo("fr-CA");
		await Assert.That(cut.FindAll("a").Any(a => Path(a) == "/wiki" && Query(a)["lang"] == "fr-CA")).IsTrue();
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["collection"] == "package:reference" && Query(uri)["lang"] == "fr-CA")).IsTrue();
	}

	[Test]
	public async Task DirectArticleUsesAuthorizedOwnerWithoutInventingResultState()
	{
		_nav.NavigateTo("/wiki/main/intro?lang=fr-CA");
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll("a").Any(a => Path(a) == "/wiki/collections/package%3Aowner"), "Authorized owner browse link missing."));

		var owner = cut.FindAll("a").First(a => Path(a) == "/wiki/collections/package%3Aowner");
		await Assert.That(Query(owner)["q"]).IsNull();
		await Assert.That(Query(owner)["category"]).IsNull();
		await Assert.That(Query(owner)["skip"]).IsNull();
		await Assert.That(Query(owner)["lang"]).IsEqualTo("fr-CA");
		await Assert.That(Query(cut.Find("#guide"))["browseCollection"]).IsNull();
		await Assert.That(Query(cut.Find("#guide"))["lang"]).IsEqualTo("fr-CA");
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["page"] == "main/intro" && Query(uri)["collection"] is null)).IsTrue();
	}

	[Test]
	[Arguments("package:hidden")]
	[Arguments("package:unknown")]
	[Arguments("https://outside.invalid/private")]
	public async Task UntrustedUnknownOrHiddenReturnScopeFallsBackWithoutExposingItsDestination(string collection)
	{
		_nav.NavigateTo("/wiki/main/intro?browseCollection=" + Uri.EscapeDataString(collection) + "&browseQ=Private%20query&browseCategory=Private%20category&lang=fr-CA&returnUrl=https%3A%2F%2Foutside.invalid");
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll("a").Any(a => Path(a) == "/wiki/collections/package%3Aowner"), "Safe authorized owner fallback missing."));

		await Assert.That(cut.FindAll("a").Any(a => Query(a)["q"] == "Private query" || Query(a)["browseQ"] == "Private query")).IsFalse();
		await Assert.That(cut.Markup).DoesNotContain("Private category");
		await Assert.That(cut.FindAll("a").Any(a => Query(a)["returnUrl"] is not null)).IsFalse();
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["collection"] == collection)).IsTrue();
	}

	[Test]
	public async Task QueryOnlyOffsetChangeRefreshesReturnAndBodyLinksWithoutRefetchingBodyOrExistence()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["skip"] == "100", "Initial return offset missing."));
		_nav.NavigateTo("/wiki/main/intro?" + Origin.Replace("browseSkip=100", "browseSkip=150", StringComparison.Ordinal));
		cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["skip"] == "150", "Return offset stayed stale."));

		await Assert.That(Query(cut.Find("#guide"))["browseSkip"]).IsEqualTo("150");
		await Assert.That(_api.ArticleRequests.Count).IsEqualTo(1);
		await Assert.That(_api.ExistenceBatches.Count).IsEqualTo(1);
		await Assert.That(_api.NavigationRequests.Count(uri => Query(uri)["collection"] == "package:reference")).IsEqualTo(2);
	}

	[Test]
	public async Task ReplayingAddressedHistoryRestoresEachResultState()
	{
		var first = "/wiki/main/intro?" + Origin;
		var second = "/wiki/main/intro?browseCollection=game&browseQ=Second&browseCategory=lore&browseSkip=250&membersCollection=package%3Aowner&lang=de";
		_nav.NavigateTo(first);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["q"] == "Règle & C++", "First result state missing."));
		_nav.NavigateTo(second);
		cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["q"] == "Second", "Second result state missing."));
		_nav.NavigateTo(first);
		cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["q"] == "Règle & C++", "Replayed state was not restored."));

		await AssertJourney(cut.Find("#guide"), "fr-CA");
		await Assert.That(Query(ReturnLink(cut))["skip"]).IsEqualTo("100");
	}

	[Test]
	public async Task TwoIndependentTabsKeepTheirOwnJourney()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var first = RenderRoute(this);
		WaitForBody(first, "Introduction body");
		using var secondContext = new JourneyContext();
		Install(secondContext, new JourneyApi());
		secondContext.Services.GetRequiredService<NavigationManager>().NavigateTo("/wiki/main/intro?browseCollection=game&browseQ=Other%20tab&lang=de");
		var second = RenderRoute(secondContext);
		WaitForBody(second, "Introduction body");
		second.WaitForAssertion(() => Require(Query(ReturnLink(second))["q"] == "Other tab", "Second tab return missing."));

		await AssertJourney(first.Find("#guide"), "fr-CA");
		await Assert.That(Query(ReturnLink(first))["q"]).IsEqualTo("Règle & C++");
		await Assert.That(Path(ReturnLink(second))).IsEqualTo("/wiki/collections/game");
	}

	[Test]
	[Arguments("", null)]
	[Arguments("-50", null)]
	[Arguments("oops", null)]
	[Arguments("2147483648", null)]
	[Arguments("77", "50")]
	public async Task ReturnOffsetsAreNormalizedFromRawUntrustedQuery(string offset, string? expected)
	{
		_nav.NavigateTo("/wiki/main/intro?browseCollection=package%3Areference&browseSkip=" + offset + "&lang=fr-CA");
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll("a").Any(a => Path(a) == "/wiki/collections/package%3Areference"), "Validated results return missing."));

		await Assert.That(Query(ReturnLink(cut))["skip"]).IsEqualTo(expected);
		await Assert.That(Query(cut.Find("#guide"))["browseSkip"]).IsEqualTo(expected);
	}

	[Test]
	public async Task RepeatedReturnFieldsUseLastValuesAndNormalizeSearchAndCategory()
	{
		var longSearch = new string('x', 210);
		_nav.NavigateTo("/wiki/main/intro?browseCollection=package%3Ahidden&browseCollection=game&browseQ=ignored&browseQ=%20%20" + longSearch + "%20%20&browseCategory=ignored&browseCategory=%20Lore%20%26%20Magic%20&browseSkip=500&browseSkip=77&lang=de&lang=fr-CA");
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll("a").Any(a => Path(a) == "/wiki/collections/game"), "Last collection value was not applied."));

		var back = ReturnLink(cut);
		await Assert.That(Query(back)["q"]).IsEqualTo(new string('x', 200));
		await Assert.That(Query(back)["category"]).IsEqualTo("Lore & Magic");
		await Assert.That(Query(back)["skip"]).IsEqualTo("50");
		await Assert.That(Query(back)["lang"]).IsEqualTo("fr-CA");
		await Assert.That(_api.NavigationRequests.Any(uri => Query(uri)["collection"] == "package:hidden")).IsFalse();
	}

	[Test]
	public async Task LocalAnchorRewritingPreservesQueriesFragmentsLabelsAndCanonicalRedlinkChecks()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.Find("#missing").ClassList.Contains("wiki-redlink"), "Queried missing article was not marked red."));

		await AssertJourney(cut.Find("#queried"), "fr-CA");
		await Assert.That(Query(cut.Find("#queried"))["extra"]).IsEqualTo("a&b");
		await Assert.That(Address(cut.Find("#queried")).Fragment).IsEqualTo("#costs");
		await Assert.That(cut.Find("#queried em").TextContent).IsEqualTo("carefully");
		await Assert.That(cut.Find("#queried").ClassList).Contains("authored-link");
		await Assert.That(cut.Find("#queried").ClassList.Contains("wiki-redlink")).IsFalse();
		await Assert.That(cut.Find("#missing em").TextContent).IsEqualTo("missing label");
		await AssertJourney(cut.Find("#missing"), "fr-CA");
		await Assert.That(Address(cut.Find("#missing")).Fragment).IsEqualTo("#detail");
		await Assert.That(_api.ExistenceBatches.SelectMany(batch => batch).Where(reference => reference.StartsWith("main/", StringComparison.Ordinal)).Distinct().Order().ToArray())
			.IsEquivalentTo(new[] { "main/guide", "main/missing", "main/salt&pepper" });
		await Assert.That(_api.ExistenceBatches.Count).IsEqualTo(1);
		await Assert.That(cut.FindComponent<WikiView>().Instance.Slug).IsEqualTo("intro");
		await Assert.That(cut.FindComponent<WikiDisplay>().Instance.Article!.RenderedHtml).IsEqualTo(JourneyApi.IntroHtml);
		await Assert.That(cut.FindComponent<WikiDisplay>().Instance.Article!.Content).IsEqualTo("Source stays unchanged");
	}

	[Test]
	public async Task NonArticleLinksAndTocAnchorsKeepTheirMeaning()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");

		await Assert.That(cut.Find("#local").GetAttribute("href")).IsEqualTo("#costs");
		await Assert.That(cut.Find("#external").GetAttribute("href")).IsEqualTo("https://outside.invalid/wiki/main/guide?lang=de#costs");
		await Assert.That(cut.Find("#character").GetAttribute("href")).IsEqualTo("/character/Ada");
		await Assert.That(cut.Find("#download").GetAttribute("href")).IsEqualTo("/api/wiki-assets/rules.pdf");
		await Assert.That(cut.Find("#editor").GetAttribute("href")).IsEqualTo("/wiki/main/guide/edit?lang=de");
		await Assert.That(cut.Find("#collection").GetAttribute("href")).IsEqualTo("/wiki/collections/all?q=local");
		var toc = cut.FindAll("a").Single(a => Address(a).Fragment == "#costs" && Path(a) == "/wiki/main/intro");
		await AssertJourney(toc, "fr-CA");
		await Assert.That(_api.ExistenceBatches.SelectMany(batch => batch).Any(reference => reference.Contains('?', StringComparison.Ordinal) || reference.Contains('#', StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task CategoryRelatedAndLocaleLinksKeepReturnOriginAndIndependentMemberScopeThroughFallback()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-more a").Any(a => Path(a) == "/wiki/main/related"), "Related card has not loaded."));

		await AssertJourney(cut.Find(".wiki-catlinks a"), "fr-CA");
		await AssertJourney(cut.FindAll(".wiki-more a").Single(a => Path(a) == "/wiki/main/related"), "fr-CA");
		var locales = cut.FindAll(".wiki-lang-chips a").Where(a => Path(a) == "/wiki/main/intro" && Query(a)["lang"] == "de").ToArray();
		await Assert.That(locales).IsNotEmpty();
		foreach (var locale in locales) await AssertJourney(locale, "de");
		await Assert.That(cut.FindComponent<WikiDisplay>().Instance.Article!.Locale).IsEqualTo("en");
		await Assert.That(cut.FindComponent<WikiDisplay>().Instance.Article!.RequestedLocale).IsEqualTo("en");
		await Assert.That(_api.ArticleRequests.Single().Query).Contains("lang=fr-CA");
	}

	[Test]
	public async Task ReturnOnlyOriginDoesNotManufactureAMemberSelectionForTheDifferentArticleOwner()
	{
		_nav.NavigateTo("/wiki/main/intro?lang=fr-CA");
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back").Any(link => Path(link) == "/wiki/collections/package%3Aowner"), "The article's authorized owner was not resolved."));
		_nav.NavigateTo("/wiki/main/intro?browseCollection=package%3Areference&browseQ=R%C3%A8gle&browseCategory=fighter&browseSkip=100&lang=fr-CA");
		cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["q"] == "Règle", "The distinct return origin did not validate."));
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-more a").Any(a => Path(a) == "/wiki/main/related"), "Related card has not loaded."));

		var links = new[] { cut.Find("#guide"), cut.Find(".wiki-catlinks a"), cut.FindAll(".wiki-more a").Single(a => Path(a) == "/wiki/main/related") }
			.Concat(cut.FindAll(".wiki-lang-chips a")).ToArray();
		foreach (var link in links)
		{
			await Assert.That(Query(link)["browseCollection"]).IsEqualTo("package:reference");
			await Assert.That(Query(link)["browseQ"]).IsEqualTo("Règle");
			await Assert.That(Query(link)["browseCategory"]).IsEqualTo("fighter");
			await Assert.That(Query(link)["browseSkip"]).IsEqualTo("100");
			await Assert.That(Query(link)["membersCollection"]).IsNull();
		}
		await Assert.That(Query(ReturnLink(cut))["q"]).IsEqualTo("Règle");
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SameUriAuthenticationChangeClearsAuthorizedReturnOrOwnerWhileRevalidationIsPending(bool withReturn)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		auth.IsLoggedIn.Returns(true);
		auth.AccountSessionToken.Returns("first-character-session");
		_nav.NavigateTo(withReturn ? "/wiki/main/intro?" + Origin : "/wiki/main/intro?lang=fr-CA");
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back").Any(), "Initial authorized navigation did not render."));
		var originalUri = _nav.Uri;
		var before = _api.NavigationRequests.Count;
		var denied = new TaskCompletionSource<HttpResponseMessage>();
		_api.PendingNavigation = denied.Task;
		_api.RefuseAllNavigation = true;
		auth.AccountSessionToken.Returns("second-character-session");
		try
		{
			await cut.InvokeAsync(() => auth.AuthStateChanged += Raise.Event<Action>());
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count > before, "The same URI reused navigation authorized for the previous credential."));
			await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("Article owner");
			await Assert.That(cut.Markup).DoesNotContain("Reference library");
			await Assert.That(Query(cut.Find("#guide"))["browseCollection"]).IsNull();
			_api.PendingNavigation = null;
			await cut.InvokeAsync(() => CompletePending(denied, new HttpResponseMessage(HttpStatusCode.Forbidden)));
			await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
			await Assert.That(_nav.Uri).IsEqualTo(originalUri);
			await Assert.That(_api.ArticleRequests.Count).IsEqualTo(2);
		}
		finally { await cut.InvokeAsync(() => CompletePending(denied, new HttpResponseMessage(HttpStatusCode.Forbidden))); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OldCredentialNavigationCannotPublishAfterTheNewCredentialWasDeniedAtTheSameUri(bool characterChange)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		auth.IsLoggedIn.Returns(true);
		auth.AccountSessionToken.Returns("first-character-session");
		var old = new TaskCompletionSource<HttpResponseMessage>();
		_api.PendingReference = old.Task;
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var originalUri = _nav.Uri;
		var cut = RenderRoute(this);
		try
		{
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count == 1, "Old credential navigation did not start."));
			_api.PendingReference = null;
			_api.RefuseAllNavigation = true;
			auth.AccountSessionToken.Returns("second-character-session");
			await cut.InvokeAsync(() => RaiseReaderChanged(auth, characterChange));
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count >= 3, "New credential was not checked for both return and owner fallback."));
			await cut.InvokeAsync(() => CompletePending(old, JourneyApi.Navigation("package:reference")));

			await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
			await Assert.That(Query(cut.Find("#guide"))["browseCollection"]).IsNull();
			await Assert.That(cut.Markup).DoesNotContain("Reference library");
			await Assert.That(_nav.Uri).IsEqualTo(originalUri);
		}
		finally { await cut.InvokeAsync(() => CompletePending(old, new HttpResponseMessage(HttpStatusCode.Forbidden))); }
	}

	[Test]
	public async Task NewReaderAtTheSameUriLosesThePreviouslyLoadedPrivateArticleWhenItsReadIsDenied()
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		auth.IsLoggedIn.Returns(true);
		auth.AccountSessionToken.Returns("author-character-session");
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		var originalUri = _nav.Uri;
		_api.RefuseArticle = true;
		auth.AccountSessionToken.Returns("other-character-session");

		await cut.InvokeAsync(() => auth.AuthStateChanged += Raise.Event<Action>());
		cut.WaitForAssertion(() => Require(_api.ArticleRequests.Count == 2, "The new reader reused the previous reader's article body."));
		cut.WaitForAssertion(() => Require(!cut.Markup.Contains("Introduction body", StringComparison.Ordinal), "The denied reader retained the previous reader's private body."));

		await Assert.That(cut.FindAll(".wiki-article-body")).IsEmpty();
		await Assert.That(_nav.Uri).IsEqualTo(originalUri);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OldReaderArticleResponseCannotPublishAfterTheNewReaderWasDeniedAtTheSameUri(bool characterChange)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		auth.IsLoggedIn.Returns(true);
		auth.AccountSessionToken.Returns("author-character-session");
		var old = new TaskCompletionSource<HttpResponseMessage>();
		_api.PendingArticle = old.Task;
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var originalUri = _nav.Uri;
		var cut = RenderRoute(this);
		try
		{
			cut.WaitForAssertion(() => Require(_api.ArticleRequests.Count == 1, "The old reader's article request did not start."));
			_api.PendingArticle = null;
			_api.RefuseArticle = true;
			auth.AccountSessionToken.Returns("other-character-session");
			await cut.InvokeAsync(() => RaiseReaderChanged(auth, characterChange));
			cut.WaitForAssertion(() => Require(_api.ArticleRequests.Count == 2, "The new reader did not reauthorize the article body."));
			await cut.InvokeAsync(() => CompletePending(old, JourneyApi.Article("intro")));

			await Assert.That(cut.Markup).DoesNotContain("Introduction body");
			await Assert.That(cut.FindAll(".wiki-article-body")).IsEmpty();
			await Assert.That(_nav.Uri).IsEqualTo(originalUri);
		}
		finally { await cut.InvokeAsync(() => CompletePending(old, new HttpResponseMessage(HttpStatusCode.NotFound))); }
	}

	[Test]
	public async Task RealCharacterSwitchReauthorizesTheCompletedPrivateBodyAtTheSameUri()
	{
		using var context = new JourneyContext();
		var api = new JourneyApi { AuthorizeBySession = true };
		var auth = Install(context, api, realAccount: true)!;
		await auth.InitAsync();
		auth.SetActiveCharacter(new AccountAuthService.CharacterSummary(1, 1L, "Author", "", IsActing: true));
		var nav = context.Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki/main/intro?" + Origin);
		var originalUri = nav.Uri;
		var cut = RenderRoute(context);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back-results").Any(), "The author's authorized return did not render."));

		var result = await auth.SwitchCharacterAsync(new AccountAuthService.CharacterSummary(2, 2L, "Other reader", ""));
		await Assert.That(result is string ott && ott == "switch-ott").IsTrue();
		await Assert.That(auth.AccountSessionToken).IsEqualTo("second-character-session");
		await Assert.That(api.SwitchTokens.Single()).IsEqualTo("first-character-session");
		cut.WaitForAssertion(() => Require(api.ArticleTokens.Contains("second-character-session"), "The real character-switch producer did not trigger a current-reader article request."));
		cut.WaitForAssertion(() => Require(!cut.Markup.Contains("Introduction body", StringComparison.Ordinal), "A real switch retained the previous character's private body."));
		await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
		await Assert.That(nav.Uri).IsEqualTo(originalUri);
	}

	[Test]
	public async Task RealCharacterSwitchReauthorizesCompletedStandaloneOwnerNavigationAtTheSameUri()
	{
		using var context = new JourneyContext();
		var api = new JourneyApi { AuthorizeBySession = true };
		var auth = Install(context, api, realAccount: true)!;
		await auth.InitAsync();
		auth.SetActiveCharacter(new AccountAuthService.CharacterSummary(1, 1L, "Author", "", IsActing: true));
		var nav = context.Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki/main/intro?lang=fr-CA");
		var originalUri = nav.Uri;
		var cut = context.Render<WikiDisplay>(parameters => parameters
			.Add(display => display.Slug, "intro")
			.Add(display => display.Namespace, "main")
			.Add(display => display.Locale, "fr-CA")
			.Add(display => display.Article, new WikiArticle("Public article", "Public content", null, "<p>Public content</p>") { Id = "intro", Slug = "intro", Locale = "en" })
			.Add(display => display.ActivateEditMode, () => Task.CompletedTask));
		cut.WaitForAssertion(() => Require(cut.Markup.Contains("Article owner", StringComparison.Ordinal), "The initial authorized owner navigation did not render."));

		var result = await auth.SwitchCharacterAsync(new AccountAuthService.CharacterSummary(2, 2L, "Other reader", ""));
		await Assert.That(result is string ott && ott == "switch-ott").IsTrue();
		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
		cut.WaitForAssertion(() => Require(api.NavigationTokens.Contains("second-character-session"), "The real switch did not invalidate standalone navigation."));
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back").Count == 0, "The new reader retained authorized owner chrome."));
		await Assert.That(cut.Markup).DoesNotContain("Article owner");
		await Assert.That(nav.Uri).IsEqualTo(originalUri);
	}

	[Test]
	[Arguments("{}")]
	[Arguments("{\"id\":\"intro\",\"slug\":\"intro\",\"title\":\"Introduction\",\"renderedHtml\":\"<p>body</p>\"}")]
	[Arguments("{\"collection\":\"game\",\"total\":0,\"collections\":null,\"categories\":[]}")]
	[Arguments("{\"collection\":\"game\",\"total\":0,\"collections\":[],\"categories\":null}")]
	[Arguments("{\"collection\":\"\",\"total\":0,\"collections\":[],\"categories\":[]}")]
	[Arguments("{\"collection\":\"game\",\"total\":-1,\"collections\":[],\"categories\":[]}")]
	[Arguments("{\"collection\":\"game\",\"total\":0,\"collections\":[null],\"categories\":[]}")]
	[Arguments("{\"collection\":\"game\",\"total\":1,\"collections\":[],\"categories\":[{\"key\":\"guides\",\"name\":null,\"pages\":1,\"hasPage\":true}]}")]
	public async Task StructurallyInvalidNavigationIsAnHonestResponseFailure(string body)
	{
		_api.NavigationJson = body;
		var result = await Services.GetRequiredService<WikiService>().GetNavigationAsync(collection: "package:reference", lang: "fr-CA");

		await Assert.That(result is ApiFailure { Kind: ApiFailureKind.Unexpected }).IsTrue();
	}

	[Test]
	public async Task RevokedReturnScopeClearsItsPriorActionsAndLabelsOnQueryRefresh()
	{
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["q"] == "Règle & C++", "Initial validated return missing."));
		_api.RefuseReference = true;
		_nav.NavigateTo("/wiki/main/intro?" + Origin.Replace("browseSkip=100", "browseSkip=150", StringComparison.Ordinal));
		cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count(uri => Query(uri)["collection"] == "package:reference") == 2, "Return scope was not revalidated."));
		cut.WaitForAssertion(() => Require(!cut.FindAll("a").Any(a => Query(a)["browseCollection"] == "package:reference" || Path(a) == "/wiki/collections/package%3Areference"), "Revoked return action remained visible."));

		await Assert.That(cut.Markup).DoesNotContain("Reference library");
		await Assert.That(_api.ArticleRequests.Count).IsEqualTo(1);
	}

	[Test]
	public async Task LateReturnValidationCannotReplaceTheNewerScope()
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		_api.PendingReference = pending.Task;
		_nav.NavigateTo("/wiki/main/intro?" + Origin);
		var cut = RenderRoute(this);
		try
		{
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Any(uri => Query(uri)["collection"] == "package:reference"), "Initial validation did not start."));
			_nav.NavigateTo("/wiki/main/intro?browseCollection=game&browseQ=New%20search&membersCollection=package%3Aowner&lang=fr-CA");
			cut.WaitForAssertion(() => Require(Query(ReturnLink(cut))["q"] == "New search", "Newer return scope did not render."));
			await cut.InvokeAsync(() => pending.SetResult(JourneyApi.Navigation("package:reference")));

			await Assert.That(Path(ReturnLink(cut))).IsEqualTo("/wiki/collections/game");
			await Assert.That(Query(ReturnLink(cut))["q"]).IsEqualTo("New search");
			await Assert.That(Query(cut.Find("#guide"))["membersCollection"]).IsEqualTo("package:owner");
			await Assert.That(cut.Markup).DoesNotContain("Reference library");
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.NotFound))); }
	}

	private static AccountAuthService? Install(TrackingBunitContext context, JourneyApi api, bool realAccount = false)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		AccountAuthService? account = null;
		HttpMessageHandler handler = api;
		if (realAccount)
		{
			context.JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
			context.JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("first-character-session");
			context.JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("same-account");
			account = new AccountAuthService(factory, context.JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
			context.Services.AddSingleton(account).AddSingleton<IAccountAuthState>(account);
			handler = new AccountSessionBearerHandler(account) { InnerHandler = api };
		}
		var client = context.Track(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
		factory.CreateClient("api").Returns(client);
		context.Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<WikiMarkdigPipeline>()
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		context.AddAuthorization().SetAuthorized("reader");
		context.JSInterop.Mode = JSRuntimeMode.Loose;
		return account;
	}

	[Test]
	[Arguments("generated")]
	[Arguments("fragment")]
	[Arguments("key-case")]
	public async Task FollowingTocRetainsAuthorizedJourneyWithoutAnotherNavigationRead(string variant)
	{
		const string original = "/wiki/main/intro?" + Origin + "&expectedPageId=intro&extra=a%26b";
		_nav.NavigateTo(original);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back-results").Count == 1, "Initial return was not authorized."));
		var back = cut.Find(".wiki-back-results").GetAttribute("href");
		var reads = _api.NavigationRequests.Count;
		var articles = _api.ArticleRequests.Count;
		var href = cut.FindAll(".wiki-toc a").First(a => Address(a).Fragment == "#costs").GetAttribute("href")!;
		if (variant == "fragment") href = original + "#costs";
		if (variant == "key-case")
			href = href.Replace("lang=fr-CA", "LANG=fr-CA", StringComparison.Ordinal)
				.Replace("extra=a%26b", "EXTRA=a%26b", StringComparison.Ordinal);
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_api.NavigationGate = pending.Task;
		try
		{
			await cut.InvokeAsync(() => _nav.NavigateTo(href));
			cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back-results").Count == 1, "TOC navigation discarded the authorized return."));
			await Assert.That(cut.Find(".wiki-back-results").GetAttribute("href")).IsEqualTo(back);
			await Assert.That(_api.NavigationRequests.Count).IsEqualTo(reads);
			await Assert.That(_api.ArticleRequests.Count).IsEqualTo(articles);
			await AssertJourney(cut.Find("#guide"), "fr-CA");
			foreach (var toc in cut.FindAll(".wiki-toc a"))
			{
				await AssertJourney(toc, "fr-CA");
				await Assert.That(Query(toc)["expectedPageId"]).IsEqualTo("intro");
			}
			await Assert.That(Query(cut.Find("#guide"))["expectedPageId"]).IsNull();
			await cut.InvokeAsync(() => _nav.NavigateTo(original + "#wiki-top"));
			await cut.InvokeAsync(() => _nav.NavigateTo(original));
			await Assert.That(cut.Find(".wiki-back-results").GetAttribute("href")).IsEqualTo(back);
			await Assert.That(_api.NavigationRequests.Count).IsEqualTo(reads);
		}
		finally { pending.TrySetResult(false); }
	}

	[Test]
	[Arguments("extra=a%26b", "extra=A%26b")]
	[Arguments("extra=a%26b", "unknown=a%26b")]
	[Arguments("lang=fr-CA", "lang=de")]
	[Arguments("browseQ=R%C3%A8gle%20%26%20C%2B%2B", "browseQ=Changed")]
	[Arguments("browseSkip=100", "browseSkip=150")]
	[Arguments("membersCollection=game", "membersCollection=all")]
	[Arguments("membersSkip=50", "membersSkip=100")]
	public async Task ChangedQueryValueStillClearsAndReauthorizesJourney(string before, string after)
	{
		const string original = "/wiki/main/intro?" + Origin + "&expectedPageId=intro&extra=a%26b";
		_nav.NavigateTo(original);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back-results").Count == 1, "Initial return was not authorized."));
		var reads = _api.NavigationRequests.Count;
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_api.NavigationGate = pending.Task;
		try
		{
			await cut.InvokeAsync(() => _nav.NavigateTo(original.Replace(before, after, StringComparison.Ordinal) + "#costs"));
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count > reads, "A real context change reused old authorization."));
			await Assert.That(cut.FindAll(".wiki-back-results")).IsEmpty();
			await cut.InvokeAsync(() => pending.TrySetResult(false));
			await Assert.That(cut.FindAll(".wiki-back-results")).IsEmpty();
		}
		finally { pending.TrySetResult(false); }
	}

	[Test]
	public async Task ChangedExpectedIdentityStartsConstrainedReadAndClearsPreviousJourney()
	{
		const string original = "/wiki/main/intro?" + Origin + "&expectedPageId=intro";
		_nav.NavigateTo(original);
		var cut = RenderRoute(this);
		WaitForBody(cut, "Introduction body");
		cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back-results").Count == 1, "Initial return was not authorized."));
		var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_api.PendingArticle = pending.Task;
		try
		{
			await cut.InvokeAsync(() => _nav.NavigateTo(original.Replace("expectedPageId=intro", "expectedPageId=other", StringComparison.Ordinal) + "#costs"));
			cut.WaitForAssertion(() => Require(_api.ArticleRequests.Any(uri => Query(uri)["expectedPageId"] == "other"), "Changed identity did not start a constrained article read."));
			await Assert.That(cut.FindAll(".wiki-back-results, .wiki-article-body")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("Introduction body");
			await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.NotFound)));
			await Assert.That(cut.Markup).DoesNotContain("Introduction body");
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.NotFound))); }
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task InitialPendingNavigationKeepsSameArticleHintsWithoutAuthorizingReturn(bool allowed)
	{
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_api.NavigationGate = pending.Task;
		_api.IntroHtmlOverride = JourneyApi.IntroHtml + """
			<a id="pending-self" href="/wiki/main/intro#costs">Same article costs</a>
			""";
		_nav.NavigateTo("/wiki/main/intro?" + Origin + "&expectedPageId=intro");
		var cut = RenderRoute(this);
		try
		{
			WaitForBody(cut, "Introduction body");
			cut.WaitForAssertion(() => Require(_api.NavigationRequests.Count > 0, "The initial navigation authorization was not started."));
			await Assert.That(pending.Task.IsCompleted).IsFalse();
			await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("Reference library");
			await Assert.That(cut.Markup).DoesNotContain("Article owner");

			foreach (var toc in cut.FindAll(".wiki-toc a"))
			{
				await AssertJourney(toc, "fr-CA");
				await Assert.That(Query(toc)["expectedPageId"]).IsEqualTo("intro");
			}
			var locale = cut.Find(".wiki-lang-chips a[lang='de']");
			await AssertJourney(locale, "de");
			await Assert.That(Query(locale)["expectedPageId"]).IsEqualTo("intro");
			await AssertJourney(cut.Find("#pending-self"), "fr-CA");
			await Assert.That(Query(cut.Find("#pending-self"))["expectedPageId"]).IsEqualTo("intro");
			// Other destinations do not inherit an origin that has not yet been authorized.
			await Assert.That(Query(cut.Find("#guide"))["browseCollection"]).IsNull();
			await Assert.That(Query(cut.Find("#guide"))["expectedPageId"]).IsNull();

			var selected = cut.FindAll(".wiki-toc a").First(a => Address(a).Fragment == "#costs");
			var target = selected.GetAttribute("href")!;
			var navigationReads = _api.NavigationRequests.Count;
			await cut.InvokeAsync(() => _nav.NavigateTo(target));
			WaitForBody(cut, "Introduction body");
			await Assert.That(_api.NavigationRequests.Count).IsEqualTo(navigationReads);
			await Assert.That(Query(new Uri(_nav.Uri))["browseCollection"]).IsEqualTo("package:reference");
			await Assert.That(Query(new Uri(_nav.Uri))["browseQ"]).IsEqualTo("Règle & C++");
			await Assert.That(Query(new Uri(_nav.Uri))["expectedPageId"]).IsEqualTo("intro");
			await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
			await AssertJourney(cut.Find("#pending-self"), "fr-CA");

			await cut.InvokeAsync(() => pending.TrySetResult(allowed));
			if (allowed)
			{
				cut.WaitForAssertion(() => Require(cut.FindAll(".wiki-back-results").Count == 1, "Authorization did not restore the return after same-article navigation."));
				var back = cut.Find(".wiki-back-results");
				await Assert.That(Path(back)).IsEqualTo("/wiki/collections/package%3Areference");
				await Assert.That(Query(back)["q"]).IsEqualTo("Règle & C++");
				await Assert.That(Query(back)["category"]).IsEqualTo("fighter & feats");
				await Assert.That(Query(back)["skip"]).IsEqualTo("100");
				await Assert.That(Query(back)["lang"]).IsEqualTo("fr-CA");
				await AssertJourney(cut.Find("#pending-self"), "fr-CA");
				await AssertJourney(cut.Find(".wiki-lang-chips a[lang='de']"), "de");
				foreach (var toc in cut.FindAll(".wiki-toc a")) await AssertJourney(toc, "fr-CA");
			}
			else
			{
				// Wait for a completed denial to remove the hints, not merely for the
				// already-absent return link while the authorization was pending.
				cut.WaitForAssertion(() => Require(Query(cut.Find("#pending-self"))["browseCollection"] is null
					&& cut.FindAll(".wiki-toc a, .wiki-lang-chips a").All(a => Query(a)["browseCollection"] is null),
					"Completed denial retained pending browse hints."));
				await Assert.That(cut.FindAll(".wiki-back")).IsEmpty();
				await Assert.That(cut.Markup).DoesNotContain("Reference library");
				await Assert.That(cut.Markup).DoesNotContain("Article owner");
				await Assert.That(Query(cut.Find("#guide"))["browseCollection"]).IsNull();
			}
			await Assert.That(Query(cut.Find("#pending-self"))["expectedPageId"]).IsEqualTo("intro");
			await Assert.That(Query(cut.Find(".wiki-lang-chips a[lang='de']"))["expectedPageId"]).IsEqualTo("intro");
			foreach (var toc in cut.FindAll(".wiki-toc a"))
				await Assert.That(Query(toc)["expectedPageId"]).IsEqualTo("intro");
			await Assert.That(Query(cut.Find("#guide"))["expectedPageId"]).IsNull();
		}
		finally { pending.TrySetResult(false); }
	}

	private static void RaiseReaderChanged(IAccountAuthState auth, bool characterChange)
	{
		if (characterChange)
		{
			auth.ActiveCharacter.Returns(new AccountAuthService.CharacterSummary(2, 2L, "Other reader", ""));
			auth.ActiveCharacterChanged += Raise.Event<Action>();
		}
		else auth.AuthStateChanged += Raise.Event<Action>();
	}

	// Keep routing/query binding real while leaving the shell/sidebar to their own suites.
	private static IRenderedComponent<Router> RenderRoute(TrackingBunitContext context) => context.Render<Router>(parameters => parameters
		.Add(p => p.AppAssembly, typeof(WikiPage).Assembly)
		.Add(p => p.Found, (RenderFragment<RouteData>)(route => builder =>
		{
			builder.OpenComponent(0, route.PageType);
			foreach (var value in route.RouteValues) builder.AddAttribute(1, value.Key, value.Value);
			builder.CloseComponent();
		})));

	private static void WaitForBody(IRenderedComponent<Router> cut, string body) =>
		cut.WaitForAssertion(() => Require(cut.Find(".wiki-article-body").TextContent.Contains(body, StringComparison.Ordinal), "Article body not loaded."));

	private static IElement ReturnLink(IRenderedComponent<Router> cut) => cut.FindAll("a").FirstOrDefault(a => a.Closest(".wiki-article-body") is null && Path(a).StartsWith("/wiki/collections/", StringComparison.Ordinal) && Query(a)["q"] is not null)
		?? cut.FindAll("a").First(a => a.Closest(".wiki-article-body") is null && Path(a).StartsWith("/wiki/collections/", StringComparison.Ordinal) && Query(a)["category"] is null && Query(a)["q"] is null);

	private static async Task AssertJourney(IElement link, string locale)
	{
		var query = Query(link);
		await Assert.That(query["browseCollection"]).IsEqualTo("package:reference");
		await Assert.That(query["browseQ"]).IsEqualTo("Règle & C++");
		await Assert.That(query["browseCategory"]).IsEqualTo("fighter & feats");
		await Assert.That(query["browseSkip"]).IsEqualTo("100");
		await Assert.That(query["membersCollection"]).IsEqualTo("game");
		await Assert.That(query["membersSkip"]).IsEqualTo("50");
		await Assert.That(query["lang"]).IsEqualTo(locale);
		await Assert.That(query.GetValues("lang")!.Length).IsEqualTo(1);
	}

	private static Uri Address(IElement link) => new(new Uri("http://localhost/"), link.GetAttribute("href") ?? string.Empty);
	private static string Path(IElement link) => Address(link).AbsolutePath;
	private static NameValueCollection Query(IElement link) => Query(Address(link));
	private static NameValueCollection Query(Uri uri) => System.Web.HttpUtility.ParseQueryString(uri.Query);
	// A successful completion transfers ownership to the HTTP consumer; a rejected retry does not.
	private static void CompletePending(TaskCompletionSource<HttpResponseMessage> pending, HttpResponseMessage response)
	{
		if (!pending.TrySetResult(response)) response.Dispose();
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class JourneyContext : TrackingBunitContext
	{
	}

	/// <summary>The boundary is HTTP: authorization failures and canonical existence refs remain observable.</summary>
	private sealed class JourneyApi : HttpMessageHandler
	{
		public const string IntroHtml = """
			<p>Introduction body</p><h2 id="costs">Costs</h2>
			<a id="guide" href="/wiki/main/guide">Read guide</a>
			<a id="queried" class="authored-link" href="/wiki/main/guide?extra=a%26b&amp;lang=de#costs">Read <em>carefully</em></a>
			<a id="missing" href="/wiki/main/missing?extra=a%26b#detail"><em>missing label</em></a>
			<a id="encoded" href="/wiki/main/salt&amp;pepper?extra=a%26b">Salt &amp; pepper</a>
			<a id="local" href="#costs">Local costs</a>
			<a id="external" href="https://outside.invalid/wiki/main/guide?lang=de#costs">External</a>
			<a id="character" href="/character/Ada">Ada</a>
			<a id="download" href="/api/wiki-assets/rules.pdf" download>Download</a>
			<a id="editor" href="/wiki/main/guide/edit?lang=de">Editor</a>
			<a id="collection" href="/wiki/collections/all?q=local">Collection</a>
			""";

		public List<Uri> ArticleRequests { get; } = [];
		public List<Uri> NavigationRequests { get; } = [];
		public List<string[]> ExistenceBatches { get; } = [];
		public List<string?> ArticleTokens { get; } = [];
		public List<string?> NavigationTokens { get; } = [];
		public List<string?> SwitchTokens { get; } = [];
		public bool AuthorizeBySession { get; set; }
		public bool RefuseReference { get; set; }
		public bool RefuseAllNavigation { get; set; }
		public bool RefuseArticle { get; set; }
		public Task<HttpResponseMessage>? PendingReference { get; set; }
		public Task<HttpResponseMessage>? PendingNavigation { get; set; }
		public Task<bool>? NavigationGate { get; set; }
		public Task<HttpResponseMessage>? PendingArticle { get; set; }
		public string? NavigationJson { get; set; }
		public string? IntroHtmlOverride { get; set; }

		public static HttpResponseMessage Navigation(string collection, bool referenceVisible = true) => Json(new WikiNavigationDto(collection, 151,
			referenceVisible
				? [new WikiCollectionSummaryDto("package:reference", "Reference library", 151), new WikiCollectionSummaryDto("package:owner", "Article owner", 3)]
				: [new WikiCollectionSummaryDto("package:owner", "Article owner", 3)],
			[new WikiCategorySummaryDto("guides", "Guides", 3, true, false)]));

		public static HttpResponseMessage Article(string slug, bool privateArticle = false, string? introHtml = null) => Json(new WikiPageDto(slug, slug, slug == "intro" ? "Introduction" : "Guide", "main", "Source stays unchanged", slug == "intro" ? introHtml ?? IntroHtml : "<p>Guide body</p>", "body",
			DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, privateArticle, 1, ["guides"], !privateArticle)
		{
			Locale = "en", RequestedLocale = "en", IsFallback = true, AvailableLocales = ["en", "de"],
			Access = new WikiAccessDto(true, false, false, false)
		});

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			var token = request.Headers.Authorization?.Parameter;
			if (uri.AbsolutePath == "/api/account/session")
				return Json(new { username = "same-account", mustChangePassword = false, role = "Player", permissions = Array.Empty<string>() });
			if (uri.AbsolutePath == "/api/auth/switch-character" && request.Method == HttpMethod.Post)
			{
				SwitchTokens.Add(token);
				return Json(new { ott = "switch-ott", expiresIn = 60, accountSessionToken = "second-character-session" });
			}
			if (uri.AbsolutePath == "/api/wiki/navigation")
			{
				NavigationRequests.Add(uri);
				NavigationTokens.Add(token);
				if (NavigationGate is { } gate && !await gate) return new HttpResponseMessage(HttpStatusCode.Forbidden);
				if (AuthorizeBySession && token != "first-character-session") return new HttpResponseMessage(HttpStatusCode.Forbidden);
				if (PendingNavigation is { } navigation) return await navigation;
				if (NavigationJson is { } body) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
				var query = Query(uri);
				var collection = query["collection"];
				if (collection == "package:reference" && PendingReference is { } pending) return await pending;
				if (RefuseAllNavigation) return new HttpResponseMessage(HttpStatusCode.Forbidden);
				if (collection == "package:reference" && RefuseReference) return new HttpResponseMessage(HttpStatusCode.NotFound);
				return collection is null or "game" or "all" or "package:reference" or "package:owner"
					? Navigation(collection ?? "package:owner", !RefuseReference) : new HttpResponseMessage(HttpStatusCode.NotFound);
			}
			if (uri.AbsolutePath.StartsWith("/api/wiki/ns/main/", StringComparison.Ordinal))
			{
				ArticleRequests.Add(uri);
				ArticleTokens.Add(token);
				if (AuthorizeBySession && token != "first-character-session") return new HttpResponseMessage(HttpStatusCode.NotFound);
				if (PendingArticle is { } article) return await article;
				if (RefuseArticle) return new HttpResponseMessage(HttpStatusCode.NotFound);
				var slug = Uri.UnescapeDataString(uri.AbsolutePath["/api/wiki/ns/main/".Length..]);
				return Article(slug, privateArticle: AuthorizeBySession, introHtml: IntroHtmlOverride);
			}
			if (uri.AbsolutePath == "/api/wiki/exists")
			{
				var content = await request.Content!.ReadFromJsonAsync<ExistsRequest>(cancellationToken: cancellationToken);
				var refs = content?.Refs ?? [];
				ExistenceBatches.Add(refs);
				return Json(refs.ToDictionary(reference => reference, reference => reference != "main/missing"));
			}
			if (uri.AbsolutePath == "/api/wiki/category-names") return Json(new Dictionary<string, string> { ["guides"] = "Guides" });
			if (uri.AbsolutePath == "/api/wiki/category/guides")
				return Json(new[] { new WikiPageSummaryDto("related", "related", "Related article", "main", DateTimeOffset.UnixEpoch, false, 1, ["guides"], true, "en", true, null, null) });
			return new HttpResponseMessage(HttpStatusCode.NotFound);
		}

		private sealed record ExistsRequest(string[] Refs);
		private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
	}
}
