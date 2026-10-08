using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Authentication;

/// <summary><see cref="BuiltInRoles.SeedChanges"/>, which the database migration writes, and the defaults it seeds.</summary>
public class BuiltInRoleSeedTests
{
	[Test]
	public async Task NewWorldGetsSystemAndStarterRoles()
	{
		var changes = BuiltInRoles.SeedChanges([], 1);
		await Assert.That(changes.Select(r => r.Slug)).IsEquivalentTo(BuiltInRoles.All.Concat(BuiltInRoles.Starters).Select(r => r.Slug));
		await Assert.That(changes.Single(r => r.Slug == "moderator").IsSystem).IsFalse();
	}

	[Test]
	public async Task ExistingWorldKeepsEditsAndDeletedStarters()
	{
		var player = new SharpRole
		{
			Slug = "player", Name = "Citizen", Category = "Townsfolk", Priority = 10, IsSystem = true,
			Permissions = new() { [PortalPermission.WikiEdit] = PermissionState.Deny }
		};
		var changes = BuiltInRoles.SeedChanges([player], 1);
		await Assert.That(changes.Select(r => r.Slug)).DoesNotContain("player");
		await Assert.That(changes.Select(r => r.Slug)).Contains(BuiltInRoles.EveryoneSlug);
		await Assert.That(changes.Select(r => r.Slug)).DoesNotContain("moderator");
	}

	[Test]
	public async Task ExistingSystemRoleGainsNewGameScopesItLeavesOnInherit()
	{
		var wizard = new SharpRole
		{
			Slug = BuiltInRoles.WizardSlug, Name = "Archmage", Priority = 30, IsSystem = true, CreatedAt = 5,
			Permissions = new()
			{
				[PortalPermission.RolesAdmin] = PermissionState.Allow,
				[PortalPermission.ControlAll] = PermissionState.Deny
			}
		};
		var changed = BuiltInRoles.SeedChanges([wizard], 9).Single(r => r.Slug == BuiltInRoles.WizardSlug);
		await Assert.That(changed.Name).IsEqualTo("Archmage");
		await Assert.That(changed.CreatedAt).IsEqualTo(5);
		await Assert.That(changed.Permissions[PortalPermission.GameWizard]).IsEqualTo(PermissionState.Allow);
		// A game scope the game already decided on keeps its setting.
		await Assert.That(changed.Permissions[PortalPermission.ControlAll]).IsEqualTo(PermissionState.Deny);
		// Portal scopes are not added back.
		await Assert.That(changed.Permissions.ContainsKey(PortalPermission.WikiAdmin)).IsFalse();
	}

	[Test]
	public async Task SystemRoleWithoutACategoryIsPutInSystem()
	{
		var player = new SharpRole { Slug = "player", Name = "Citizen", Priority = 10, IsSystem = true, CreatedAt = 5 };
		var changed = BuiltInRoles.SeedChanges([player], 9).Single(r => r.Slug == "player");
		await Assert.That(changed.Category).IsEqualTo(Categories.System);
		await Assert.That(changed.Name).IsEqualTo("Citizen");
		await Assert.That(BuiltInRoles.SeedChanges([], 1).Single(r => r.Slug == "helper").Category).IsEqualTo(Categories.Staff);
	}

	[Test]
	public async Task ApprovedIsASystemRoleJustAbovePlayerThatAllowsPicturesAndWikiWriting()
	{
		var approved = BuiltInRoles.All.Single(r => r.Slug == BuiltInRoles.ApprovedSlug);
		await Assert.That(approved.IsSystem).IsTrue();
		await Assert.That(approved.Category).IsEqualTo(Categories.System);
		await Assert.That(approved.Permissions.Keys)
			.IsEquivalentTo([PortalPermission.GamePower("Send_Image"), PortalPermission.WikiCreate, PortalPermission.WikiEdit])
			.Because("an approved character may show pictures in a pose and write the wiki, and nothing else comes with approval");
		await Assert.That(approved.Priority).IsBetween((int)PortalRole.Player + 1, BuiltInRoles.Starters.Min(r => r.Priority) - 1);
		await Assert.That(BuiltInRoles.IsImplicit(approved.Slug)).IsFalse();
	}

	/// <summary>A world seeded before Send_Image existed gains it on its approved role.</summary>
	[Test]
	public async Task AnApprovedRoleSeededWithoutSendImageGainsIt()
	{
		var approved = new SharpRole
		{
			Slug = BuiltInRoles.ApprovedSlug, Name = "Approved", Category = Categories.System, Priority = BuiltInRoles.ApprovedPriority,
			IsSystem = true, CreatedAt = 5
		};
		var changed = BuiltInRoles.SeedChanges([approved], 9).Single(r => r.Slug == BuiltInRoles.ApprovedSlug);
		await Assert.That(changed.Permissions[PortalPermission.GamePower("Send_Image")]).IsEqualTo(PermissionState.Allow);
	}

