using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
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
/// Fakes the tiny slice of the account API that /account touches when it refreshes the
/// characters list on init (<c>api/account/characters</c>). Always returns an empty list —
/// these tests are about render-crash regressions, not characters-table content.
/// </summary>
file sealed class AccountPageApiHandler(object[]? characters = null, object[]? passkeys = null) : HttpMessageHandler
{
	/// <summary>DELETE api/account/characters/{n} requests seen: the unlink itself.</summary>
	public int Unlinks { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Get && path == "api/account/characters")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(characters ?? [])
			});
		if (request.Method == HttpMethod.Get && path == "api/account/passkeys")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(passkeys ?? [])
			});
		if (request.Method == HttpMethod.Delete && path.StartsWith("api/account/characters/", StringComparison.Ordinal))
		{
			Unlinks++;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Regression coverage for the /account page render crash: "Render output is invalid for
/// component of type 'SharpMUSH.Client.Pages.Account'. A frame of type 'Element' was left
/// unclosed." Root cause was the logged-out branch's <c>return;</c> statement exiting
/// BuildRenderTree before the outer <c>&lt;div class="acct-page"&gt;</c> (opened unconditionally
/// at the top of the markup) was closed. These tests render the page in each state the field
/// report implicated (logged-in normal, logged-in MustChangePassword, logged-out/post-logout)
/// and simply assert no exception escapes Render — that's the whole regression surface.
/// </summary>
public class AccountPageTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> ownedHttpClients = [];
	private BunitAuthorizationContext Auth { get; }

	public AccountPageTests()
	{
		Auth = this.AddAuthorization();
		Auth.SetNotAuthorized();
	}

	/// <summary>
	/// Wires a real <see cref="AccountAuthService"/> (backed by <see cref="AccountPageApiHandler"/>)
	/// into the test's DI container, pre-seeding sessionStorage/localStorage via JSInterop so that
	/// the service's own <c>InitAsync</c> (called from Account.razor's OnInitializedAsync) restores
	/// exactly the auth state under test — mirrors AdminAccountsPageTests/SetupPageTests' pattern of
	/// driving state through the real service rather than substituting it (AccountAuthService's
	/// members aren't virtual, so NSubstitute can't fake it directly).
	/// </summary>
	private void SeedAuthState(bool loggedIn, bool mustChangePassword = false, HttpMessageHandler? handler = null,
		bool passkeysSupported = true)
	{
		var apiClient = Track(new HttpClient(handler ?? new AccountPageApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		ownedHttpClients.Add(apiClient);

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []))
			.AddSingleton<PasskeyInterop>()
			.AddSingleton<AccountPasskeyService>();

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<bool>("SharpMUSH.Passkeys.isSupported").SetResult(passkeysSupported);

		if (loggedIn)
		{
			JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
			JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
			JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword")
				.SetResult(mustChangePassword ? bool.TrueString : bool.FalseString);
			JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
			JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		}
		else
		{
			JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);
		}
	}

	[Test]
	public async Task Render_LoggedIn_NormalState_DoesNotThrow()
	{
		SeedAuthState(loggedIn: true, mustChangePassword: false);

		// If Account.razor's render tree is unbalanced (Bug A), Render itself throws
		// "Render output is invalid ... A frame of type 'Element' was left unclosed." — that
		// failure IS the regression test; no need to wrap it in an assertion helper.
		var cut = Render<SharpMUSH.Client.Pages.Account>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("headwiz"))
				throw new InvalidOperationException("profile not rendered yet");
		});
		await Assert.That(cut.Markup).Contains("AuthProfile");
	}

	[Test]
	public async Task Render_LoggedIn_MustChangePassword_DoesNotThrow()
	{
		SeedAuthState(loggedIn: true, mustChangePassword: true);

		var cut = Render<SharpMUSH.Client.Pages.Account>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthPasswordChangeRequired"))
				throw new InvalidOperationException("must-change-password banner not rendered yet");
		});
		await Assert.That(cut.Markup).Contains("AuthPasswordChangeRequired");
		// The Profile/Characters sections are gated off while a password change is pending.
		await Assert.That(cut.Markup).DoesNotContain("Characters");
	}

	/// <summary>
	/// /account used to be the only account page with no auth gate, so an anonymous visitor got a
	/// dead-end card telling them to "use the terminal login panel". The gate is what sends them to
	/// /login instead (via App.razor's NotAuthorized branch → RedirectToLogin), and bUnit renders a
	/// page component directly, below AuthorizeRouteView — so the attribute itself is the thing to
	/// assert, not a redirect the direct render can never perform.
	/// </summary>
	[Test]
	public async Task AccountPage_IsGatedByAuthorize()
	{
		var authorize = typeof(SharpMUSH.Client.Pages.Account)
			.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
			.Cast<AuthorizeAttribute>()
			.SingleOrDefault();

		await Assert.That(authorize).IsNotNull()
			.Because("/account exposes the username, character list and password form of whoever is signed in");
		// Plain [Authorize]: any signed-in account may manage its own account.
		await Assert.That(authorize!.Roles).IsNull();
		await Assert.That(authorize.Policy).IsNull();
	}

	/// <summary>
	/// The one state that still reaches the page's no-account-session branch now that [Authorize]
	/// intercepts anonymous visitors: a development DebugAuth principal whose debug-OTT never
	/// produced an account session (here, because the fake API 404s <c>api/auth/debug-ott</c>).
	/// </summary>
	[Test]
	public async Task Render_DebugAuthWithoutAccountSession_ShowsDebugCard()
	{
		Auth.SetAuthorized("DebugAdmin");
		SeedAuthState(loggedIn: false);

		var cut = Render<SharpMUSH.Client.Pages.Account>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthDebugMode"))
				throw new InvalidOperationException("debug-mode card not rendered yet");
		});
		await Assert.That(cut.Markup).Contains("AuthDebugMode");
	}

	/// <summary>
	/// Post-logout, before the redirect lands: no account session and no debug identity either.
	/// The page must render its empty shell — not the removed "use the terminal login panel"
	/// dead end, and not a stale Debug Mode alert.
	/// </summary>
	[Test]
	public async Task Render_LoggedOut_RendersNoCard()
	{
		SeedAuthState(loggedIn: false);

		var cut = Render<SharpMUSH.Client.Pages.Account>();

		await Assert.That(cut.Markup).DoesNotContain("kit-card");
		await Assert.That(cut.Markup).DoesNotContain("AuthDebugMode");
	}

	/// <summary>
	/// D1 §6.5: the account page is a Settings page — the plain header names the account and carries
	/// Log out, the profile and the characters are cards, and the characters card is the
	/// <c>#characters</c> anchor the Settings sidebar's Characters row lands on.
	/// </summary>
	[Test]
	public async Task Render_LoggedIn_UsesThePlainHeaderAndCards_WithTheCharactersAnchor()
	{
		Auth.SetAuthorized("headwiz");
		SeedAuthState(loggedIn: true);

		var cut = Render<SharpMUSH.Client.Pages.Account>();
		cut.WaitForAssertion(() => cut.Find("#characters .kit-card"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("headwiz");
		await Assert.That(cut.Find(".kit-page-head .kit-page-actions .acct-btn-danger").TextContent.Trim()).IsEqualTo("AuthLogOutOfAccount");
		await Assert.That(cut.FindAll(".kit-card .kit-card-title").Select(t => t.TextContent).ToList())
			.IsEquivalentTo(["AuthProfile", "Characters", "AuthPasskeys"]);
		await Assert.That(cut.Find("#characters a[href='/characters/new']")).IsNotNull();
		var layout = typeof(SharpMUSH.Client.Pages.Account).GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.LayoutAttribute), false)
			.Cast<Microsoft.AspNetCore.Components.LayoutAttribute>().Single();
		await Assert.That(layout.LayoutType).IsEqualTo(typeof(SharpMUSH.Client.Layout.SettingsLayout));
	}

	/// <summary>
	/// The passkeys card, the <c>#passkeys</c> anchor, lists the account's passkeys with when each was
	/// last used, and offers to add one in a browser that can make one.
	/// </summary>
	[Test]
	public async Task Render_LoggedIn_ListsPasskeys_AndOffersToAddOne()
	{
		Auth.SetAuthorized("headwiz");
		var handler = new AccountPageApiHandler(passkeys:
		[
			new { id = "AQID", name = "Phone", createdAt = DateTimeOffset.UnixEpoch, lastUsedAt = (DateTimeOffset?)null, isSynced = true }
		]);
		SeedAuthState(loggedIn: true, handler: handler);

		var cut = Render<SharpMUSH.Client.Pages.Account>();
		cut.WaitForAssertion(() => cut.Find("#passkeys .acct-passkey"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find("#passkeys .acct-passkey-name").TextContent).Contains("Phone");
		await Assert.That(cut.Find("#passkeys .acct-passkey-name .acct-flag").TextContent).IsEqualTo("AuthPasskeySynced");
		await Assert.That(cut.Find("#passkeys .acct-passkey-meta").TextContent).Contains("AuthPasskeyNeverUsed");
		await Assert.That(cut.Find("#passkeys button[aria-label='AuthPasskeyRemove']")).IsNotNull();
		await Assert.That(cut.FindAll("#passkeys button").Any(b => b.TextContent.Contains("AuthPasskeyAdd"))).IsTrue();
	}

	/// <summary>A browser with no passkey support says so, and offers nothing it cannot do.</summary>
	[Test]
	public async Task Render_LoggedIn_WithoutPasskeySupport_SaysSo()
	{
		Auth.SetAuthorized("headwiz");
		SeedAuthState(loggedIn: true, passkeysSupported: false);

		var cut = Render<SharpMUSH.Client.Pages.Account>();
		cut.WaitForAssertion(() => cut.Find("#passkeys .acct-empty"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find("#passkeys").TextContent).Contains("AuthPasskeyUnsupported");
		await Assert.That(cut.FindAll("#passkeys button").Any(b => b.TextContent.Contains("AuthPasskeyAdd"))).IsFalse();
	}

	/// <summary>
	/// Unlinking takes the character, and any role it gave the account, away at once (unlinking #1 left
	/// the administrator a Guest), so the button asks first; cancelling sends nothing.
	/// </summary>
	[Test]
	public async Task Unlink_AsksFirst_AndCancelLeavesTheCharacterLinked()
	{
		Auth.SetAuthorized("headwiz");
		var handler = new AccountPageApiHandler([new { dbrefNumber = 7, creationTime = 7L, name = "Ash", flags = "PLAYER" }]);
		SeedAuthState(loggedIn: true, handler: handler);

		var cut = Render(builder =>
		{
			builder.OpenComponent<MudBlazor.MudDialogProvider>(0);
			builder.CloseComponent();
			builder.OpenComponent<SharpMUSH.Client.Pages.Account>(1);
			builder.CloseComponent();
		});
		cut.WaitForAssertion(() => cut.Find("button[aria-label='AuthUnlinkCharacter']"), TimeSpan.FromSeconds(5));

		// The click waits on the confirmation this test answers below.
		await cut.StartClickAsync(cut.Find("button[aria-label='AuthUnlinkCharacter']"));
		cut.WaitForAssertion(() => cut.Find(".mud-dialog"), TimeSpan.FromSeconds(5));
		await Assert.That(handler.Unlinks).IsEqualTo(0).Because("nothing is unlinked before the player confirms");

		await cut.FindAll(".mud-dialog button").Single(b => b.TextContent.Trim() == "Cancel").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mud-dialog").Count > 0) throw new InvalidOperationException("dialog still open");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(handler.Unlinks).IsEqualTo(0);
	}

	/// <summary>
	/// Disposes the HttpClient(s) created for the substitute IHttpClientFactory. TUnit's disposer
	/// prefers IAsyncDisposable over IDisposable when a type implements both (as BunitContext
	/// does), so overriding only Dispose would never run — re-declare DisposeAsync to take over
	/// the interface's dispatch slot for this type; base.DisposeAsync() still runs to dispose
	/// bUnit's own service provider.
	/// </summary>
	public new async ValueTask DisposeAsync()
	{
		foreach (var client in ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
