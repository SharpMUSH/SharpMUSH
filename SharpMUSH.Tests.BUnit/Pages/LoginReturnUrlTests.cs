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

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Fakes api/auth/account-login with a fixed successful response.</summary>
file sealed class LoginApiHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Post && path == "api/auth/account-login")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "test-account-id",
					username = "headwiz",
					characters = Array.Empty<object>(),
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

file sealed class LoginStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Coverage for Login honoring an optional <c>returnUrl</c> query parameter on successful
/// sign-in (RedirectToLogin now sends anonymous visitors here with one attached — see
/// RedirectToLoginTests). Only same-origin relative paths are honored: absolute URLs and
/// protocol-relative "//host" / "/\host" tricks must fall back to "/" rather than open-redirect
/// the user off the site.
/// </summary>
public class LoginReturnUrlTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> ownedHttpClients = [];

	private void SeedServices()
	{
		var apiClient = new HttpClient(new LoginApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		ownedHttpClients.Add(apiClient);

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance))
			.AddSingleton(Substitute.For<ITerminalService>())
			.AddSingleton<IStringLocalizer<SharedResource>, LoginStubLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>Renders Login already navigated to <paramref name="startingUri"/> and submits the Sign In form.</summary>
	private IRenderedComponent<SharpMUSH.Client.Pages.Login> SubmitLoginFrom(string startingUri)
	{
		SeedServices();
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo(startingUri);

		var cut = Render<SharpMUSH.Client.Pages.Login>();
		cut.Find("#login-username").Change("headwiz");
		cut.Find("#login-password").Change("hunter2");
		cut.Find("button.login-submit").Click();
		return cut;
	}

	[TUnit.Core.Test]
	public async Task ValidRelativeReturnUrl_NavigatesThereOnSuccess()
	{
		var cut = SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString("/play")}");
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
		var cut = SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString(externalUrl)}");
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
		var cut = SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString("//evil.example/steal")}");
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
		var cut = SubmitLoginFrom("/login");
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
