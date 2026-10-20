using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// The D1 wiki page sidebar (board 21): "Wiki · namespace · N pages", search, Browse (home, recent
/// changes), Categories with covers and counts, and a New page button for those who may create.
/// </summary>
public class WikiSidebarTests : TrackingBunitContext
{
	private readonly BunitAuthorizationContext _auth;
	private readonly BunitNavigationManager _nav;
	private readonly WikiApiFake _api;

	public WikiSidebarTests()
	{
		(_api, _auth) = WikiApiFake.Install(this);
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	private IRenderedComponent<WikiSidebar> RenderAt(string path)
	{
		_nav.NavigateTo(path);
		var cut = Render<WikiSidebar>();
		cut.WaitForAssertion(() => cut.Find(".wiki-side-cats a.kit-row"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task ImportedCollectionsStayOneEntryAndCountsAreNotCappedAt500()
	{
		_api.Collections.Add(new("package:pf2e-remaster", "Pathfinder 2e", 4711));
		_api.NavigationTotal = 6001;
		_api.Pinned.Remove("lore");
		var cut = RenderAt("/wiki");
		await Assert.That(cut.FindAll(".wiki-side-collections a").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".wiki-side-collections a").TextContent).Contains("4711");
		await Assert.That(cut.Find(".kit-side-sub").TextContent.Replace(",", "")).Contains("6001");
		await Assert.That(cut.FindAll(".wiki-side-cats a[href*='category=lore']")).IsEmpty();
	}

	[Test]
	public async Task AllCollectionsHeaderDoesNotMislabelTheGlobalTotalAsGameWiki()
	{
		_api.NavigationTotal = 4715;
		var cut = RenderAt("/wiki/collections/all");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).StartsWith("All collections");
		await Assert.That(cut.Find(".kit-side-sub").TextContent.Replace(",", "")).Contains("4715");
	}

	[Test]
	public async Task NavigationDoesNotSampleTheFirst500Pages()
	{
		RenderAt("/wiki");
		await Assert.That(_api.Requests.Any(path => path.Contains("take=500", StringComparison.Ordinal))).IsFalse()
			.Because("category counts and navigation must come from the complete authorized collection, not a partial page sample");
	}

	[Test]
	public async Task ListsNamedCategoriesWithCountsAndCovers()
	{
		var cut = RenderAt("/wiki");
		var rows = cut.FindAll(".wiki-side-cats .kit-row");
		await Assert.That(rows.Count).IsEqualTo(3)
			.Because("only pinned categories and the complete category directory belong in the sidebar");
		await Assert.That(rows[0].GetAttribute("href")).StartsWith("/wiki/category/guides?");
		await Assert.That(cut.Find(".wiki-side-members[href='/wiki/collections/game?category=guides']")).IsNotNull();
		await Assert.That(rows[0].QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("2");
		await Assert.That(rows[0].QuerySelector("img.kit-row-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/intro.jpg");
		await Assert.That(rows[1].GetAttribute("href")).StartsWith("/wiki/category/lore?");
		await Assert.That(cut.Find(".wiki-side-members[href='/wiki/collections/game?category=lore']")).IsNotNull();
		await Assert.That(rows.Any(r => r.TextContent.Contains("Uncategorized"))).IsFalse();
	}

	[Test]
	public async Task HeaderCountsThePages_AndBrowseLinksHomeAndRecent()
	{
		var cut = RenderAt("/wiki");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).StartsWith("Game wiki");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("4");
		await Assert.That(cut.Find(".wiki-side-browse a[href='/wiki']").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.Find(".wiki-side-browse a[href='/wiki/recent']")).IsNotNull();
	}

	[Test]
	[Arguments("/wiki/collections/game?category=lore")]
	[Arguments("/wiki/category/lore")]
	public async Task TheCurrentCategoryRowIsCurrent(string route)
	{
		var cut = RenderAt(route);
		await Assert.That(cut.Find(".wiki-side-cats a[href='/wiki/collections/game?category=lore']").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.FindAll(".wiki-side-browse a[aria-current]").Count).IsEqualTo(0);
	}

	[Test]
	public async Task SearchStaysInTheAuthorizedCollectionAndRetainsCategoryAndLocale()
	{
		_api.Collections.Add(new("package:reference", "Reference", 4711));
		var cut = RenderAt("/wiki/collections/package%3Areference?category=lore&skip=500&lang=fr");
		await cut.Find(".kit-side-search input").InputAsync("harbour");
		await cut.Find(".kit-side-search").SubmitAsync();
		var location = new Uri(_nav.Uri);
		var query = System.Web.HttpUtility.ParseQueryString(location.Query);
		await Assert.That(Uri.UnescapeDataString(location.AbsolutePath)).IsEqualTo("/wiki/collections/package:reference");
		await Assert.That(query["q"]).IsEqualTo("harbour");
		await Assert.That(query["category"]).IsEqualTo("lore");
		await Assert.That(query["lang"]).IsEqualTo("fr");
		await Assert.That(query["skip"]).IsNull();
	}

	[Test]
	public async Task CategoryLinksRetainSubmittedSearchAndLocaleWhileResettingTheOffset()
	{
		var cut = RenderAt("/wiki/collections/game?q=harbour&category=guides&skip=500&lang=fr");
		var href = cut.FindAll(".wiki-side-cats a").Single(link => link.GetAttribute("href")!.Contains("category=lore", StringComparison.Ordinal)).GetAttribute("href");
		var query = System.Web.HttpUtility.ParseQueryString(new Uri(new Uri(_nav.Uri), href).Query);
		await Assert.That(query["q"]).IsEqualTo("harbour");
		await Assert.That(query["category"]).IsEqualTo("lore");
		await Assert.That(query["lang"]).IsEqualTo("fr");
		await Assert.That(query["skip"]).IsNull();
		await Assert.That(query.GetValues("lang")!.Length).IsEqualTo(1);
	}

	[Test]
	public async Task SearchEncodesUnicodeAndQueryPunctuationWithoutAddingFields()
	{
		var cut = RenderAt("/wiki/collections/game?category=lore&lang=fr");
		await cut.Find(".kit-side-search input").InputAsync("  Étoile & C++ \"quoted\" #fragment  ");
		await cut.Find(".kit-side-search").SubmitAsync();
		var location = new Uri(_nav.Uri);
		var query = System.Web.HttpUtility.ParseQueryString(location.Query);
		await Assert.That(location.AbsolutePath).IsEqualTo("/wiki/collections/game");
		await Assert.That(query["q"]).IsEqualTo("Étoile & C++ \"quoted\" #fragment");
		await Assert.That(query["category"]).IsEqualTo("lore");
		await Assert.That(query["lang"]).IsEqualTo("fr");
		await Assert.That(query.Count).IsEqualTo(3);
		await Assert.That(location.Fragment).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task SearchFromDirectArticleUsesItsAuthorizedOwnerCollection()
	{
		_api.PageCollection = "package:reference";
		_api.Collections.Add(new("package:reference", "Reference", 4711));
		var cut = RenderAt("/wiki/main/intro?lang=fr&browseCollection=forged&returnUrl=https%3A%2F%2Fevil.example%2F");
		await cut.Find(".kit-side-search input").InputAsync("harbour");
		await cut.Find(".kit-side-search").SubmitAsync();
		var location = new Uri(_nav.Uri);
		var query = System.Web.HttpUtility.ParseQueryString(location.Query);
		await Assert.That(Uri.UnescapeDataString(location.AbsolutePath)).IsEqualTo("/wiki/collections/package:reference");
		await Assert.That(query["q"]).IsEqualTo("harbour");
		await Assert.That(query["lang"]).IsEqualTo("fr");
		await Assert.That(query["returnUrl"]).IsNull();
		await Assert.That(location.Host).IsEqualTo(new Uri(_nav.BaseUri).Host);
	}

	[Test]
	public async Task NewPage_OnlyForCreators_TakesATitleAndOpensTheEditor()
	{
		var reader = RenderAt("/wiki");
		await Assert.That(reader.FindAll(".wiki-side-new").Count).IsEqualTo(0);

		_auth.SetPolicies("wiki.create");
		var creator = RenderAt("/wiki");
		await creator.Find("button.wiki-side-new").ClickAsync();
		await creator.Find(".wiki-side-new-form input").InputAsync("Salt Market");
		await creator.Find(".wiki-side-new-form").SubmitAsync();
		await Assert.That(_nav.Uri).EndsWith("/wiki/main/salt_market/edit?title=Salt%20Market")
			.Because("the editor starts from the title the creator typed, not the slug made from it");

		await creator.Find("button.wiki-side-new").ClickAsync();
		await creator.Find(".wiki-side-new-form input").InputAsync("What? / Why#");
		await creator.Find(".wiki-side-new-form").SubmitAsync();
		await Assert.That(_nav.Uri).EndsWith("/wiki/main/what%3F_%2F_why%23/edit?title=What%3F%20%2F%20Why%23")
			.Because("a title with ?, / or # must not break the route");
	}

	[Test]
	public async Task Collapsed_NewPage_LinksToTheEditor()
	{
		_auth.SetPolicies("wiki.create");
		_nav.NavigateTo("/wiki");
		var cut = Render<WikiSidebar>(p => p.Add(x => x.Collapsed, true));
		cut.WaitForAssertion(() => cut.Find("a.kit-row--collapsed[href='/wiki/main/new-page/edit']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("button.kit-row--collapsed").Count(b => b.Closest(".wiki-side-cats") is null)).IsEqualTo(0)
			.Because("the strip has no room for the title form, so its New page is a link to the editor, not a toggle");
	}

	[Test]
	public async Task Collapsed_KeepsCategoryCoversOnly()
	{
		_nav.NavigateTo("/wiki");
		var cut = Render<WikiSidebar>(p => p.Add(x => x.Collapsed, true));
		cut.WaitForAssertion(() => cut.Find(".wiki-side-cats .kit-row--collapsed"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-side-search").Count).IsEqualTo(0);
	}
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SearchCannotUseThePreviousCollectionWhileTheCurrentDirectoryIsPending(bool allowed)
	{
		var handler = InstallDelayedDirectory();
		var cut = RenderAt("/wiki/collections/package%3Afirst?category=first&lang=fr");
		const string destination = "/wiki/collections/package%3Asecond?category=second&lang=de";
		_nav.NavigateTo(destination);
		cut.WaitForAssertion(() =>
		{
			if (!handler.SecondRequested) throw new InvalidOperationException("The second collection read was not started.");
		});
		var pendingRoute = _nav.Uri;
		try
		{
			await cut.Find(".kit-side-search input").InputAsync("harbour");
			await cut.Find(".kit-side-search").SubmitAsync();
			await Assert.That(_nav.Uri).IsEqualTo(pendingRoute)
				.Because("search has no current authorized scope until the destination directory finishes");
			await cut.InvokeAsync(() => handler.CompleteSecond(allowed));
			if (allowed)
			{
				cut.WaitForAssertion(() => cut.Find(".wiki-side-cats a[href*='category=second']"));
				await cut.Find(".kit-side-search input").InputAsync("harbour");
				await cut.Find(".kit-side-search").SubmitAsync();
				var location = new Uri(_nav.Uri);
				var query = System.Web.HttpUtility.ParseQueryString(location.Query);
				await Assert.That(Uri.UnescapeDataString(location.AbsolutePath)).IsEqualTo("/wiki/collections/package:second");
				await Assert.That(query["q"]).IsEqualTo("harbour");
				await Assert.That(query["category"]).IsEqualTo("second");
				await Assert.That(query["lang"]).IsEqualTo("de");
			}
			else
			{
				cut.WaitForAssertion(() => cut.Find("button.wiki-side-new"));
				await Assert.That(cut.FindAll(".wiki-side-collections a, .wiki-side-cats a")).IsEmpty();
				await cut.Find(".kit-side-search input").InputAsync("blocked");
				await cut.Find(".kit-side-search").SubmitAsync();
				await Assert.That(_nav.Uri).IsEqualTo(pendingRoute);
			}
		}
		finally { await cut.InvokeAsync(() => handler.CompleteSecond(false)); }
	}

	[Test]
	public async Task PendingAndRevokedDirectoriesDoNotLeavePreviousCollectionChromeActionable()
	{
		var handler = InstallDelayedDirectory();
		var cut = RenderAt("/wiki/collections/package%3Afirst?lang=fr");
		await Assert.That(cut.Find(".wiki-side-cats").TextContent).Contains("First private category");
		_nav.NavigateTo("/wiki/collections/package%3Asecond?category=second&lang=de");
		cut.WaitForAssertion(() =>
		{
			if (!handler.SecondRequested) throw new InvalidOperationException("The second collection read was not started.");
		});
		try
		{
			await Assert.That(cut.FindAll(".wiki-side-collections a, .wiki-side-cats a")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("First private category");
			await Assert.That(cut.Markup).DoesNotContain("First private collection");
			await cut.InvokeAsync(() => handler.CompleteSecond(false));
			cut.WaitForAssertion(() => cut.Find("button.wiki-side-new"));
			await Assert.That(cut.FindAll(".wiki-side-collections a, .wiki-side-cats a")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("First private category");
			await Assert.That(cut.Markup).DoesNotContain("First private collection");
		}
		finally { await cut.InvokeAsync(() => handler.CompleteSecond(false)); }
	}

	private const string ContextRoute = "/wiki/main/intro?browseCollection=package%3Areference&browseQ=R%C3%A8gle%20%26%20C%2B%2B&browseCategory=guides&browseSkip=100&membersCollection=game&membersSkip=50&lang=fr-CA&expectedPageId=intro&extra=a%26b";
	private const string EquivalentContextRoute = "/wiki/main/intro?EXTRA=a%26b&EXPECTEDPAGEID=intro&lang=ignored&LANG=fr-CA&membersSkip=50&membersCollection=game&browseSkip=100&browseCategory=guides&browseQ=R%C3%A8gle+%26+C%2B%2B&browseCollection=package%3Areference#costs";

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task EquivalentFragmentContextKeepsDirectoryAndSearchActionable(bool reorder)
	{
		var handler = InstallContextDirectory();
		var cut = RenderAt(ContextRoute);
		var count = handler.Requests.Count;
		var header = cut.Find(".kit-side-sub").TextContent;
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Pending = pending.Task;
		try
		{
			await cut.InvokeAsync(() => _nav.NavigateTo(reorder ? EquivalentContextRoute : ContextRoute + "#costs"));
			await Assert.That(handler.Requests.Count).IsEqualTo(count);
			await Assert.That(cut.Find(".kit-side-sub").TextContent).IsEqualTo(header);
			await cut.Find(".kit-side-search input").InputAsync("harbour");
			await cut.Find(".kit-side-search").SubmitAsync();
			var location = new Uri(_nav.Uri);
			var query = System.Web.HttpUtility.ParseQueryString(location.Query);
			await Assert.That(Uri.UnescapeDataString(location.AbsolutePath)).IsEqualTo("/wiki/collections/package:reference");
			await Assert.That(query["q"]).IsEqualTo("harbour");
			await Assert.That(query["lang"]).IsEqualTo("fr-CA");
		}
		finally { pending.TrySetResult(false); }
	}

	[Test]
	public async Task PendingDirectoryFinishesAcrossEquivalentFragmentWithoutDuplicateRead()
	{
		var handler = InstallContextDirectory();
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Pending = pending.Task;
		_nav.NavigateTo(ContextRoute);
		var cut = Render<WikiSidebar>();
		try
		{
			cut.WaitForAssertion(() => RequireContext(handler.Requests.Count == 1, "Initial owner read did not start."));
			await cut.InvokeAsync(() => _nav.NavigateTo(EquivalentContextRoute));
			await Assert.That(handler.Requests.Count).IsEqualTo(1);
			await cut.InvokeAsync(() => pending.TrySetResult(true));
			cut.WaitForAssertion(() => cut.Find(".wiki-side-cats a.kit-row"));
			await Assert.That(handler.Requests.Count).IsEqualTo(2)
				.Because("the single owner read is followed by its single authorized return-origin read");
			await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("Reference collection");
			await cut.Find(".kit-side-search input").InputAsync("harbour");
			await cut.Find(".kit-side-search").SubmitAsync();
			await Assert.That(Uri.UnescapeDataString(new Uri(_nav.Uri).AbsolutePath)).IsEqualTo("/wiki/collections/package:reference");
		}
		finally { pending.TrySetResult(false); }
	}

	[Test]
	[Arguments("extra=a%26b", "extra=A%26b")]
	[Arguments("extra=a%26b", "unknown=a%26b")]
	[Arguments("expectedPageId=intro", "expectedPageId=other")]
	[Arguments("lang=fr-CA", "lang=de")]
	[Arguments("browseQ=R%C3%A8gle%20%26%20C%2B%2B", "browseQ=Changed")]
	[Arguments("browseSkip=100", "browseSkip=150")]
	[Arguments("membersCollection=game", "membersCollection=all")]
	[Arguments("membersSkip=50", "membersSkip=100")]
	[Arguments("/wiki/main/intro", "/wiki/help/intro")]
	public async Task RealContextChangeClearsDirectoryAndBlocksOldSearch(string before, string after)
	{
		var handler = InstallContextDirectory();
		var cut = RenderAt(ContextRoute);
		var count = handler.Requests.Count;
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Pending = pending.Task;
		try
		{
			await cut.InvokeAsync(() => _nav.NavigateTo(ContextRoute.Replace(before, after, StringComparison.Ordinal) + "#costs"));
			cut.WaitForAssertion(() => RequireContext(handler.Requests.Count > count, "A real context change did not reauthorize."));
			await Assert.That(cut.FindAll(".wiki-side-collections a, .wiki-side-cats a")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("Reference collection");
			var destination = _nav.Uri;
			await cut.Find(".kit-side-search input").InputAsync("blocked");
			await cut.Find(".kit-side-search").SubmitAsync();
			await Assert.That(_nav.Uri).IsEqualTo(destination);
			await cut.InvokeAsync(() => pending.TrySetResult(false));
			cut.WaitForAssertion(() => cut.Find("button.wiki-side-new"));
			await Assert.That(cut.FindAll(".wiki-side-collections a, .wiki-side-cats a")).IsEmpty();
		}
		finally { pending.TrySetResult(false); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ReaderChangeReauthorizesEvenAfterEquivalentFragmentReuse(bool characterChange)
	{
		var handler = InstallContextDirectory();
		var cut = RenderAt(ContextRoute);
		await cut.InvokeAsync(() => _nav.NavigateTo(EquivalentContextRoute));
		var count = handler.Requests.Count;
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Pending = pending.Task;
		try
		{
			var auth = Services.GetRequiredService<IAccountAuthState>();
			await cut.InvokeAsync(() =>
			{
				if (characterChange) auth.ActiveCharacterChanged += Raise.Event<Action>();
				else auth.AuthStateChanged += Raise.Event<Action>();
			});
			cut.WaitForAssertion(() => RequireContext(handler.Requests.Count > count, "Reader change reused a previous reader's authorization."));
			await Assert.That(cut.FindAll(".wiki-side-collections a, .wiki-side-cats a")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("Reference collection");
			await cut.InvokeAsync(() => pending.TrySetResult(false));
			cut.WaitForAssertion(() => cut.Find("button.wiki-side-new"));
			await Assert.That(cut.FindAll(".wiki-side-collections a, .wiki-side-cats a")).IsEmpty();
		}
		finally { pending.TrySetResult(false); }
	}

	[Test]
	public async Task RetryReissuesFailedDirectoryAtTheSameContext()
	{
		var handler = InstallContextDirectory();
		handler.Refuse = true;
		_nav.NavigateTo(ContextRoute);
		var cut = Render<WikiSidebar>();
		cut.WaitForAssertion(() => cut.Find("button.wiki-side-new"));
		var count = handler.Requests.Count;
		handler.Refuse = false;
		await cut.Find("button.wiki-side-new").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".wiki-side-cats a.kit-row"));
		await Assert.That(handler.Requests.Count).IsEqualTo(count + 2);
		await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("Reference collection");
	}

	private ContextDirectoryApi InstallContextDirectory()
	{
		var handler = new ContextDirectoryApi();
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri(_nav.BaseUri) });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
		return handler;
	}

	private static void RequireContext(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class ContextDirectoryApi : HttpMessageHandler
	{
		public List<Uri> Requests { get; } = [];
		public Task<bool>? Pending { get; set; }
		public bool Refuse { get; set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			Requests.Add(uri);
			// Each concurrent request gets its own response even when the gate is shared.
			var allowed = Pending is { } pending ? await pending : !Refuse;
			if (!allowed) return new HttpResponseMessage(HttpStatusCode.Forbidden);
			var collection = System.Web.HttpUtility.ParseQueryString(uri.Query)["collection"] ?? "package:reference";
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new WikiNavigationDto(collection, 151,
					[new WikiCollectionSummaryDto(collection, "Reference collection", 151)],
					[new WikiCategorySummaryDto("guides", "Guides", 3, true, true)]))
			};
		}
	}

	private DelayedDirectoryApi InstallDelayedDirectory()
	{
		var handler = new DelayedDirectoryApi();
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri(_nav.BaseUri) });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
		return handler;
	}

	private sealed class DelayedDirectoryApi : HttpMessageHandler
	{
		private readonly TaskCompletionSource<HttpResponseMessage> _second = new();
		private bool _completed;
		private bool _allowed;
		public bool SecondRequested { get; private set; }

		public void CompleteSecond(bool allowed)
		{
			if (_completed) return;
			_completed = true;
			_allowed = allowed;
			_second.SetResult(allowed ? Directory("second") : new HttpResponseMessage(HttpStatusCode.Forbidden));
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var collection = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["collection"];
			if (collection == "package:second")
			{
				SecondRequested = true;
				return _completed ? Task.FromResult(_allowed ? Directory("second") : new HttpResponseMessage(HttpStatusCode.Forbidden)) : _second.Task;
			}
			return Task.FromResult(Directory("first"));
		}

		private static HttpResponseMessage Directory(string key) => new(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new WikiNavigationDto("package:" + key, 4,
				[new WikiCollectionSummaryDto("package:" + key, key == "first" ? "First private collection" : "Second collection", 4)],
				[new WikiCategorySummaryDto(key, key == "first" ? "First private category" : "Second category", 4, true, true)]))
		};
	}

}