	[Test]
	public async Task SeedingTwiceChangesNothing()
	{
		var seeded = BuiltInRoles.SeedChanges([], 1);
		await Assert.That(BuiltInRoles.SeedChanges(seeded, 2)).IsEmpty();
	}

	[Test]
	public async Task DefaultsGrantEachHolderWhatItShould()
	{
		var resolver = new PermissionResolver();
		IReadOnlySet<string> Holding(params string[] slugs) => resolver.Resolve(new PermissionContext(
			slugs.Append(BuiltInRoles.EveryoneSlug).Select(slug => BuiltInRoles.All.Single(r => r.Slug == slug)).ToArray(),
			new Dictionary<string, PermissionState>(), false));

		var guest = Holding(BuiltInRoles.GuestSlug);
		var player = Holding(BuiltInRoles.PlayerSlug);
		var approved = Holding(BuiltInRoles.PlayerSlug, BuiltInRoles.ApprovedSlug);
		var moderator = resolver.Resolve(new PermissionContext(
			[.. BuiltInRoles.Starters.Where(r => r.Slug == "moderator"), BuiltInRoles.All.Single(r => r.Slug == BuiltInRoles.EveryoneSlug)],
			new Dictionary<string, PermissionState>(), false));
		var builder = Holding(BuiltInRoles.PlayerSlug, BuiltInRoles.BuilderSlug);
		var royalty = Holding(BuiltInRoles.PlayerSlug, BuiltInRoles.RoyaltySlug);
		var wizard = Holding(BuiltInRoles.PlayerSlug, BuiltInRoles.WizardSlug);

		await Assert.That(guest).IsEquivalentTo([PortalPermission.WikiRead, PortalPermission.GamePower("Guest")]);
		await Assert.That(player).Contains(PortalPermission.WikiRead);
		await Assert.That(player).DoesNotContain(PortalPermission.WikiCreate)
			.Because("a player who is not approved reads the wiki and writes nothing on it");
		await Assert.That(player).DoesNotContain(PortalPermission.WikiEdit);
		await Assert.That(approved).Contains(PortalPermission.WikiCreate);
		await Assert.That(approved).Contains(PortalPermission.WikiEdit);
		await Assert.That(moderator).Contains(PortalPermission.WikiEdit);
		await Assert.That(royalty).Contains(PortalPermission.WikiEdit);
		await Assert.That(wizard).Contains(PortalPermission.WikiEdit);
		await Assert.That(player).Contains(PortalPermission.SnapshotRestore);
		await Assert.That(player).DoesNotContain(PortalPermission.WikiDelete);
		await Assert.That(player.Where(PortalPermission.IsGameScope)).IsEmpty();
		await Assert.That(builder).Contains(PortalPermission.DiagnosticsProfile);
		await Assert.That(builder).Contains(PortalPermission.GamePower("Builder"));
		await Assert.That(royalty).Contains(PortalPermission.WikiDelete);
		await Assert.That(royalty).Contains(PortalPermission.GameRoyalty);
		await Assert.That(royalty).Contains(PortalPermission.ProtectAdmin);
		await Assert.That(royalty).DoesNotContain(PortalPermission.ControlAll);
		await Assert.That(royalty).DoesNotContain(PortalPermission.RolesAdmin);
		await Assert.That(wizard).Contains(PortalPermission.RolesAdmin);
		await Assert.That(wizard).Contains(PortalPermission.GameWizard);
		await Assert.That(wizard).Contains(PortalPermission.ControlAll);
		await Assert.That(wizard).Contains(PortalPermission.ProtectWizard);
		await Assert.That(wizard).DoesNotContain(PortalPermission.ServerAdmin);
		// A wizard's powers come from game.wizard, as PennMUSH's do from the flag; haspower() stays 0.
		await Assert.That(wizard).DoesNotContain(PortalPermission.GamePower("See_All"));
		await Assert.That(wizard).DoesNotContain(PortalPermission.GameRoyalty);
	}

	[Test]
	public async Task SystemRolePrioritiesFollowTheTierOrder()
	{
		var priorities = Enum.GetValues<PortalRole>()
			.Select(tier => BuiltInRoles.All.Single(r => r.Slug == BuiltInRoles.SlugFor(tier)).Priority).ToArray();
		await Assert.That(priorities).IsInOrder();
		await Assert.That(BuiltInRoles.Starters.Single(r => r.Slug == "moderator").Priority)
			.IsBetween((int)PortalRole.Royalty, (int)PortalRole.Wizard);
	}
}
