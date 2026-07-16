using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>Fakes a successful api/auth/account-login round-trip for the logout-latch test below.</summary>
file sealed class FakeSuccessLoginHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new
			{
				accountId = "acct-1",
				username = "headwiz",
				characters = Array.Empty<object>(),
				accountSessionToken = "session-token-1",
				mustChangePassword = false,
				role = "God",
				permissions = new[] { "*" },
			})
		});
}

/// <summary>
/// Regression coverage for <see cref="AccountAuthService.InitAsync"/>'s null-session-token path.
/// sessionStorage (tab-scoped) is empty in a fresh tab while localStorage (survives tab closure)
/// may still hold a Username from a previous session. InitAsync must clear all in-memory auth
/// state in that case rather than restoring Username from localStorage — otherwise a returning
/// user in a new tab gets a phantom identity with no live session.
/// </summary>
public class AccountAuthServiceInitTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task InitAsync_NoSessionToken_ClearsStateEvenWhenLocalStorageHasUsername()
	{
		// sessionStorage has no AccountSessionToken...
		JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(null);
		// ...but localStorage still has a username from an earlier session in this browser.
		// If InitAsync ever regresses to reading this before checking the session token, this
		// setup makes that regression observable (Username would come back non-null below).
		JSInterop.Setup<string?>("localStorage.getItem", _ => true).SetResult("returning-user");

		var service = new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance);

		await service.InitAsync();

		await Assert.That(service.IsLoggedIn).IsFalse();
		await Assert.That(service.Username).IsNull();
		await Assert.That(service.Role).IsNull();
		await Assert.That(service.Permissions).IsEmpty();
	}

	/// <summary>
	/// Sticky-logout latch: once sessionStorage records an explicit logout, InitAsync must
	/// surface it as <see cref="AccountAuthService.ExplicitlyLoggedOut"/> so callers (MainLayout's
	/// dev-mode debug re-auth guard) can refuse to silently re-authenticate the user.
	/// </summary>
	[TUnit.Core.Test]
	public async Task InitAsync_LoggedOutFlagSet_ExposesExplicitlyLoggedOutTrue()
	{
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(bool.TrueString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		var service = new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance);

		await service.InitAsync();

		await Assert.That(service.ExplicitlyLoggedOut).IsTrue();
	}

	/// <summary>
	/// The latch must not be permanent: any successful login (or register/setup, which persist
	/// a session the same way) clears it, so the very next reload behaves normally again.
	/// </summary>
	[TUnit.Core.Test]
	public async Task LoginAsync_Success_ClearsExplicitlyLoggedOutFlag()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(bool.TrueString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		using var http = new HttpClient(new FakeSuccessLoginHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(httpClientFactory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		await service.InitAsync();
		await Assert.That(service.ExplicitlyLoggedOut).IsTrue();

		var (success, _, _) = await service.LoginAsync("headwiz", "password-one");

		await Assert.That(success).IsTrue();
		await Assert.That(service.ExplicitlyLoggedOut).IsFalse();
	}

	/// <summary>
	/// The logout latch must be enforced at this chokepoint, not only at call sites:
	/// <c>SharpMUSH.Client.Authentication.DebugAuthStateProvider</c> (and MainLayout before it) call
	/// <see cref="AccountAuthService.GetDebugOttAsync"/> on every auth-state query — including
	/// every F5 / CascadingAuthenticationState evaluation. Without this early return, that routine
	/// re-auth would reach the server, get a fresh debug OTT, and persist it via
	/// <c>PersistSessionAsync</c> — which clears <see cref="AccountAuthService.ExplicitlyLoggedOut"/>
	/// and re-populates the session, silently undoing an explicit logout on the very next reload.
	/// This test never sets up an HTTP handler at all: if the guard regresses, the call falls
	/// through to <c>httpClientFactory.CreateClient("api")</c> against an un-configured
	/// <see cref="Substitute"/>, which throws and the test fails loudly rather than silently
	/// passing for the wrong reason.
	/// </summary>
	[TUnit.Core.Test]
	public async Task GetDebugOttAsync_ExplicitlyLoggedOut_ReturnsNullWithoutCallingServer()
	{
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(bool.TrueString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		var service = new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance);

		await service.InitAsync();
		await Assert.That(service.ExplicitlyLoggedOut).IsTrue();

		var result = await service.GetDebugOttAsync();

		await Assert.That(result).IsNull();
		await Assert.That(service.ExplicitlyLoggedOut).IsTrue();
	}
}
