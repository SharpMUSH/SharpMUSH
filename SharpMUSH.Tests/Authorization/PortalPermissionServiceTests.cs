using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Authorization;

public class PortalPermissionServiceTests
{
	private readonly Mock<IAccountService> _accountServiceMock = new();
	private IPortalPermissionService _permissionService = null!;

	[SetUp]
	public void Setup()
	{
		_permissionService = new PortalPermissionService(_accountServiceMock.Object);
	}

	#region GetRoleFromFlags Tests

	[Test]
	public void GetRoleFromFlags_WithGodFlag_ReturnsGod()
	{
		var flags = new[] { "WIZARD", "GOD", "ROYALTY" };
		var result = _permissionService.GetRoleFromFlags(flags, isGodCharacter: false);
		Assert.That(result).IsEqualTo(PortalRole.God);
	}

	[Test]
	public void GetRoleFromFlags_WithGodCharacter_ReturnsGod()
	{
		var flags = Array.Empty<string>();
		var result = _permissionService.GetRoleFromFlags(flags, isGodCharacter: true);
		Assert.That(result).IsEqualTo(PortalRole.God);
	}

	[Test]
	public void GetRoleFromFlags_WithWizardFlag_ReturnsWizard()
	{
		var flags = new[] { "WIZARD", "ROYALTY" };
		var result = _permissionService.GetRoleFromFlags(flags, isGodCharacter: false);
		Assert.That(result).IsEqualTo(PortalRole.Wizard);
	}

	[Test]
	public void GetRoleFromFlags_WithRoyaltyFlag_ReturnsRoyalty()
	{
		var flags = new[] { "ROYALTY", "QUIET" };
		var result = _permissionService.GetRoleFromFlags(flags, isGodCharacter: false);
		Assert.That(result).IsEqualTo(PortalRole.Royalty);
	}

	[Test]
	public void GetRoleFromFlags_WithoutSpecialFlags_ReturnsPlayer()
	{
		var flags = new[] { "QUIET", "CONNECTED" };
		var result = _permissionService.GetRoleFromFlags(flags, isGodCharacter: false);
		Assert.That(result).IsEqualTo(PortalRole.Player);
	}

	[Test]
	public void GetRoleFromFlags_WithEmptyFlags_ReturnsGuest()
	{
		var flags = Array.Empty<string>();
		var result = _permissionService.GetRoleFromFlags(flags, isGodCharacter: false);
		Assert.That(result).IsEqualTo(PortalRole.Guest);
	}

	[Test]
	public void GetRoleFromFlags_CaseInsensitiveFlags()
	{
		var flags = new[] { "wizard", "royalty", "god" };
		var result = _permissionService.GetRoleFromFlags(flags, isGodCharacter: false);
		// Should match regardless of case; GOD wins
		Assert.That(result).IsEqualTo(PortalRole.God);
	}

	#endregion

	#region GetAccountRoleAsync Tests

