using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.BUnit.Authentication;

public class AccountAuthStateProviderTests
{
	/// <summary>Tiny fake so the provider can be tested without a real HTTP/JS-backed <see cref="AccountAuthService"/>.</summary>
	private sealed class FakeAccountAuthState(
		bool isLoggedIn, string? username, string? role, IReadOnlyList<string> permissions) : IAccountAuthState
	{
		public bool IsLoggedIn { get; } = isLoggedIn;
		public string? AccountSessionToken { get; set; }
		public string? Username { get; } = username;
		public string? Role { get; } = role;
		public IReadOnlyList<string> Permissions { get; } = permissions;
		public bool ExplicitlyLoggedOut { get; set; }
		public event Action? AuthStateChanged;
		public AccountAuthService.CharacterSummary? ActiveCharacter { get; set; }
		public bool HasCharacters => false;
		public bool CanUseTerminal => false;
		public event Action? ActiveCharacterChanged;
		public void Fire()
		{
			AuthStateChanged?.Invoke();
			ActiveCharacterChanged?.Invoke();
		}

		/// <summary>No-op: this fake is always constructed already "hydrated" via its constructor args.</summary>
		public Task InitAsync() => Task.CompletedTask;

		public Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync() =>
			Task.FromResult<AccountAuthService.DebugOttResponse?>(null);
	}

	private static FakeAccountAuthState CreateAuthService(
		bool loggedIn, string? username = null, string? role = null, IReadOnlyList<string>? permissions = null) =>
		new(loggedIn, username, role, permissions ?? []);

	[Test]
	public async Task LoggedOut_ReturnsAnonymous()
	{
		var provider = new AccountAuthStateProvider(CreateAuthService(loggedIn: false));
		var state = await provider.GetAuthenticationStateAsync();
		await Assert.That(state.User.Identity?.IsAuthenticated ?? false).IsFalse();
	}

	[Test]
	public async Task LoggedIn_EmitsRoleAndPermissionClaims()
	{
		var provider = new AccountAuthStateProvider(
			CreateAuthService(loggedIn: true, username: "headwiz", role: "Wizard", permissions: ["players.view", "players.moderate"]));
		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity!.IsAuthenticated).IsTrue();
		await Assert.That(state.User.IsInRole("Wizard")).IsTrue();
		await Assert.That(state.User.HasClaim(PortalPermission.ClaimType, "players.moderate")).IsTrue();
	}

	[Test]
	public async Task LoggedIn_MissingRole_FallsBackToGuest_NotPlayer()
	{
		var provider = new AccountAuthStateProvider(CreateAuthService(loggedIn: true, username: "newacct", role: null));
		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity!.IsAuthenticated).IsTrue();
		await Assert.That(state.User.IsInRole(nameof(PortalRole.Guest))).IsTrue();
		await Assert.That(state.User.IsInRole(nameof(PortalRole.Player))).IsFalse();
	}

	[Test]
	public async Task AuthStateChanged_TriggersProviderNotification()
	{
		var fake = CreateAuthService(loggedIn: false);
		var provider = new AccountAuthStateProvider(fake);

		var notified = false;
		provider.AuthenticationStateChanged += _ => notified = true;

		fake.Fire();

		await Assert.That(notified).IsTrue();
	}
}
