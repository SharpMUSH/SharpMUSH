using System.Net;
using System.Net.Http.Headers;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// A minimal, mutable fake of <see cref="IAccountAuthState"/> so a test can flip
/// <see cref="AccountSessionToken"/> after the handler has been constructed, proving the handler
/// reads the token live per request rather than capturing it once.
/// </summary>
file sealed class FakeAccountAuthState : IAccountAuthState
{
	public bool IsLoggedIn => AccountSessionToken is not null;
	public string? AccountSessionToken { get; set; }
	public string? Username { get; set; }
	public string? Role { get; set; }
	public IReadOnlyList<string> Permissions { get; set; } = [];
	public bool ExplicitlyLoggedOut { get; set; }
	public event Action? AuthStateChanged;
	public AccountAuthService.CharacterSummary? ActiveCharacter { get; set; }
	public event Action? ActiveCharacterChanged;
	public Task InitAsync() => Task.CompletedTask;
	public Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync() =>
		Task.FromResult<AccountAuthService.DebugOttResponse?>(null);

	public void Touch()
	{
		AuthStateChanged?.Invoke();
		ActiveCharacterChanged?.Invoke();
	}
}

/// <summary>Captures the outgoing request so a test can assert what the handler forwarded.</summary>
file sealed class CapturingInnerHandler : HttpMessageHandler
{
	public HttpRequestMessage? LastRequest { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		LastRequest = request;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
	}
}

/// <summary>
/// Pins the client-side contract that authenticated portal REST calls carry the account-session
/// bearer token. Before this handler, 18 of 20 client data services attached no token and only
/// worked because the dev server's DebugAuth scheme auto-authenticated every request; under the
/// production AccountSession scheme those calls 401. The handler attaches the token uniformly so
/// every service authenticates without per-call code.
/// </summary>
public class AccountSessionBearerHandlerTests
{
	private static async Task<HttpRequestMessage?> SendAsync(
		IAccountAuthState auth, HttpRequestMessage request)
	{
		var inner = new CapturingInnerHandler();
		using var handler = new AccountSessionBearerHandler(auth) { InnerHandler = inner };
		using var invoker = new HttpMessageInvoker(handler);
		await invoker.SendAsync(request, CancellationToken.None);
		return inner.LastRequest;
	}

	[Test]
	public async Task SendAsync_WithToken_AttachesBearerHeader()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = "session-token-1" };

		var sent = await SendAsync(auth, new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/mail"));

		await Assert.That(sent!.Headers.Authorization?.Scheme).IsEqualTo("Bearer");
		await Assert.That(sent.Headers.Authorization?.Parameter).IsEqualTo("session-token-1");
	}

	[Test]
	public async Task SendAsync_WithoutToken_LeavesRequestAnonymous()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = null };

		var sent = await SendAsync(auth, new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/wiki"));

		// Anonymous browsing (public wiki, character directory) must keep working — no header added.
		await Assert.That(sent!.Headers.Authorization).IsNull();
	}

	[Test]
	public async Task SendAsync_WithExistingAuthHeader_DoesNotOverwrite()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = "handler-token" };
		var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/auth/switch-character")
		{
			Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "explicit-token") }
		};

		var sent = await SendAsync(auth, request);

		// A call that set its own header on purpose wins; the handler must not clobber it.
		await Assert.That(sent!.Headers.Authorization?.Parameter).IsEqualTo("explicit-token");
	}

	[Test]
	public async Task SendAsync_ReadsTokenLive_NotCapturedAtConstruction()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = null };
		var inner = new CapturingInnerHandler();
		using var handler = new AccountSessionBearerHandler(auth) { InnerHandler = inner };
		using var invoker = new HttpMessageInvoker(handler);

		// Log in AFTER the handler exists (it is a long-lived singleton built once at startup).
		auth.AccountSessionToken = "logged-in-later";
		await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/mail"), CancellationToken.None);

		await Assert.That(inner.LastRequest!.Headers.Authorization?.Parameter).IsEqualTo("logged-in-later");
	}
}
