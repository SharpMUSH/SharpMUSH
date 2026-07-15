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
		public string? AccountSessionToken { get; set; }
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

	/// <summary>
	/// The provider itself no longer caches the debug OTT (that moved to
	/// <see cref="AccountAuthService.GetDebugOttAsync"/>'s own single-flight cache — see
	/// AccountAuthServiceInitTests/AccountAuthServiceDebugOttTests for that coverage); it simply
	/// delegates to <see cref="IAccountAuthState.GetDebugOttAsync"/> on every auth-state query. This
	/// fake doesn't cache, so each query here calls through — proving the provider doesn't hide a
	/// stale reference of its own.
	/// </summary>
	[Test]
	public async Task NotLoggedOut_DelegatesToAccountAuthServiceEachQuery()
	{
		var fake = new FakeAccountAuthState { ExplicitlyLoggedOut = false };
		var provider = new DebugAuthStateProvider(fake);

		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity!.IsAuthenticated).IsTrue();
		await Assert.That(state.User.IsInRole("Admin")).IsTrue();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(1);

		await provider.GetAuthenticationStateAsync();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(2);
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
	/// While latched, the provider's early return must skip the debug-OTT call entirely (not just
	/// rely on a cache), and once unlatched a fresh login must reach the service again rather than
	/// replaying a stale identity. (The actual stale-cache-clearing on logout now lives in
	/// <see cref="AccountAuthService.LogoutAsync"/>, which clears its own single-flight task; this
	/// fake has no cache of its own; both behaviors, provider early-return and service refetch,
	/// combine to give this end-to-end guarantee.)
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

		// Simulate a fresh login clearing the latch. Fire() itself kicks off one delegated query
		// via NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()) (call #2 below), and
		// since the provider no longer caches anything, the explicit query right after is a
		// genuinely separate delegated call too (call #3) — neither resurrects a stale identity,
		// which is the property this test exists to lock down.
		fake.ExplicitlyLoggedOut = false;
		fake.Fire();

		var reLoggedInState = await provider.GetAuthenticationStateAsync();
		await Assert.That(reLoggedInState.User.Identity!.IsAuthenticated).IsTrue();
		await Assert.That(fake.DebugOttCallCount).IsEqualTo(3);
	}
}
