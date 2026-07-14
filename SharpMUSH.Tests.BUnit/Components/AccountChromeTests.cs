using Bunit;
using MudBlazor.Services;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// bUnit coverage for <see cref="AccountChrome"/>, the header account area extracted out of
/// <c>MainLayout</c> specifically so this could be tested in isolation: MainLayout's own
/// OnInitializedAsync makes a setup-status HTTP call, wires ILayoutService/ITerminalService,
/// and drives NavigationManager.LocationChanged-based routing — too much incidental setup for
/// what is fundamentally a three-state render check (anonymous / logged-in / dev-mode debug
/// auth). Uses <see cref="MudHarness"/> (declared in SchemaFormRendererShapeTests.cs, same
/// namespace) because the logged-in/debug-auth states render a MudMenu, which needs a
/// MudPopoverProvider in the tree (see PlaySidebarTests' precedent for the same requirement).
/// </summary>
public class AccountChromeTests : BunitContext
{
	public AccountChromeTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task Anonymous_RendersSignInLink()
	{
		var cut = Render<MudHarness>(p => p
			.AddChildContent<AccountChrome>(ac => ac
				.Add(a => a.IsLoggedIn, false)
				.Add(a => a.IsDebugAuth, false)));

		var signIn = cut.Find("a[href='/login']");
		await Assert.That(signIn.TextContent).Contains("Sign in");

		// Anonymous browsing must never also show the authenticated account chip.
		await Assert.That(cut.Markup).DoesNotContain("phosphor-user-btn");
	}

	[TUnit.Core.Test]
	public async Task LoggedIn_RendersAccountChip_NotSignInLink()
	{
		var cut = Render<MudHarness>(p => p
			.AddChildContent<AccountChrome>(ac => ac
				.Add(a => a.IsLoggedIn, true)
				.Add(a => a.IsDebugAuth, false)
				.Add(a => a.DisplayName, "headwiz")
				.Add(a => a.UserInitial, "H")
				.Add(a => a.AccountUsername, "headwiz")));

		await Assert.That(cut.Markup).Contains("phosphor-user-btn");
		await Assert.That(cut.Markup).Contains("headwiz");
		await Assert.That(cut.Markup).DoesNotContain("href=\"/login\"");
	}

	[TUnit.Core.Test]
	public async Task DebugAuth_RendersAccountChip_NotSignInLink()
	{
		var cut = Render<MudHarness>(p => p
			.AddChildContent<AccountChrome>(ac => ac
				.Add(a => a.IsLoggedIn, false)
				.Add(a => a.IsDebugAuth, true)
				.Add(a => a.DisplayName, "DebugAdmin")
				.Add(a => a.UserInitial, "D")));

		await Assert.That(cut.Markup).Contains("phosphor-user-btn");
		await Assert.That(cut.Markup).Contains("DebugAdmin");
		await Assert.That(cut.Markup).DoesNotContain("href=\"/login\"");
	}

	[TUnit.Core.Test]
	public async Task LoggedIn_MenuOpened_ShowsAccountCaptionAndDisconnect_NotDevModeCaption()
	{
		var cut = Render<MudHarness>(p => p
			.AddChildContent<AccountChrome>(ac => ac
				.Add(a => a.IsLoggedIn, true)
				.Add(a => a.IsDebugAuth, false)
				.Add(a => a.DisplayName, "headwiz")
				.Add(a => a.UserInitial, "H")
				.Add(a => a.AccountUsername, "headwiz")));

		cut.Find("button.phosphor-user-btn").Click();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Account: headwiz"))
				throw new InvalidOperationException("menu not open yet");
		});

		await Assert.That(cut.Markup).Contains("Account: headwiz");
		await Assert.That(cut.Markup).Contains("Disconnect");
		await Assert.That(cut.Markup).DoesNotContain("Dev Mode");
	}

	[TUnit.Core.Test]
	public async Task DebugAuth_MenuOpened_ShowsDevModeCaption_NoDisconnectItem()
	{
		var cut = Render<MudHarness>(p => p
			.AddChildContent<AccountChrome>(ac => ac
				.Add(a => a.IsLoggedIn, false)
				.Add(a => a.IsDebugAuth, true)
				.Add(a => a.DisplayName, "DebugAdmin")
				.Add(a => a.UserInitial, "D")));

		cut.Find("button.phosphor-user-btn").Click();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Dev Mode (DebugAdmin)"))
				throw new InvalidOperationException("menu not open yet");
		});

		await Assert.That(cut.Markup).Contains("Dev Mode (DebugAdmin)");
		// Disconnect (real-session logout) never applies to the debug identity.
		await Assert.That(cut.Markup).DoesNotContain("Disconnect");
	}
}
