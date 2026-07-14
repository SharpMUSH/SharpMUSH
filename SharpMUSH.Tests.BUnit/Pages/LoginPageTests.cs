using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;

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

file sealed class LoginPageStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
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
				NullLogger<AccountAuthService>.Instance))
			.AddSingleton(Substitute.For<ITerminalService>())
			.AddSingleton<IStringLocalizer<SharedResource>, LoginPageStubLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
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

		// New shape: compact logo + "SharpMUSH" header above a single centered card.
		await Assert.That(cut.Find(".login-page")).IsNotNull();
		await Assert.That(cut.Find(".login-brand")).IsNotNull();
		await Assert.That(cut.Find(".login-card")).IsNotNull();
		await Assert.That(markup.Contains("SharpMUSH")).IsTrue();

		// Only one card-level container — not the old two-panel MudGrid split.
		await Assert.That(cut.FindAll(".login-card").Count).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task Login_PreservesTabsFormFieldsAndTerminalLink()
	{
		SeedServices();
		var cut = Render<SharpMUSH.Client.Pages.Login>();

		// Tab structure preserved.
		await Assert.That(cut.Markup.Contains("Sign In")).IsTrue();
		await Assert.That(cut.Markup.Contains("Register")).IsTrue();

		// Selectors LoginReturnUrlTests depends on must keep working.
		await Assert.That(cut.Find("#login-username")).IsNotNull();
		await Assert.That(cut.Find("#login-password")).IsNotNull();
		await Assert.That(cut.Find("button.login-submit")).IsNotNull();

		// Terminal link line preserved.
		await Assert.That(cut.Markup.Contains("terminal")).IsTrue();
		await Assert.That(cut.Find("a[href='/play']")).IsNotNull();
	}
}
