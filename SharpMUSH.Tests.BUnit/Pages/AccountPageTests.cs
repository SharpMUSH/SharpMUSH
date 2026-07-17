using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Fakes the tiny slice of the account API that /account touches when it refreshes the
/// characters list on init (<c>api/account/characters</c>). Always returns an empty list —
/// these tests are about render-crash regressions, not characters-table content.
/// </summary>
file sealed class AccountPageApiHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Get && path == "api/account/characters")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(Array.Empty<object>())
			});

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
public class AccountPageTests : BunitContext, IAsyncDisposable
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
	private void SeedAuthState(bool loggedIn, bool mustChangePassword = false)
	{
		var apiClient = new HttpClient(new AccountPageApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		ownedHttpClients.Add(apiClient);

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance));

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);

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

	[TUnit.Core.Test]
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
		await Assert.That(cut.Markup).Contains("Profile");
	}

	[TUnit.Core.Test]
	public async Task Render_LoggedIn_MustChangePassword_DoesNotThrow()
	{
		SeedAuthState(loggedIn: true, mustChangePassword: true);

		var cut = Render<SharpMUSH.Client.Pages.Account>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Password change required"))
				throw new InvalidOperationException("must-change-password banner not rendered yet");
		});
		await Assert.That(cut.Markup).Contains("Password change required");
		// The Profile/Characters sections are gated off while a password change is pending.
		await Assert.That(cut.Markup).DoesNotContain("Characters");
	}

	[TUnit.Core.Test]
	public async Task Render_LoggedOut_DoesNotThrow()
	{
		SeedAuthState(loggedIn: false);

		var cut = Render<SharpMUSH.Client.Pages.Account>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("not logged in"))
				throw new InvalidOperationException("logged-out card not rendered yet");
		});
		await Assert.That(cut.Markup).Contains("not logged in");
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
