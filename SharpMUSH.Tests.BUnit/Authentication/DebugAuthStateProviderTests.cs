using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>
/// Regression coverage for the dev-logout state-consistency fix: <see cref="DebugAuthStateProvider"/>
/// must go anonymous (no cached OTT reuse, no static-fallback claims) whenever
/// <see cref="IAccountAuthState.ExplicitlyLoggedOut"/> is latched, and must push a live
/// notification through <see cref="Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider.AuthenticationStateChanged"/>
/// whenever the underlying account auth state raises <see cref="IAccountAuthState.AuthStateChanged"/> —
/// this is what makes every <c>AuthorizeView</c> (nav items, the sidebar's bottom-left account
/// indicator, the topbar account chip) flip live on logout/login instead of waiting for an
/// unrelated re-render.
/// </summary>
public class DebugAuthStateProviderTests
{
	/// <summary>
	/// Fake <see cref="IAccountAuthState"/> that never actually reaches an HTTP/JS-backed
	/// <see cref="AccountAuthService"/> — this is the narrow-interface seam the provider now
	/// depends on (it used to hard-depend on the concrete service, which made this fake
	/// impossible to construct it against).
	/// </summary>
	private sealed class FakeAccountAuthState : IAccountAuthState
	{
		public bool IsLoggedIn { get; set; }
		public string? Username { get; set; }
		public string? Role { get; set; }
		public IReadOnlyList<string> Permissions { get; set; } = [];
		public bool ExplicitlyLoggedOut { get; set; }
		public event Action? AuthStateChanged;
		public void Fire() => AuthStateChanged?.Invoke();

		/// <summary>No-op: this fake is always constructed already "hydrated" via its properties.</summary>
		public Task InitAsync() => Task.CompletedTask;

		/// <summary>How many times the provider actually called through for a debug OTT.</summary>
		public int DebugOttCallCount { get; private set; }

		public AccountAuthService.DebugOttResponse? NextDebugOtt { get; set; } =
			new("token", 900, "God", "acct-1", "headwiz", "session-token", false);

		public Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync()
		{
			DebugOttCallCount++;
			return Task.FromResult(NextDebugOtt);
		}
	}

	[Test]
	public async Task ExplicitlyLoggedOut_ReturnsAnonymous_WithoutFetchingDebugOtt()
	{
		var fake = new FakeAccountAuthState { ExplicitlyLoggedOut = true };
		var provider = new DebugAuthStateProvider(fake);

		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity?.IsAuthenticated ?? false).IsFalse();
		await Assert.That(state.User.Claims).IsEmpty();
		// The chokepoint fix lives in AccountAuthService.GetDebugOttAsync itself, but the
		// provider must not even call through once latched — no cached-OTT reuse, no
		// static-sentinel "DebugAdmin" fallback claims.
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(0);
	}

	[Test]
	public async Task NotLoggedOut_StillFetchesAndCachesDebugOtt()
	{
		var fake = new FakeAccountAuthState { ExplicitlyLoggedOut = false };
		var provider = new DebugAuthStateProvider(fake);

		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity!.IsAuthenticated).IsTrue();
		await Assert.That(state.User.IsInRole("Admin")).IsTrue();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(1);

		// Cached for the lifetime of the instance — a second call must not re-fetch.
		await provider.GetAuthenticationStateAsync();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(1);
	}

	[Test]
	public async Task AuthStateChanged_NotifiesSubscribers()
	{
		var fake = new FakeAccountAuthState();
		var provider = new DebugAuthStateProvider(fake);

		var notified = false;
		provider.AuthenticationStateChanged += _ => notified = true;

		fake.Fire();

		await Assert.That(notified).IsTrue();
	}

	/// <summary>
	/// Regression coverage for the "Game is starting up" gate: when the server is unreachable (or
	/// the bootstrap account doesn't exist yet) <see cref="AccountAuthService.GetDebugOttAsync"/>
	/// returns null, and the provider must answer with a genuinely anonymous principal — no
	/// claims at all — rather than the old static "DebugAdmin" / "debug-bootstrap-pending"
	/// sentinel identity. <c>ServerStartupGate</c> is what keeps this provider from being queried
	/// this early in the first place; this test locks down the provider's own fallback in case it
	/// still happens (e.g. a very late bootstrap race).
	/// </summary>
	[Test]
	public async Task ServerUnreachable_NullDebugOtt_ReturnsAnonymous_NotDebugAdmin()
	{
		var fake = new FakeAccountAuthState { ExplicitlyLoggedOut = false, NextDebugOtt = null };
		var provider = new DebugAuthStateProvider(fake);

		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity?.IsAuthenticated ?? false).IsFalse();
		await Assert.That(state.User.Claims).IsEmpty();
	}

	/// <summary>
	/// The logged-out transition must drop any cached debug identity so a later re-login in the
	/// same tab re-fetches from the server instead of resurrecting the pre-logout OTT response.
	/// </summary>
	[Test]
	public async Task LoggedOutTransition_ClearsCache_SoNextLoginRefetches()
	{
		var fake = new FakeAccountAuthState { ExplicitlyLoggedOut = false };
		var provider = new DebugAuthStateProvider(fake);

		await provider.GetAuthenticationStateAsync();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(1);

		// Simulate LogoutAsync latching the flag and raising AuthStateChanged.
		fake.ExplicitlyLoggedOut = true;
		fake.Fire();

		var loggedOutState = await provider.GetAuthenticationStateAsync();
		await Assert.That(loggedOutState.User.Identity?.IsAuthenticated ?? false).IsFalse();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(1); // still not re-fetched while latched

		// Simulate a fresh login clearing the latch.
		fake.ExplicitlyLoggedOut = false;
		fake.Fire();

		var reLoggedInState = await provider.GetAuthenticationStateAsync();
		await Assert.That(reLoggedInState.User.Identity!.IsAuthenticated).IsTrue();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(2); // re-fetched, not resurrected from cache
	}
}
