using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Regression coverage for /login getting the dedicated full-screen treatment: it must render
/// inside <see cref="OnboardingLayout"/> (no portal chrome — nav/topbar/terminal), not the
/// default <c>MainLayout</c> it fell back to before <c>@layout OnboardingLayout</c> was added.
/// Mirrors <c>SetupPageTests.Setup_UsesOnboardingLayout</c>.
/// </summary>
public class LoginPageTests
{
	[TUnit.Core.Test]
	public async Task Login_UsesOnboardingLayout()
	{
		var layoutAttribute = typeof(SharpMUSH.Client.Pages.Login)
			.GetCustomAttributes(typeof(LayoutAttribute), inherit: true)
			.Cast<LayoutAttribute>()
			.SingleOrDefault();

		await Assert.That(layoutAttribute).IsNotNull();
		await Assert.That(layoutAttribute!.LayoutType).IsEqualTo(typeof(OnboardingLayout));
	}
}

/// <summary>
/// Regression coverage for the login-page redesign (maintainer feedback: "Why even show the
/// Players / Scenes / Wiki Pages there. It should just be a login page."). The old two-column
/// marketing hero (tagline + placeholder Players/Scenes/Wiki-Pages stats that always rendered as
/// literal "—") must be gone; the page is now a single centered card with a compact logo header,
/// while the tab structure, form fields, and terminal link line are preserved.
/// </summary>
public class LoginPageRenderTests : BunitContext
{
	private void SeedServices()
	{
		Services
			.AddMudServices()
			.AddSingleton(Substitute.For<IHttpClientFactory>())
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []))
			.AddSingleton(Substitute.For<ITerminalService>())
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task AccountExplanationKeepsTheDirectCharacterTerminalRoute()
	{
		SeedServices();
		var cut = Render<SharpMUSH.Client.Pages.Login>();
		await Assert.That(cut.Find(".journey-identity").TextContent).IsEqualTo("AuthJourneyIdentity");
		await Assert.That(cut.FindAll("a[href='/play']")).IsNotEmpty();
		await Assert.That(cut.FindAll("#login-username, #login-password").Count).IsEqualTo(2);
	}

	[TUnit.Core.Test]
	public async Task Login_RendersSingleCard_NoHeroMarketingPanel()
	{
		SeedServices();
		var cut = Render<SharpMUSH.Client.Pages.Login>();
		var markup = cut.Markup;

		// The old hero panel and its placeholder stats are gone entirely.
		await Assert.That(markup.Contains("login-hero")).IsFalse();
		await Assert.That(markup.Contains("A living world awaits.")).IsFalse();
		await Assert.That(markup.Contains("Players")).IsFalse();
		await Assert.That(markup.Contains("Scenes")).IsFalse();
		await Assert.That(markup.Contains("Wiki Pages")).IsFalse();

		// New shape: compact logo + brand header above a single centered card.
		await Assert.That(cut.Find(".login-page")).IsNotNull();
		await Assert.That(cut.Find(".login-brand")).IsNotNull();
		await Assert.That(cut.Find(".login-card")).IsNotNull();
		await Assert.That(markup.Contains("AppTitle")).IsTrue();

		// Only one card-level container — not the old two-panel MudGrid split.
		await Assert.That(cut.FindAll(".login-card").Count).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task Login_PreservesTabsFormFieldsAndTerminalLink()
	{
		SeedServices();
		var cut = Render<SharpMUSH.Client.Pages.Login>();

		// Tab structure preserved.
		await Assert.That(cut.Markup.Contains("AuthSignIn")).IsTrue();
		await Assert.That(cut.Markup.Contains("AuthRegister")).IsTrue();

		// Selectors LoginReturnUrlTests depends on must keep working.
		await Assert.That(cut.Find("#login-username")).IsNotNull();
		await Assert.That(cut.Find("#login-password")).IsNotNull();
		await Assert.That(cut.Find("button.login-submit")).IsNotNull();

		// Terminal link line preserved.
		await Assert.That(cut.Markup.Contains("AuthTerminalLink")).IsTrue();
		await Assert.That(cut.Find("a[href='/play']")).IsNotNull();
	}

	/// <summary>The passkey button is offered only by a browser that can use one.</summary>
	[TUnit.Core.Test]
	[TUnit.Core.Arguments(true)]
	[TUnit.Core.Arguments(false)]
	public async Task Login_OffersPasskeySignIn_OnlyWhereSupported(bool supported)
	{
		SeedServices();
		JSInterop.Setup<bool>("SharpMUSH.Passkeys.isSupported").SetResult(supported);

		var cut = Render<SharpMUSH.Client.Pages.Login>();

		if (supported)
			cut.WaitForAssertion(() => cut.Find("button.login-passkey"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("button.login-passkey").Count).IsEqualTo(supported ? 1 : 0);
		if (supported)
			await Assert.That(cut.Find("button.login-passkey").TextContent).Contains("AuthPasskeySignIn");
	}

	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	/// <summary>
	/// Renders Login once the account session has been restored, so the page does not draw itself again (and
	/// replace its handlers) between a test finding an element and acting on it.
	/// </summary>
	private async Task<IRenderedComponent<SharpMUSH.Client.Pages.Login>> RenderSettledAsync()
	{
		await Services.GetRequiredService<AccountAuthService>().InitAsync();
		return Render<SharpMUSH.Client.Pages.Login>();
	}

	/// <summary>
	/// Enter straight after typing the password signs in with what was typed. The fields used to commit their
	/// value on change, which comes after the keydown, so the first Enter found them empty.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Login_EnterAfterTyping_SubmitsWhatWasTyped()
	{
		SeedServices();
		var wire = CapturingHttpHandler.WithJson(System.Net.HttpStatusCode.Unauthorized, """{"error":"Wrong password."}""");
		Services.AddSingleton<IHttpClientFactory>(new SingleClientFactory(wire));
		var cut = await RenderSettledAsync();

		await cut.Find("#login-username").InputAsync(new ChangeEventArgs { Value = "Marguerite" });
		await cut.Find("#login-password").InputAsync(new ChangeEventArgs { Value = "Wizard1996!" });
		await cut.Find("#login-password").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

		cut.WaitForAssertion(() => cut.Find("[role=alert]"), TimeSpan.FromSeconds(5));
		await Assert.That(wire.LastRequest).IsNotNull();
		await Assert.That(cut.Markup).DoesNotContain("AuthEnterCredentials");
	}

	/// <summary>A refused sign-in is announced: its alert has the alert role.</summary>
	[TUnit.Core.Test]
	public async Task Login_EmptySubmit_AnnouncesTheError()
	{
		SeedServices();
		var cut = await RenderSettledAsync();

		await cut.Find("button.login-submit").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

		await Assert.That(cut.Find("[role=alert]").TextContent).Contains("AuthEnterCredentials");
	}

	[TUnit.Core.Test]
	public async Task Login_DefaultsToSignInTab()
	{
		SeedServices();
		var cut = Render<SharpMUSH.Client.Pages.Login>();

		// Sign In panel active: its fields render, the Register panel's submit does not.
		await Assert.That(cut.FindAll("#login-username").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("button.register-submit").Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task Login_TabRegisterQuery_OpensRegisterTab()
	{
		SeedServices();
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/login?tab=register");

		var cut = Render<SharpMUSH.Client.Pages.Login>();

		// Register panel active: its submit renders, the Sign In panel's fields do not.
		await Assert.That(cut.FindAll("button.register-submit").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("#login-username").Count).IsEqualTo(0);
	}
}
