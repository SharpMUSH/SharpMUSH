using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// A minimal, fully mutable fake of <see cref="IAccountAuthState"/> — lets a test flip
/// <see cref="AccountSessionToken"/> after a consumer (e.g. <see cref="GameHubConnectionFactory"/>)
/// has already been constructed against it, to prove that consumer reads the property live rather
/// than capturing a snapshot.
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
	public Task InitAsync() => Task.CompletedTask;
	public Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync() =>
		Task.FromResult<AccountAuthService.DebugOttResponse?>(null);

	// Keep the compiler from warning about the never-invoked event on this test double.
	public void Touch() => AuthStateChanged?.Invoke();
}

/// <summary>
/// Task 9 (auth-consolidation, Phase 2): pins the client-side contract that the SignalR game-hub
/// connection authenticates with the live account-session token (never a JWT — Task 8 retired JWT
/// server-side), and that character switching goes through the session-based
/// <c>POST api/auth/switch-character</c> endpoint (Task 7), not the retired <c>jwt-switch-character</c>
/// flow.
/// </summary>
public class AccountAuthServiceHubTokenTests : BunitContext
{
	/// <summary>
	/// Core requirement from the task brief: the hub's <c>AccessTokenProvider</c> must read the
	/// CURRENT account-session token on each (re)connect, not a value captured once at build time.
	/// <see cref="GameHubConnectionFactory"/> cannot be asserted on via SignalR's own
	/// <see cref="Microsoft.AspNetCore.SignalR.Client.HubConnection"/> (it does not expose the
	/// configured provider), so this drives the same delegate SignalR would invoke —
	/// <see cref="GameHubConnectionFactory.ResolveAccessTokenAsync"/> — directly, and proves it
	/// reflects a token change that happens AFTER the factory was constructed (i.e. after a
	/// logout/re-login in the same tab), which a captured-string snapshot could never do.
	/// </summary>
	[Test]
	public async Task ResolveAccessTokenAsync_ReflectsLiveAccountSessionToken_NotBuildTimeSnapshot()
	{
		var accountAuth = new FakeAccountAuthState { AccountSessionToken = "session-token-1" };
		var factory = new GameHubConnectionFactory("https://localhost/hubs/game", accountAuth);

		await Assert.That(await factory.ResolveAccessTokenAsync()).IsEqualTo("session-token-1");

		// Simulate a session refresh/re-login happening after the factory exists (it is registered
		// as a long-lived singleton in Program.cs, built once at startup).
		accountAuth.AccountSessionToken = "session-token-2";

		await Assert.That(await factory.ResolveAccessTokenAsync()).IsEqualTo("session-token-2");
	}

	/// <summary>
	/// When the session ends (logout), the live read must surface <c>null</c> immediately — a
	/// snapshot would keep re-offering the stale, now-invalid token to the hub on every automatic
	/// reconnect attempt.
	/// </summary>
	[Test]
	public async Task ResolveAccessTokenAsync_AfterLogout_ReturnsNull()
	{
		var accountAuth = new FakeAccountAuthState { AccountSessionToken = "session-token-1" };
		var factory = new GameHubConnectionFactory("https://localhost/hubs/game", accountAuth);

		accountAuth.AccountSessionToken = null;

		await Assert.That(await factory.ResolveAccessTokenAsync()).IsNull();
	}

	/// <summary>Captures the outgoing request so the test can assert method/URL/headers/body.</summary>
	private sealed class CapturingHandler(HttpStatusCode status, object? responseBody) : HttpMessageHandler
	{
		public HttpRequestMessage? LastRequest { get; private set; }
		public string? LastBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastRequest = request;
			LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			return new HttpResponseMessage(status)
			{
				Content = responseBody is null ? null : JsonContent.Create(responseBody)
			};
		}
	}

	/// <summary>
	/// Character switching must go through <c>POST api/auth/switch-character</c> (Task 7's
	/// session-based replacement for <c>jwt-switch-character</c>), authenticated via the
	/// <c>AccountSession</c> scheme — i.e. a Bearer header carrying the account-session token, the
	/// same way every other authenticated account-service call in this file does it — and must
	/// return the OTT from the response.
	/// </summary>
	[Test]
	public async Task SwitchCharacterAsync_PostsToSwitchCharacterEndpoint_WithBearerAuth_ReturnsOtt()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");

		var handler = new CapturingHandler(HttpStatusCode.OK, new { ott = "one-time-token", expiresIn = 60 });
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(httpClientFactory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		var character = new AccountAuthService.CharacterSummary(42, 12345L, "Bob", "");

		var ott = await service.SwitchCharacterAsync(character);

		await Assert.That(ott).IsEqualTo("one-time-token");
		await Assert.That(handler.LastRequest).IsNotNull();
		await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
		await Assert.That(handler.LastRequest.RequestUri!.AbsolutePath).IsEqualTo("/api/auth/switch-character");
		await Assert.That(handler.LastRequest.Headers.Authorization?.Scheme).IsEqualTo("Bearer");
		await Assert.That(handler.LastRequest.Headers.Authorization?.Parameter).IsEqualTo("session-token-1");
		// The switch-character endpoint authenticates via the Bearer header (AccountSession scheme),
		// not a token embedded in the body — unlike the older mush-token request shape.
		await Assert.That(handler.LastBody).Contains("\"characterKey\":42");
		await Assert.That(handler.LastBody).Contains("\"characterCreationTime\":12345");
	}

	[Test]
	public async Task SwitchCharacterAsync_NotLoggedIn_ReturnsNullWithoutCallingServer()
	{
		JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(null);

		var service = new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance);
		var character = new AccountAuthService.CharacterSummary(42, 12345L, "Bob", "");

		// No HTTP handler configured at all: if the not-logged-in guard regresses, the call falls
		// through to httpClientFactory.CreateClient("api") against an un-configured Substitute,
		// which throws and fails the test loudly rather than silently passing for the wrong reason.
		var ott = await service.SwitchCharacterAsync(character);

		await Assert.That(ott).IsNull();
	}

	/// <summary>
	/// Task 8 retired JWT server-side; this pins that the client never grew JWT or refresh-token
	/// plumbing back — no public/private member on <see cref="AccountAuthService"/> whose name
	/// mentions "Jwt" or "Refresh" (case-insensitive), covering fields, properties, methods, and
	/// nested record types (which reflection surfaces as nested types).
	/// </summary>
	[Test]
	public async Task AccountAuthService_HasNoJwtOrRefreshTokenMembers()
	{
		var type = typeof(AccountAuthService);
		const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic
			| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		var offendingMembers = type.GetMembers(allMembers)
			.Concat(type.GetNestedTypes(allMembers))
			.Select(m => m.Name)
			.Where(name => name.Contains("Jwt", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("Refresh", StringComparison.OrdinalIgnoreCase))
			.ToList();

		await Assert.That(offendingMembers).IsEmpty();
	}
}
