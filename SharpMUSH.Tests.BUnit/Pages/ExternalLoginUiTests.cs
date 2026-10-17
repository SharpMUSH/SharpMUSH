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
		public string? Csrf; public int Attempts; public bool HasPassword = true;
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("/providers", StringComparison.Ordinal)) return Reply(new[] { new { id = "discord", displayName = "Discord" }, new { id = "google", displayName = "Google" }, new { id = "microsoft", displayName = "Microsoft" }, new { id = "grapevine", displayName = "Grapevine" } });
			if (path == "/api/account/security") return Reply(new { hasPassword = HasPassword, allowCharacterPasswordAccountLogin = true, passkeys = new[] { new { id = "key" } }, identities = new[] { new { id = "google-identity", label = "Google account", registration = "google" } } });
			if (path.EndsWith("/csrf", StringComparison.Ordinal)) return Reply(new { token = "browser-csrf", fieldName = "__RequestVerificationToken" });
			if (path.EndsWith("/complete", StringComparison.Ordinal))
			{ Csrf = request.Headers.GetValues("X-SharpMUSH-CSRF").Single(); return Reply(new { signupRequired = true, returnPath = "/account" }); }
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
