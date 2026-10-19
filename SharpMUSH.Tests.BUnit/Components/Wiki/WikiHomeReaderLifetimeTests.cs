using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>The home remains mounted when account or acting-character authority changes at /wiki.</summary>
public class WikiHomeReaderLifetimeTests : TrackingBunitContext
{
	private readonly HomeApi _api = new();
	private readonly NavigationManager _nav;

	public WikiHomeReaderLifetimeTests()
	{
		WikiApiFake.Install(this);
		_nav = Services.GetRequiredService<NavigationManager>();
		_nav.NavigateTo("/wiki?lang=fr-CA");
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri(_nav.BaseUri) });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient("api").Returns(client);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CompletedPrivateHomeIsClearedBeforeCurrentReaderDirectoryCompletes(bool characterChange)
	{
		var cut = Render<HomeHost>();
		WaitForPrivateHome(cut);
		var originalUri = _nav.Uri;
		var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_api.OtherReader = true;
		_api.PendingCurrentDirectory = pending.Task;
		try
		{
			await cut.InvokeAsync(() => RaiseReaderChanged(characterChange));
			cut.WaitForAssertion(() => Require(_api.CurrentDirectoryRequests == 1, "The same URI did not reauthorize the current reader."));
			await AssertNoPrivateHome(cut);
			await Assert.That(cut.FindAll(".wiki-collection-entry, .wiki-cat-card")).IsEmpty();
			await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
			cut.WaitForAssertion(() => cut.Find("[role='alert']"));
			await AssertNoPrivateHome(cut);
			await Assert.That(_nav.Uri).IsEqualTo(originalUri);
		}
		finally { await cut.InvokeAsync(() => CompletePending(pending, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))); }
	}

	[Test]
	[Arguments(false, "directory")]
	[Arguments(true, "directory")]
	[Arguments(false, "preview")]
	[Arguments(true, "preview")]
	[Arguments(false, "hero")]
	[Arguments(true, "hero")]
	public async Task OldReaderDirectoryPreviewAndHeroCannotPublishAfterReaderChange(bool characterChange, string stage)
	{
		_api.PendingStage = stage;
		var cut = Render<HomeHost>();
		try
		{
			cut.WaitForAssertion(() => Require(_api.PendingRequested, "The old reader's delayed request did not start."));
			_api.OtherReader = true;
			await cut.InvokeAsync(() => RaiseReaderChanged(characterChange));
			cut.WaitForAssertion(() => Require(_api.CurrentDirectoryRequests == 1, "The new reader did not refresh the home."));
			await cut.InvokeAsync(() => _api.CompleteOld());
			cut.WaitForAssertion(() => Require(_api.PendingReturned, "The delayed old-reader response did not complete."));
			await cut.InvokeAsync(() => { });
			await AssertNoPrivateHome(cut);
			await Assert.That(_api.OldPreviewRequests).IsEqualTo(stage == "directory" ? 0 : 1);
			await Assert.That(_api.OldHeroRequests).IsEqualTo(stage == "hero" ? 1 : 0);
		}
		finally { await cut.InvokeAsync(() => _api.CompleteOld()); }
	}

	[Test]
	[Arguments(false, "directory")]
	[Arguments(true, "directory")]
	[Arguments(false, "preview")]
	[Arguments(true, "preview")]
	[Arguments(false, "hero")]
	[Arguments(true, "hero")]
	public async Task FailedRefreshAtEachHomeStageCannotRetainPriorReaderState(bool characterChange, string failedStage)
	{
		var cut = Render<HomeHost>();
		WaitForPrivateHome(cut);
		_api.OtherReader = true;
		_api.FailCurrentStage = failedStage;
		await cut.InvokeAsync(() => RaiseReaderChanged(characterChange));
		cut.WaitForAssertion(() => Require(_api.CurrentFailureRequests == 1, "The current reader's failing refresh did not run."));
		await AssertNoPrivateHome(cut);
		await Assert.That(_nav.Uri).EndsWith("/wiki?lang=fr-CA");
	}

	[Test]
	[Arguments("directory")]
	[Arguments("preview")]
	[Arguments("hero")]
	public async Task ActualUnmountUnsubscribesBothReaderSignalsAndRejectsPendingWork(string stage)
	{
		_api.PendingStage = stage;
		var cut = Render<HomeHost>();
		try
		{
			cut.WaitForAssertion(() => Require(_api.PendingRequested, "The delayed home read did not start."));
			await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(host => host.Visible, false)));
			cut.WaitForAssertion(() => Require(string.IsNullOrEmpty(cut.Markup), "The widget was not unmounted."));
			var requests = _api.Requests;
			await cut.InvokeAsync(() => RaiseReaderChanged(false));
			await cut.InvokeAsync(() => RaiseReaderChanged(true));
			_nav.NavigateTo("/wiki?lang=de");
			await cut.InvokeAsync(() => _api.CompleteOld());
			cut.WaitForAssertion(() => Require(_api.PendingReturned, "The disposed widget's pending response did not complete."));
			await cut.InvokeAsync(() => { });
			await Assert.That(_api.Requests).IsEqualTo(requests);
			await Assert.That(cut.Markup).IsEmpty();
		}
		finally { await cut.InvokeAsync(() => _api.CompleteOld()); }
	}

	[Test]
	public async Task ActualUnmountAfterCompletedHomeDoesNotReloadForEitherReaderSignal()
	{
		var cut = Render<HomeHost>();
		WaitForPrivateHome(cut);
		await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(host => host.Visible, false)));
		var requests = _api.Requests;
		await cut.InvokeAsync(() => RaiseReaderChanged(false));
		await cut.InvokeAsync(() => RaiseReaderChanged(true));
		await Assert.That(_api.Requests).IsEqualTo(requests);
		await Assert.That(cut.Markup).IsEmpty();
	}

	[Test]
	public async Task RealAccountCharacterSwitchClearsCompletedPrivateHomeAtTheSameUri()
	{
		using var context = new HomeContext();
		var factory = Substitute.For<IHttpClientFactory>();
		context.JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		context.JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("first-character-session");
		context.JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("same-account");
		var auth = new AccountAuthService(factory, context.JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		var client = context.Track(new HttpClient(new AccountSessionBearerHandler(auth) { InnerHandler = _api }) { BaseAddress = new Uri("http://localhost/") });
		factory.CreateClient("api").Returns(client);
		context.Services.AddMudServices().AddLocalization()
			.AddSingleton(factory).AddSingleton(auth).AddSingleton<IAccountAuthState>(auth)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));
		context.AddAuthorization().SetAuthorized("reader");
		context.JSInterop.Mode = JSRuntimeMode.Loose;
		await auth.InitAsync();
		auth.SetActiveCharacter(new AccountAuthService.CharacterSummary(1, 1L, "Author", "", IsActing: true));
		var nav = context.Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki?lang=fr-CA");
		var cut = context.Render<HomeHost>();
		WaitForPrivateHome(cut);
		var originalUri = nav.Uri;
		_api.OtherReader = true;

		var switched = await auth.SwitchCharacterAsync(new AccountAuthService.CharacterSummary(2, 2L, "Other reader", ""));
		await Assert.That(switched is string ott && ott == "switch-ott").IsTrue();
		await Assert.That(auth.AccountSessionToken).IsEqualTo("second-character-session");
		cut.WaitForAssertion(() => Require(_api.CurrentDirectoryTokens.Contains("second-character-session"), "The real switch producer did not reauthorize the home."));
		await AssertNoPrivateHome(cut);
		await Assert.That(nav.Uri).IsEqualTo(originalUri);
	}

	private sealed class HomeContext : TrackingBunitContext { }

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

	private static void WaitForPrivateHome(IRenderedComponent<HomeHost> cut) => cut.WaitForAssertion(() =>
	{
		Require(cut.Markup.Contains("PRIVATE_PREVIEW", StringComparison.Ordinal), "Private preview missing.");
		Require(cut.Markup.Contains("private-home-hero.jpg", StringComparison.Ordinal), "Private hero missing.");
		Require(cut.Markup.Contains("PRIVATE_CATEGORY", StringComparison.Ordinal), "Private pinned category missing.");
		Require(cut.Markup.Contains("PRIVATE_COLLECTION", StringComparison.Ordinal), "Private directory missing.");
	});

	private static async Task AssertNoPrivateHome(IRenderedComponent<HomeHost> cut)
	{
		cut.WaitForAssertion(() => Require(!cut.Markup.Contains("PRIVATE_", StringComparison.Ordinal)
			&& !cut.Markup.Contains("private-home-", StringComparison.Ordinal), "The current reader retained a previous reader's authorized home data."));
		await Assert.That(cut.Markup).DoesNotContain("PRIVATE_");
		await Assert.That(cut.Markup).DoesNotContain("private-home-");
	}

	// A successful completion transfers ownership to the HTTP consumer; a rejected retry does not.
	private static void CompletePending(TaskCompletionSource<HttpResponseMessage> pending, HttpResponseMessage response)
	{
		if (!pending.TrySetResult(response)) response.Dispose();
	}

	private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

	public sealed class HomeHost : ComponentBase
	{
		[Parameter] public bool Visible { get; set; } = true;
		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			if (!Visible) return;
			builder.OpenComponent<WikiIndexWidget>(0);
			builder.CloseComponent();
		}
	}

	private sealed class HomeApi : HttpMessageHandler
	{
		private readonly TaskCompletionSource<HttpResponseMessage> _old = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public string? PendingStage { get; set; }
		public bool PendingRequested { get; private set; }
		public bool PendingReturned { get; private set; }
		public bool OtherReader { get; set; }
		public string? FailCurrentStage { get; set; }
		public Task<HttpResponseMessage>? PendingCurrentDirectory { get; set; }
		public int CurrentDirectoryRequests { get; private set; }
		public List<string?> CurrentDirectoryTokens { get; } = [];
		public int CurrentFailureRequests { get; private set; }
		public int OldPreviewRequests { get; private set; }
		public int OldHeroRequests { get; private set; }
		public int Requests { get; private set; }

		public void CompleteOld() => CompletePending(_old, Response(PendingStage!, true));

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			if (uri.AbsolutePath == "/api/account/session")
				return Json(new { username = "same-account", mustChangePassword = false, role = "Player", permissions = Array.Empty<string>() });
			if (uri.AbsolutePath == "/api/auth/switch-character")
				return Json(new { ott = "switch-ott", expiresIn = 60, accountSessionToken = "second-character-session" });
			var stage = uri.AbsolutePath switch
			{
				"/api/wiki/navigation" => "directory",
				"/api/wiki/ns/main/home" => "hero",
				_ when uri.AbsolutePath.StartsWith("/api/wiki/collections/", StringComparison.Ordinal) => "preview",
				_ => null
			};
			if (stage is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
			Requests++;
			var oldReader = !OtherReader;
			if (!oldReader && stage == "directory")
			{
				CurrentDirectoryRequests++;
				CurrentDirectoryTokens.Add(request.Headers.Authorization?.Parameter);
				if (PendingCurrentDirectory is { } pending) return await pending;
			}
			if (oldReader && stage == "preview") OldPreviewRequests++;
			if (oldReader && stage == "hero") OldHeroRequests++;
			if (oldReader && stage == PendingStage)
			{
				PendingRequested = true;
				var response = await _old.Task;
				PendingReturned = true;
				return response;
			}
			if (!oldReader && stage == FailCurrentStage)
			{
				CurrentFailureRequests++;
				return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
			}
			return Response(stage, oldReader, FailCurrentStage == "preview");
		}

		private static HttpResponseMessage Response(string stage, bool oldReader, bool publicPin = false)
		{
			if (stage == "directory") return Json(new WikiNavigationDto("game", oldReader || publicPin ? 1 : 0,
				oldReader ? [new WikiCollectionSummaryDto("package:private", "PRIVATE_COLLECTION", 1)] : [],
				oldReader || publicPin ? [new WikiCategorySummaryDto("guides", oldReader ? "PRIVATE_CATEGORY" : "Public category", 1, true, true)] : []));
			if (stage == "hero") return oldReader
				? Json(new WikiPageDto("home", "home", "Home", "main", "", "", "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, true, 1, [], false) { Image = "/api/wiki-assets/private-home-hero.jpg" })
				: new HttpResponseMessage(HttpStatusCode.NotFound);
			var response = Json(oldReader
				? new[] { new WikiPageSummaryDto("private", "private", "PRIVATE_PREVIEW", "main", DateTimeOffset.UnixEpoch, true, 1, ["guides"], false, "en", false, "/api/wiki-assets/private-home-preview.jpg", null) }
				: Array.Empty<WikiPageSummaryDto>());
			response.Headers.Add("X-Total-Count", oldReader ? "1" : "0");
			return response;
		}

		private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
	}
}
