using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Settings;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
namespace SharpMUSH.Tests.BUnit.Pages;

public class ExternalLoginUiTests : TrackingBunitContext
{
	private sealed class Handler : HttpMessageHandler
	{
		public string? Csrf; public int Attempts; public bool HasPassword = true; public int Providers = 4;
		public object? Completion;
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("/providers", StringComparison.Ordinal)) return Reply(new[] { new { id = "discord", displayName = "Discord" }, new { id = "google", displayName = "Google" }, new { id = "microsoft", displayName = "Microsoft" }, new { id = "grapevine", displayName = "Grapevine" } }.Take(Providers));
			if (path == "/api/account/security") return Reply(new { hasPassword = HasPassword, allowCharacterPasswordAccountLogin = true, passkeys = new[] { new { id = "key" } }, identities = new[] { new { id = "google-identity", label = "Google account", registration = "external-google", provider = "google" } } });
			if (path.EndsWith("/csrf", StringComparison.Ordinal)) return Reply(new { token = "browser-csrf", fieldName = "__RequestVerificationToken" });
			if (path.EndsWith("/complete", StringComparison.Ordinal))
			{ Csrf = request.Headers.GetValues("X-SharpMUSH-CSRF").Single(); return Reply(Completion ?? new { signupRequired = true, returnPath = "/account" }); }
			if (path.EndsWith("/signup", StringComparison.Ordinal)) { Attempts++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("Username is already taken.") }); }
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Signed out") });
		}
		private static Task<HttpResponseMessage> Reply(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
	}
	private Handler Setup()
	{
		var handler = new Handler(); var http = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient("api").Returns(http);
		Services.AddMudServices(); Services.AddSingleton(factory); JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create());
		Services.AddSingleton(sp => new AccountAuthService(factory, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(), NullLogger<AccountAuthService>.Instance, []));
		Services.AddSingleton<AccountSecurityClient>(); Services.AddSingleton(Substitute.For<ITerminalService>()); return handler;
	}
	[Test]
	[Arguments("/welcome", "/characters/new?returnUrl=%2Fwelcome")]
	[Arguments("/welcome?returnUrl=%2Fplay", "/characters/new?returnUrl=%2Fwelcome%3FreturnUrl%3D%252Fplay")]
	[Arguments("/characters/claim?returnUrl=%2Fwelcome", "/characters/claim?returnUrl=%2Fwelcome")]
	[Arguments("/characters/new?returnUrl=%2Fwelcome", "/characters/new?returnUrl=%2Fwelcome")]
	[Arguments("//evil.example/characters/new", "/characters/new")]
	[Arguments("/characters/newer", "/characters/new")]
	[Arguments("/\\evil.example", "/characters/new")]
	[Arguments("/welcome\n", "/characters/new")]
	public async Task ExternalLoginWithoutCharactersPreservesValidatedOnboardingDestination(string destination, string expected)
	{
		var wire = Setup();
		wire.Completion = new { accountId = "new-account", username = "newbie", characters = Array.Empty<AccountAuthService.CharacterSummary>(), accountSessionToken = "external-session", mustChangePassword = false, role = "Guest", permissions = Array.Empty<string>(), returnPath = destination };
		var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
		var cut = Render<ExternalLoginComplete>();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith(expected, StringComparison.Ordinal)) throw new InvalidOperationException($"Expected {expected}, got {nav.Uri}");
		});
		await Assert.That(Services.GetRequiredService<AccountAuthService>().IsLoggedIn).IsTrue();
	}

	[Test]
	public async Task ExternalLoginPasswordChangeTakesPrecedenceOverGuideReturn()
	{
		var wire = Setup();
		wire.Completion = new { accountId = "new-account", username = "newbie", characters = Array.Empty<AccountAuthService.CharacterSummary>(), accountSessionToken = "external-session", mustChangePassword = true, role = "Guest", permissions = Array.Empty<string>(), returnPath = "/welcome" };
		var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
		var cut = Render<ExternalLoginComplete>();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/account", StringComparison.Ordinal)) throw new InvalidOperationException("Password change was bypassed.");
		});
		await Assert.That(Services.GetRequiredService<AccountAuthService>().MustChangePassword).IsTrue();
	}

	[Test]
	public async Task ReadyProvidersRenderAllFourChoicesWithRealLocalizedLabels()
	{
		using var culture = CultureScope.For("en"); Setup();
		JSInterop.Setup<bool>("SharpMUSH.Passkeys.isSupported").SetResult(true);
		var cut = Render<Login>();
		cut.WaitForAssertion(() => { if (cut.FindAll(".external-signin-button").Count != 4) throw new Exception("Ready providers did not render"); });
		await Assert.That(cut.FindAll("#login-password").Count).IsEqualTo(1);
		await Assert.That(cut.Markup).Contains("Sign in with a passkey");
		await CaptureAsync("login.html", cut.Markup);
		await Assert.That(cut.Markup).Contains("Continue with Discord"); await Assert.That(cut.Markup).Contains("Continue with Grapevine");
		await Assert.That(cut.Markup).DoesNotContain("AuthContinueWith");
		await Assert.That(cut.FindAll(".external-signin-button--icon").Count).IsEqualTo(4);
		await Assert.That(cut.Find(".external-signin-divider").TextContent).IsEqualTo("or");
	}
	[Test]
	public async Task TwoProvidersGetFullButtons()
	{
		using var culture = CultureScope.For("en"); Setup().Providers = 2;
		var cut = Render<Login>();
		cut.WaitForAssertion(() => { if (cut.FindAll(".external-signin-button").Count != 2) throw new Exception("Ready providers did not render"); });
		await Assert.That(cut.FindAll(".external-signin-button--icon").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".external-signin-button svg.provider-logo").Count).IsEqualTo(2);
		await Assert.That(cut.Markup).DoesNotContain("Last used");
	}
	[Test]
	public async Task TheLastUsedProviderKeepsAFullButtonAboveTheRow()
	{
		using var culture = CultureScope.For("en"); Setup();
		JSInterop.Setup<string?>("localStorage.getItem", "sharpmush.signin.lastProvider").SetResult("google");
		var cut = Render<Login>();
		cut.WaitForAssertion(() => { if (cut.FindAll(".external-signin-button").Count != 4) throw new Exception("Ready providers did not render"); });
		var full = cut.FindAll(".external-signin-button:not(.external-signin-button--icon)").Single();
		await Assert.That(full.TextContent).Contains("Continue with Google");
		await Assert.That(full.TextContent).Contains("Last used");
		await Assert.That(cut.FindAll(".external-signin-button--icon").Select(button => button.GetAttribute("aria-label")))
			.IsEquivalentTo(["Continue with Discord", "Continue with Microsoft", "Continue with Grapevine"]);
	}
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AccountMethodsSupportOptionalPasswordAndWarnBeforeRevokingChanges(bool hasPassword)
	{
		using var culture = CultureScope.For("en"); var handler = Setup(); handler.HasPassword = hasPassword;
		var cut = Render<AccountSignInMethods>();
		cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Google account", StringComparison.Ordinal)) throw new Exception("Account methods not loaded"); });
		await Assert.That(cut.Markup).Contains("Set or change account password");
		await Assert.That(cut.Markup).Contains("Removing or changing a method signs out other sessions.");
		await Assert.That(cut.Markup.Contains("Remove account password", StringComparison.Ordinal)).IsEqualTo(hasPassword);
		await Assert.That(cut.Markup).Contains("Link Grapevine");
		await Assert.That(cut.FindAll(".signin-provider").Single(row => row.TextContent.Contains("Link Google", StringComparison.Ordinal)).TextContent).Contains("Google account");
		await CaptureAsync(hasPassword ? "account-methods.html" : "account-methods-passwordless.html", cut.Markup);
	}

	private static async Task CaptureAsync(string name, string markup)
	{
		if (Environment.GetEnvironmentVariable("SHARPMUSH_SOCIAL_UI_CAPTURE_PATH") is not { Length: > 0 } path) return;
		Directory.CreateDirectory(path);
		await File.WriteAllTextAsync(Path.Combine(path, name), markup);
	}

	[Test]
	public async Task UsernameCollisionKeepsTheConfirmationFormAvailableForRetry()
	{
		using var culture = CultureScope.For("en"); var wire = Setup(); var cut = Render<ExternalLoginComplete>();
		cut.WaitForAssertion(() => { if (cut.FindAll("input").Count != 1) throw new Exception("Username confirmation not shown"); });
		await cut.Find("input").ChangeAsync("taken");
		await cut.FindAll("button").Single(button => button.TextContent.Contains("Create account", StringComparison.OrdinalIgnoreCase)).ClickAsync();
		await Assert.That(cut.Markup).Contains("Username is already taken.");
		await Assert.That(cut.FindAll("input").Count).IsEqualTo(1);
		await cut.Find("input").ChangeAsync("another-name");
		await cut.FindAll("button").Single(button => button.TextContent.Contains("Create account", StringComparison.OrdinalIgnoreCase)).ClickAsync();
		await Assert.That(wire.Attempts).IsEqualTo(2); await Assert.That(wire.Csrf).IsEqualTo("browser-csrf");
	}
}