	[Test]
	public async ValueTask GetAccountRoleAsync_NoCharacters_ReturnsGuest()
	{
		_accountServiceMock
			.Setup(s => s.GetCharactersAsync("account1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SharpPlayer>());

		var result = await _permissionService.GetAccountRoleAsync("account1");

		Assert.That(result).IsEqualTo(PortalRole.Guest);
	}

	[Test]
	public async ValueTask GetAccountRoleAsync_SinglePlayerCharacter_ReturnsPlayer()
	{
		var player = CreateMockPlayer(objectId: 2, flagNames: new[] { "QUIET" });

		_accountServiceMock
			.Setup(s => s.GetCharactersAsync("account1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SharpPlayer> { player });

		var result = await _permissionService.GetAccountRoleAsync("account1");

		Assert.That(result).IsEqualTo(PortalRole.Player);
	}

	[Test]
	public async ValueTask GetAccountRoleAsync_RoyaltyCharacter_ReturnsRoyalty()
	{
		var player = CreateMockPlayer(objectId: 3, flagNames: new[] { "ROYALTY" });

		_accountServiceMock
			.Setup(s => s.GetCharactersAsync("account1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SharpPlayer> { player });

		var result = await _permissionService.GetAccountRoleAsync("account1");

		Assert.That(result).IsEqualTo(PortalRole.Royalty);
	}

	[Test]
	public async ValueTask GetAccountRoleAsync_WizardCharacter_ReturnsWizard()
	{
		var player = CreateMockPlayer(objectId: 4, flagNames: new[] { "WIZARD" });

		_accountServiceMock
			.Setup(s => s.GetCharactersAsync("account1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SharpPlayer> { player });

		var result = await _permissionService.GetAccountRoleAsync("account1");

		Assert.That(result).IsEqualTo(PortalRole.Wizard);
	}

	[Test]
	public async ValueTask GetAccountRoleAsync_GodObject_ReturnsGod()
	{
		var player = CreateMockPlayer(objectId: 1, flagNames: Array.Empty<string>());

		_accountServiceMock
			.Setup(s => s.GetCharactersAsync("account1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SharpPlayer> { player });

		var result = await _permissionService.GetAccountRoleAsync("account1");

		Assert.That(result).IsEqualTo(PortalRole.God);
	}

	[Test]
	public async ValueTask GetAccountRoleAsync_MultipleCharacters_ReturnsHighestRole()
	{
		var player1 = CreateMockPlayer(objectId: 2, flagNames: new[] { "QUIET" }); // Player
		var player2 = CreateMockPlayer(objectId: 3, flagNames: new[] { "ROYALTY" }); // Royalty
		var player3 = CreateMockPlayer(objectId: 4, flagNames: new[] { "WIZARD", "QUIET" }); // Wizard

		_accountServiceMock
			.Setup(s => s.GetCharactersAsync("account1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SharpPlayer> { player1, player2, player3 });

		var result = await _permissionService.GetAccountRoleAsync("account1");

		Assert.That(result).IsEqualTo(PortalRole.Wizard);
	}

	#endregion

	#region HasPermission Tests

	[Test]
	public void HasPermission_GuestRole_NoPermissions()
	{
		var result = _permissionService.HasPermission(PortalRole.Guest, Permission.ViewAdminPanel);
		Assert.That(result).IsFalse();
	}

	[Test]
	public void HasPermission_PlayerRole_ViewAdminPanel()
	{
		var result = _permissionService.HasPermission(PortalRole.Player, Permission.ViewAdminPanel);
		Assert.That(result).IsTrue();
	}

	[Test]
	public void HasPermission_PlayerRole_CannotManageAccounts()
	{
		var result = _permissionService.HasPermission(PortalRole.Player, Permission.ManageAccounts);
		Assert.That(result).IsFalse();
	}

	[Test]
	public void HasPermission_RoyaltyRole_ManageAccounts()
	{
		var result = _permissionService.HasPermission(PortalRole.Royalty, Permission.ManageAccounts);
		Assert.That(result).IsTrue();
	}

	[Test]
	public void HasPermission_WizardRole_DeleteAccounts()
	{
		var result = _permissionService.HasPermission(PortalRole.Wizard, Permission.DeleteAccounts);
		Assert.That(result).IsTrue();
	}

	[Test]
	public void HasPermission_GodRole_SuperAdmin()
	{
		var result = _permissionService.HasPermission(PortalRole.God, Permission.SuperAdmin);
		Assert.That(result).IsTrue();
	}

	#endregion

	#region HasAllPermissions Tests

	[Test]
	public void HasAllPermissions_RoyaltyRole_AllRoyaltyPermissions()
	{
		var result = _permissionService.HasAllPermissions(
			PortalRole.Royalty,
			Permission.ManageAccounts,
			Permission.CreateScene,
			Permission.EditWiki
		);
		Assert.That(result).IsTrue();
	}

	[Test]
	public void HasAllPermissions_RoyaltyRole_MissingWizardPermission()
	{
		var result = _permissionService.HasAllPermissions(
			PortalRole.Royalty,
			Permission.ManageAccounts,
			Permission.DeleteAccounts // Wizard only
		);
		Assert.That(result).IsFalse();
	}

	[Test]
	public void HasAllPermissions_WizardRole_AllOwnAndLowerPermissions()
	{
		var result = _permissionService.HasAllPermissions(
			PortalRole.Wizard,
			Permission.ManageAccounts, // Royalty
			Permission.DeleteAccounts, // Wizard
			Permission.ViewAdminPanel  // Player
		);
		Assert.That(result).IsTrue();
	}

	[Test]
	public void HasAllPermissions_GuestRole_EmptyPermissionsList()
	{
		var result = _permissionService.HasAllPermissions(PortalRole.Guest);
		Assert.That(result).IsTrue(); // All (zero) permissions present
	}

	#endregion

	#region HasAnyPermission Tests

	[Test]
	public void HasAnyPermission_RoyaltyRole_AtLeastOneMatch()
	{
		var result = _permissionService.HasAnyPermission(
			PortalRole.Royalty,
			Permission.ManageServer,  // God only
			Permission.ManageAccounts // Royalty
		);
		Assert.That(result).IsTrue();
	}

	[Test]
	public void HasAnyPermission_PlayerRole_NoMatches()
	{
		var result = _permissionService.HasAnyPermission(
			PortalRole.Player,
			Permission.ManageAccounts,
			Permission.DeleteAccounts
		);
		Assert.That(result).IsFalse();
	}

	[Test]
	public void HasAnyPermission_WizardRole_AllMatches()
	{
		var result = _permissionService.HasAnyPermission(
			PortalRole.Wizard,
			Permission.CreateScene,
			Permission.ManageWiki,
			Permission.ManagePortalSettings
		);
		Assert.That(result).IsTrue();
	}

	#endregion

	#region Role Hierarchy Tests

	[Test]
	public void RoleHierarchy_PlayerInheritsGuestPermissions()
	{
		var guestPerms = _permissionService.GetPermissionsForRole(PortalRole.Guest);
		var playerPerms = _permissionService.GetPermissionsForRole(PortalRole.Player);

		foreach (var perm in guestPerms)
		{
			Assert.That(playerPerms).Contains(perm, $"Player should inherit Guest permission: {perm}");
		}
	}

	[Test]
	public void RoleHierarchy_RoyaltyInheritsPlayerPermissions()
	{
		var playerPerms = _permissionService.GetPermissionsForRole(PortalRole.Player);
		var royaltyPerms = _permissionService.GetPermissionsForRole(PortalRole.Royalty);

		foreach (var perm in playerPerms)
		{
			Assert.That(royaltyPerms).Contains(perm, $"Royalty should inherit Player permission: {perm}");
		}
	}

	[Test]
	public void RoleHierarchy_WizardInheritsRoyaltyPermissions()
	{
		var royaltyPerms = _permissionService.GetPermissionsForRole(PortalRole.Royalty);
		var wizardPerms = _permissionService.GetPermissionsForRole(PortalRole.Wizard);

		foreach (var perm in royaltyPerms)
		{
			Assert.That(wizardPerms).Contains(perm, $"Wizard should inherit Royalty permission: {perm}");
		}
	}

	[Test]
	public void RoleHierarchy_GodInheritsWizardPermissions()
	{
		var wizardPerms = _permissionService.GetPermissionsForRole(PortalRole.Wizard);
		var godPerms = _permissionService.GetPermissionsForRole(PortalRole.God);

		foreach (var perm in wizardPerms)
		{
			Assert.That(godPerms).Contains(perm, $"God should inherit Wizard permission: {perm}");
		}
	}

	[Test]
	public void PermissionCount_IncreaseWithRoles()
	{
		var guestCount = _permissionService.GetPermissionsForRole(PortalRole.Guest).Count;
		var playerCount = _permissionService.GetPermissionsForRole(PortalRole.Player).Count;
		var royaltyCount = _permissionService.GetPermissionsForRole(PortalRole.Royalty).Count;
		var wizardCount = _permissionService.GetPermissionsForRole(PortalRole.Wizard).Count;
		var godCount = _permissionService.GetPermissionsForRole(PortalRole.God).Count;

		Assert.That(guestCount).IsLessThanOrEqualTo(playerCount);
		Assert.That(playerCount).IsLessThanOrEqualTo(royaltyCount);
		Assert.That(royaltyCount).IsLessThanOrEqualTo(wizardCount);
		Assert.That(wizardCount).IsLessThanOrEqualTo(godCount);
	}

	#endregion

	#region GetPermissionsForRole Tests

	[Test]
	public void GetPermissionsForRole_ReturnsHashSet()
	{
		var result = _permissionService.GetPermissionsForRole(PortalRole.Wizard);
		Assert.That(result).IsInstanceOf<HashSet<Permission>>();
	}

	[Test]
	public void GetPermissionsForRole_GodHasAllPermissions()
	{
		var godPerms = _permissionService.GetPermissionsForRole(PortalRole.God);

		// God should have all defined permissions (except None which is 0)
		Assert.That(godPerms).Contains(Permission.ViewAdminPanel);
		Assert.That(godPerms).Contains(Permission.ManageServer);
		Assert.That(godPerms).Contains(Permission.ManageDatabase);
		Assert.That(godPerms).Contains(Permission.SuperAdmin);
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Creates a mock SharpPlayer with the specified object ID and flag names.
	/// </summary>
	private static SharpPlayer CreateMockPlayer(int objectId, string[] flagNames)
	{
		// Create mock SharpObjectFlag instances for each flag name
		var flags = flagNames.Select(name => new Mock<SharpObjectFlag>()
		{
			DefaultValue = DefaultValue.Mock
		}.SetupGet(f => f.Name).Returns(name).Object).ToList();

		// Create an async enumerable from the flag list
		var flagsAsyncEnumerable = flags.ToAsyncEnumerable();

		// Mock the Lazy<IAsyncEnumerable<SharpObjectFlag>>
		var lazyFlagsMock = new Mock<Lazy<IAsyncEnumerable<SharpObjectFlag>>>();
		lazyFlagsMock.Setup(l => l.Value).Returns(flagsAsyncEnumerable);

		// Mock SharpObject
		var objectMock = new Mock<SharpObject>();
		objectMock.Setup(o => o.DBRef.Id).Returns(objectId);
		objectMock.Setup(o => o.Flags).Returns(lazyFlagsMock.Object);

		// Mock SharpPlayer
		var playerMock = new Mock<SharpPlayer>();
		playerMock.Setup(p => p.Object).Returns(objectMock.Object);

		return playerMock.Object;
	}

	#endregion
}
