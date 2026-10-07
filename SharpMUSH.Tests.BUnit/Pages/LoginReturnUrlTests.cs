using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Fakes api/auth/account-login with a fixed successful response, and api/account/characters with the roster
/// the account has by the time it is asked (<see cref="RosterNow"/>; unset answers 404).
/// </summary>
internal sealed class LoginApiHandler : HttpMessageHandler
{
	public object[]? RosterNow { get; set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Get && path == "api/account/characters" && RosterNow is { } roster)
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(roster) });
		if (request.Method == HttpMethod.Post && path == "api/auth/account-login")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "test-account-id",
					username = "headwiz",
					// One character, deliberately: an account with an EMPTY roster is routed into
					// onboarding instead of the return URL (see OnboardingRoutingTests), which is a
					// different question from the URL sanitizing these tests are about.
					characters = new[] { new { dbrefNumber = 5, creationTime = 5L, name = "Gwendolyn", flags = "", isActing = true } },
					accountSessionToken = "test-session-token",
					mustChangePassword = false,
					role = "God",
					permissions = new[] { "*" }
				})
			});
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Coverage for Login honoring an optional <c>returnUrl</c> query parameter on successful
/// sign-in (RedirectToLogin now sends anonymous visitors here with one attached — see
/// RedirectToLoginTests). Only same-origin relative paths are honored: absolute URLs and
/// protocol-relative "//host" / "/\host" tricks must fall back to "/" rather than open-redirect
/// the user off the site.
/// </summary>
public class LoginReturnUrlTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> ownedHttpClients = [];

	private readonly LoginApiHandler handler = new();

	private void SeedServices()
	{
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		ownedHttpClients.Add(apiClient);

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []))
			.AddSingleton(Substitute.For<ITerminalService>())
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>Renders Login already navigated to <paramref name="startingUri"/> and submits the Sign In form.</summary>
	private async Task<IRenderedComponent<SharpMUSH.Client.Pages.Login>> SubmitLoginFrom(string startingUri)
	{
		SeedServices();
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo(startingUri);

		var cut = Render<SharpMUSH.Client.Pages.Login>();
		await cut.Find("#login-username").ChangeAsync("headwiz");
		await cut.Find("#login-password").ChangeAsync("hunter2");
		await cut.Find("button.login-submit").ClickAsync();
		return cut;
	}

	/// <summary>A visitor who is already signed in is sent on, not shown the sign-in form again.</summary>
	[TUnit.Core.Test]
	public async Task AlreadySignedIn_GoesStraightToTheReturnUrl()
	{
		SeedServices();
		var auth = Services.GetRequiredService<AccountAuthService>();
		// The portal starts restoring the tab's session at startup (Program.cs), before anyone signs in.
		await auth.InitAsync();
		await auth.LoginAsync("headwiz", "hunter2");
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo($"/login?returnUrl={Uri.EscapeDataString("/play")}");

		var cut = Render<SharpMUSH.Client.Pages.Login>();
		var expected = new Uri(new Uri(nav.BaseUri), "/play").ToString();
		cut.WaitForAssertion(() =>
		{
			if (nav.Uri != expected)
				throw new InvalidOperationException("not redirected yet");
		});

		await Assert.That(nav.Uri).IsEqualTo(expected);
	}

	/// <summary>
	/// A signed-in account with no character yet (its only one unlinked, say) is sent to make one, as signing in
	/// sends it: a bookmarked /login does not skip onboarding.
	/// </summary>
	[TUnit.Core.Test]
	public async Task AlreadySignedIn_WithNoCharacter_GoesToOnboarding()
	{
		SeedServices();
		var auth = Services.GetRequiredService<AccountAuthService>();
		await auth.InitAsync();
		await auth.LoginAsync("headwiz", "hunter2");
		handler.RosterNow = [];
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo($"/login?returnUrl={Uri.EscapeDataString("/play")}");

		var cut = Render<SharpMUSH.Client.Pages.Login>();
		var expected = new Uri(new Uri(nav.BaseUri), "/characters/new").ToString();
		cut.WaitForAssertion(() =>
		{
			if (nav.Uri != expected)
				throw new InvalidOperationException("not redirected yet");
		});

		await Assert.That(nav.Uri).IsEqualTo(expected);
	}

	[TUnit.Core.Test]
	public async Task ValidRelativeReturnUrl_NavigatesThereOnSuccess()
	{
		var cut = await SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString("/play")}");
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		var expected = new Uri(new Uri(nav.BaseUri), "/play").ToString();

		cut.WaitForAssertion(() =>
		{
			if (nav.Uri != expected)
				throw new InvalidOperationException("login has not completed yet");
		});

		await Assert.That(nav.Uri).IsEqualTo(expected);
	}

	[TUnit.Core.Test]
	public async Task AbsoluteExternalReturnUrl_FallsBackToHome()
	{
		var externalUrl = "https://evil.example/steal";
		var cut = await SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString(externalUrl)}");
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

		var homeUri = new Uri(new Uri(nav.BaseUri), "/").ToString();
		cut.WaitForAssertion(() =>
		{
			if (nav.Uri != homeUri)
				throw new InvalidOperationException("login has not completed yet");
		});

		await Assert.That(nav.Uri).IsEqualTo(homeUri);
	}

	[TUnit.Core.Test]
	public async Task ProtocolRelativeReturnUrl_FallsBackToHome()
	{
		// "//evil.example/steal" is not absolute by RFC 3986 (no scheme) but browsers treat a
		// leading "//" as protocol-relative -- i.e. still an off-site redirect.
		var cut = await SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString("//evil.example/steal")}");
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

		var homeUri = new Uri(new Uri(nav.BaseUri), "/").ToString();
		cut.WaitForAssertion(() =>
		{
			if (nav.Uri != homeUri)
				throw new InvalidOperationException("login has not completed yet");
		});

		await Assert.That(nav.Uri).IsEqualTo(homeUri);
	}

	[TUnit.Core.Test]
	public async Task NoReturnUrl_NavigatesHomeAsBefore()
	{
		var cut = await SubmitLoginFrom("/login");
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

		var homeUri = new Uri(new Uri(nav.BaseUri), "/").ToString();
		cut.WaitForAssertion(() =>
		{
			if (nav.Uri != homeUri)
				throw new InvalidOperationException("login has not completed yet");
		});

		await Assert.That(nav.Uri).IsEqualTo(homeUri);
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
