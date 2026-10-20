using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

public class WikiCollectionLandingTests : TrackingBunitContext
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

	[Test]
	public async Task ExplicitLandingPrecedesFeaturedReferenceTopics()
	{
		var (api, _) = WikiApiFake.Install(this);
		api.NavigationResult = Navigation("The complete live localized article title");
		api.CollectionRows = [WikiApiFake.Page("1", "member", "Member article", "equipment")];
		var cut = Render<WikiCollectionBrowser>();
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));

		var entrance = cut.Find(".wiki-collection-landing");
		await Assert.That(entrance.QuerySelector("h2")!.TextContent).IsEqualTo("Start here");
		await Assert.That(entrance.GetAttribute("aria-label")).IsEqualTo("Start here");
		var links = entrance.QuerySelectorAll("a[href]");
		await Assert.That(links.Length).IsEqualTo(1);
		await Assert.That(links[0].TextContent).IsEqualTo("The complete live localized article title");
		await Assert.That(entrance.QuerySelectorAll("img").Length).IsEqualTo(0);
		await Assert.That(cut.Find(".wiki-collection-featured").TextContent).Contains("Torque reference");
		var regions = cut.FindAll(".wiki-collection-landing, .wiki-collection-featured, .wiki-collection-search, .wiki-collection-directory");
		Require(regions.Count == 4 && regions[0].ClassList.Contains("wiki-collection-landing")
			&& regions[1].ClassList.Contains("wiki-collection-featured")
			&& regions[2].ClassList.Contains("wiki-collection-search")
			&& regions[3].ClassList.Contains("wiki-collection-directory"), "Entrance/reference/search/directory order changed.");
		await Assert.That(api.Requests.Count).IsEqualTo(2)
			.Because("the compact authorized descriptor needs no article body or image hydration");
	}

	[Test]
	[Arguments("Start here — Workshop manual")]
	[Arguments("Orientation")]
	public async Task NoLandingDoesNotInferPinnedOrTitleFallback(string pinnedTitle)
	{
		var (api, _) = WikiApiFake.Install(this);
		api.NavigationResult = new WikiNavigationDto("game", 1, [],
			[new WikiCategorySummaryDto("orientation", pinnedTitle, 1, true, true)]);
		api.CollectionRows = [WikiApiFake.Page("1", "getting_started", "Getting started", "orientation")];
		var cut = Render<WikiCollectionBrowser>();
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));

		await Assert.That(cut.FindAll(".wiki-collection-landing")).IsEmpty();
		await Assert.That(cut.Find(".wiki-collection-featured").TextContent).Contains(pinnedTitle);
		await Assert.That(cut.Find(".wiki-collection-featured").QuerySelectorAll("button").Length).IsEqualTo(1);
		await Assert.That(cut.FindAll("a[href]").Any(anchor =>
			System.Web.HttpUtility.ParseQueryString(new Uri(new Uri("http://localhost/"), anchor.GetAttribute("href")).Query)["expectedPageId"] is not null)).IsFalse();
		await Assert.That(api.Requests.Count).IsEqualTo(2);
	}

	[Test]
	public async Task LandingLinkKeepsBrowseLocaleAndExpectedId()
	{
		var (api, _) = WikiApiFake.Install(this);
		api.NavigationResult = Navigation("Premiers pas", "package:workshop", total: 151);
		api.CollectionRows = Enumerable.Range(0, 151)
			.Select(i => WikiApiFake.Page("1", $"manual_{i:D3}", $"Workshop manual {i:D3}", "equipment")).ToArray();
		var cut = Render<WikiCollectionBrowser>(p => p.Add(x => x.Collection, "package:workshop")
			.Add(x => x.Search, "Workshop manual").Add(x => x.Category, "equipment")
			.Add(x => x.Skip, 100).Add(x => x.Locale, "fr-CA"));
		cut.WaitForAssertion(() => cut.Find(".wiki-collection-page"));

		var anchor = cut.Find(".wiki-collection-landing a[href]");
		var destination = new Uri(new Uri("http://localhost/"), anchor.GetAttribute("href"));
		var query = System.Web.HttpUtility.ParseQueryString(destination.Query);
		await Assert.That(destination.AbsolutePath).IsEqualTo("/wiki/main/begin_here");
		await Assert.That(anchor.TextContent).IsEqualTo("Premiers pas");
		await Assert.That(query["expectedPageId"]).IsEqualTo("wiki_page/901");
		await Assert.That(query.GetValues("expectedPageId")!.Length).IsEqualTo(1);
		await Assert.That(query["lang"]).IsEqualTo("fr-CA");
		await Assert.That(query["browseCollection"]).IsEqualTo("package:workshop");
		await Assert.That(query["membersCollection"]).IsEqualTo("package:workshop");
		await Assert.That(query["browseQ"]).IsEqualTo("Workshop manual");
		await Assert.That(query["browseCategory"]).IsEqualTo("equipment");
		await Assert.That(query["browseSkip"]).IsEqualTo("100");
		await Assert.That(api.Requests.Count).IsEqualTo(2);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ReaderSwitchClearsLandingBeforeAwait(bool characterChange)
	{
		var api = InstallControlledApi();
		var cut = Render<CollectionHost>();
		cut.WaitForAssertion(() => Require(cut.Find(".wiki-collection-landing").TextContent.Contains("PRIVATE_ENTRANCE", StringComparison.Ordinal), "Initial private entrance missing."));
		api.OtherReader = true;
		api.HoldCurrentNavigation = true;
		try
		{
			await cut.InvokeAsync(() => RaiseReaderChanged(characterChange));
			cut.WaitForAssertion(() => Require(api.CurrentNavigationHeld, "Current-reader navigation was not held."));
			await Assert.That(cut.FindAll(".wiki-collection-landing, .wiki-collection-page")).IsEmpty();
			await Assert.That(cut.Markup).DoesNotContain("PRIVATE_");
			await cut.InvokeAsync(api.ReleaseCurrentFailure);
			cut.WaitForAssertion(() => cut.Find(".wiki-collection-error"));
			await Assert.That(cut.Markup).DoesNotContain("PRIVATE_");
		}
		finally
		{
			await cut.InvokeAsync(api.ReleaseCurrentFailure);
			cut.WaitForAssertion(() => cut.Find(".wiki-collection-error"));
		}
	}

	[Test]
	[Arguments(false, "navigation")]
	[Arguments(true, "navigation")]
	[Arguments(false, "pages")]
	[Arguments(true, "pages")]
	public async Task DelayedOldNavigationCannotRestoreLanding(bool characterChange, string stage)
	{
		var api = InstallControlledApi(stage);
		var cut = Render<CollectionHost>();
		var initialRead = cut.FindComponent<ObservedCollectionBrowser>().Instance.InitialReadCompleted.Task;
		try
		{
			cut.WaitForAssertion(() => Require(api.OldHeld, "Old-reader request was not held."));
			api.OtherReader = true;
			await cut.InvokeAsync(() => RaiseReaderChanged(characterChange));
			cut.WaitForAssertion(() => Require(cut.Find(".wiki-collection-landing").TextContent.Contains("PUBLIC_ENTRANCE", StringComparison.Ordinal), "New authorized entrance missing."));
			await cut.InvokeAsync(api.ReleaseOld);
			await initialRead.WaitAsync(Deadline);
			await cut.InvokeAsync(() => { });
			await Assert.That(cut.Markup).DoesNotContain("PRIVATE_");
			await Assert.That(cut.Find(".wiki-collection-landing").TextContent).Contains("PUBLIC_ENTRANCE");
			await Assert.That(api.OldPageRequests).IsEqualTo(stage == "pages" ? 1 : 0);
		}
		finally { await FinishInitialRead(cut, api, initialRead); }
	}

	[Test]
	[Arguments("collection")]
	[Arguments("locale")]
	public async Task ParameterChangeCannotPublishOldLanding(string changedParameter)
	{
		var api = InstallControlledApi("pages");
		var cut = Render<CollectionHost>();
		var initialRead = cut.FindComponent<ObservedCollectionBrowser>().Instance.InitialReadCompleted.Task;
		try
		{
			cut.WaitForAssertion(() => Require(api.OldHeld, "Old page request was not held."));
			api.OtherReader = true;
			await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.Collection, changedParameter == "collection" ? "all" : "game")
				.Add(x => x.Locale, changedParameter == "locale" ? "de" : "fr-CA")));
			cut.WaitForAssertion(() => Require(cut.Find(".wiki-collection-landing").TextContent.Contains("PUBLIC_ENTRANCE", StringComparison.Ordinal), "Replacement entrance missing."));
			await cut.InvokeAsync(api.ReleaseOld);
			await initialRead.WaitAsync(Deadline);
			await cut.InvokeAsync(() => { });
			await Assert.That(cut.Markup).DoesNotContain("PRIVATE_");
			var destination = new Uri(new Uri("http://localhost/"), cut.Find(".wiki-collection-landing a[href]").GetAttribute("href"));
			var query = System.Web.HttpUtility.ParseQueryString(destination.Query);
			await Assert.That(query["browseCollection"]).IsEqualTo(changedParameter == "collection" ? "all" : "game");
			await Assert.That(query["lang"]).IsEqualTo(changedParameter == "locale" ? "de" : "fr-CA");
		}
		finally { await FinishInitialRead(cut, api, initialRead); }
	}

	[Test]
	[Arguments("navigation")]
	[Arguments("pages")]
	public async Task UnmountedBrowserCannotPublishPendingLandingOrReloadForReaderSignals(string stage)
	{
		var api = InstallControlledApi(stage);
		var cut = Render<CollectionHost>();
		var initialRead = cut.FindComponent<ObservedCollectionBrowser>().Instance.InitialReadCompleted.Task;
		try
		{
			cut.WaitForAssertion(() => Require(api.OldHeld, "Old request was not held."));
			await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.Visible, false)));
			var requests = api.Requests;
			await cut.InvokeAsync(() => RaiseReaderChanged(false));
			await cut.InvokeAsync(() => RaiseReaderChanged(true));
			await cut.InvokeAsync(api.ReleaseOld);
			await initialRead.WaitAsync(Deadline);
			await cut.InvokeAsync(() => { });
			await Assert.That(cut.Markup).IsEmpty();
			await Assert.That(api.Requests).IsEqualTo(requests);
		}
		finally { await FinishInitialRead(cut, api, initialRead); }
	}

	private ControlledApi InstallControlledApi(string? oldStage = null)
	{
		WikiApiFake.Install(this);
		var api = new ControlledApi { OldStage = oldStage };
		var client = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost:8081/") });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
		return api;
	}

	private void RaiseReaderChanged(bool characterChange)
	{
		var auth = Services.GetRequiredService<IAccountAuthState>();
		if (characterChange)
		{
			auth.ActiveCharacter.Returns(new AccountAuthService.CharacterSummary(2, 2L, "Other reader", ""));
			auth.ActiveCharacterChanged += Raise.Event<Action>();
		}
		else auth.AuthStateChanged += Raise.Event<Action>();
	}

	private static async Task FinishInitialRead(IRenderedComponent<CollectionHost> cut, ControlledApi api, Task initialRead)
	{
		await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.Visible, false)));
		await cut.InvokeAsync(api.ReleaseOld);
		await initialRead.WaitAsync(Deadline);
		await cut.InvokeAsync(() => { });
	}

	private static WikiNavigationDto Navigation(string title, string collection = "game", int total = 1) => new(collection, total,
		[new WikiCollectionSummaryDto(collection, "Workshop manual", total)],
		[new WikiCategorySummaryDto("reference", "Torque reference", 1, true, true)])
	{
		LandingPage = new WikiLandingPageDto("wiki_page/901", "main", "begin_here", title, "fr", true)
	};

	private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

	// Observe completion of the genuine initial lifecycle, without replacing any rendering or loading behavior.
	public sealed class ObservedCollectionBrowser : WikiCollectionBrowser
	{
		private bool _initialStarted;
		public TaskCompletionSource InitialReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		protected override async Task OnParametersSetAsync()
		{
			var initial = !_initialStarted;
			_initialStarted = true;
			try { await base.OnParametersSetAsync(); }
			finally { if (initial) InitialReadCompleted.TrySetResult(); }
		}
	}

	public sealed class CollectionHost : ComponentBase
	{
		[Parameter] public bool Visible { get; set; } = true;
		[Parameter] public string Collection { get; set; } = "game";
		[Parameter] public string Locale { get; set; } = "fr-CA";
		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			if (!Visible) return;
			builder.OpenComponent<ObservedCollectionBrowser>(0);
			builder.AddAttribute(1, nameof(WikiCollectionBrowser.Collection), Collection);
			builder.AddAttribute(2, nameof(WikiCollectionBrowser.Locale), Locale);
			builder.CloseComponent();
		}
	}

	private sealed class ControlledApi : HttpMessageHandler
	{
		private readonly TaskCompletionSource<HttpResponseMessage> _old = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<HttpResponseMessage> _current = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private HttpResponseMessage? _oldResponse;
		public string? OldStage { get; init; }
		public bool OtherReader { get; set; }
		public bool HoldCurrentNavigation { get; set; }
		public bool CurrentNavigationHeld { get; private set; }
		public bool OldHeld { get; private set; }
		public int OldPageRequests { get; private set; }
		public int Requests { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests++;
			var oldReader = !OtherReader;
			var navigation = request.RequestUri!.AbsolutePath == "/api/wiki/navigation";
			var pages = request.RequestUri.AbsolutePath.StartsWith("/api/wiki/collections/", StringComparison.Ordinal)
				&& request.RequestUri.AbsolutePath.EndsWith("/pages", StringComparison.Ordinal);
			if (!navigation && !pages) return new HttpResponseMessage(HttpStatusCode.NotFound);
			if (pages && oldReader) OldPageRequests++;
			if (navigation && !oldReader && HoldCurrentNavigation)
			{
				CurrentNavigationHeld = true;
				return await _current.Task;
			}
			var prefix = oldReader ? "PRIVATE_" : "PUBLIC_";
			var collection = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["collection"] ?? "game";
			var response = navigation
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Navigation(prefix + "ENTRANCE", collection)) }
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[" + WikiApiFake.Page("1", "member", prefix + "MEMBER", "equipment") + "]", Encoding.UTF8, "application/json") };
			if (pages) response.Headers.Add("X-Total-Count", "1");
			if (oldReader && OldStage == (navigation ? "navigation" : "pages"))
			{
				_oldResponse = response;
				OldHeld = true;
				return await _old.Task;
			}
			return response;
		}

		public void ReleaseOld()
		{
			if (_oldResponse is not { } response) return;
			_oldResponse = null;
			if (!_old.TrySetResult(response)) response.Dispose();
		}

		public void ReleaseCurrentFailure()
		{
			var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
			if (!_current.TrySetResult(response)) response.Dispose();
		}
	}
}
