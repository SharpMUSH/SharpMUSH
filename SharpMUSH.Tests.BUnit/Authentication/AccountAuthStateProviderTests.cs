using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Authentication;

public class AccountAuthStateProviderTests
{
	private static FakeAccountAuthState CreateAuthService(
		bool loggedIn, string? username = null, string? role = null, IReadOnlyList<string>? permissions = null) =>
		new() { IsLoggedIn = loggedIn, Username = username, Role = role, Permissions = permissions ?? [] };

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
