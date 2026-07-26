using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>Captures the outgoing request so a test can assert what the handler forwarded.</summary>
file sealed class CapturingHandler : HttpMessageHandler
{
	public HttpRequestMessage? LastRequest { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		LastRequest = request;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
	}
}

/// <summary>
/// The session token lives in sessionStorage until <see cref="AccountAuthService.InitAsync"/> copies
/// it into memory, so a request issued during that window used to go out with no bearer and come back
/// 401. Callers happen to hydrate first today — an <c>[Authorize]</c> page waits on
/// <c>AccountAuthStateProvider</c>, which awaits <c>InitAsync</c>; <c>MainLayout</c>,
/// <c>GlobalTerminal</c>, <c>Account</c> and <c>GetCharactersAsync</c> call it themselves — but that is
/// convention, not enforcement, and a caller that forgets fails intermittently on refresh rather than
/// loudly. These tests use the real <see cref="AccountAuthService"/> over bUnit's JSInterop so they
/// exercise the actual storage read, not a fake that is already "hydrated".
/// </summary>
public class AccountSessionBearerHandlerHydrationTests : BunitContext
{
	private AccountAuthService ServiceWithStoredSession(string? storedToken)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(storedToken);

		return new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(),
			Substitute.For<IPlayTerminalService>());
	}

	private static async Task<HttpRequestMessage?> SendAsync(IAccountAuthState auth, string url)
	{
		var inner = new CapturingHandler();
		using var handler = new AccountSessionBearerHandler(auth) { InnerHandler = inner };
		using var invoker = new HttpMessageInvoker(handler);
		await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);
		return inner.LastRequest;
	}

	[Test]
	public async Task SendAsync_WithoutPriorInitAsync_HydratesAndAttachesStoredToken()
	{
		var auth = ServiceWithStoredSession("stored-session-token");

		// Nobody has awaited InitAsync — this is a request racing a page refresh's hydration.
		var sent = await SendAsync(auth, "https://localhost/api/mail");

		await Assert.That(sent!.Headers.Authorization?.Scheme).IsEqualTo("Bearer");
		await Assert.That(sent.Headers.Authorization?.Parameter).IsEqualTo("stored-session-token");
	}

	[Test]
	public async Task SendAsync_WithHydrationAlreadyInFlight_StillAttachesToken()
	{
		var auth = ServiceWithStoredSession("stored-session-token");

		// CascadingAuthenticationState kicks hydration off without anyone awaiting it; the handler must
		// join that in-flight single-flight task rather than read the not-yet-populated token.
		_ = auth.InitAsync();
		var sent = await SendAsync(auth, "https://localhost/api/mail");

		await Assert.That(sent!.Headers.Authorization?.Parameter).IsEqualTo("stored-session-token");
	}

	[Test]
	public async Task SendAsync_NoStoredSession_StaysAnonymous()
	{
		var auth = ServiceWithStoredSession(null);

		var sent = await SendAsync(auth, "https://localhost/api/wiki");

		// Hydrating must not invent a credential for an anonymous visitor.
		await Assert.That(sent!.Headers.Authorization).IsNull();
	}
}
