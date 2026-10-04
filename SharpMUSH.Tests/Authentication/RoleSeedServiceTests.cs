using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Authentication;

public class RoleSeedServiceTests
{
	[Test]
	public async Task NewWorldGetsSystemAndStarterRoles()
	{
		var registry = new InMemoryRoleRegistry();
		await new RoleSeedService(registry, NullLogger<RoleSeedService>.Instance).StartAsync(default);
		var slugs = (await registry.GetRolesAsync()).Select(r => r.Slug).ToArray();
		await Assert.That(slugs).IsEquivalentTo(BuiltInRoles.All.Concat(BuiltInRoles.Starters).Select(r => r.Slug));
		await Assert.That((await registry.GetRolesAsync()).Single(r => r.Slug == "moderator").IsSystem).IsFalse();
	}

	[Test]
	public async Task ExistingWorldKeepsEditsAndDeletedStarters()
	{
		var registry = new InMemoryRoleRegistry();
		registry.Add(new SharpRole
		{
			Slug = "player", Name = "Citizen", Priority = 10, IsSystem = true,
			Permissions = new() { [PortalPermission.WikiEdit] = PermissionState.Deny }
		});
		await new RoleSeedService(registry, NullLogger<RoleSeedService>.Instance).StartAsync(default);
		var roles = await registry.GetRolesAsync();
		var player = roles.Single(r => r.Slug == "player");
		await Assert.That(player.Name).IsEqualTo("Citizen");
		await Assert.That(player.Permissions[PortalPermission.WikiEdit]).IsEqualTo(PermissionState.Deny);
		await Assert.That(roles.Select(r => r.Slug)).Contains(BuiltInRoles.EveryoneSlug);
		await Assert.That(roles.Select(r => r.Slug)).DoesNotContain("moderator");
	}

	[Test]
	public async Task DefaultsGrantEachTierWhatItShould()
	{
		var resolver = new PermissionResolver();
		var everyone = BuiltInRoles.All.Single(BuiltInRoles.IsEveryone);
		IReadOnlySet<string> Tier(PortalRole tier) => resolver.Resolve(new PermissionContext(
			BuiltInRoles.TierSlugs(tier).Select(slug => BuiltInRoles.All.Single(r => r.Slug == slug)).Append(everyone).ToArray(),
			new Dictionary<string, PermissionState>(), tier == PortalRole.God));

		await Assert.That(Tier(PortalRole.Guest)).IsEquivalentTo([PortalPermission.WikiRead]);
		await Assert.That(Tier(PortalRole.Player)).Contains(PortalPermission.WikiEdit);
		await Assert.That(Tier(PortalRole.Player)).Contains(PortalPermission.SnapshotRestore);
		await Assert.That(Tier(PortalRole.Player)).DoesNotContain(PortalPermission.WikiDelete);
		await Assert.That(Tier(PortalRole.Builder)).Contains(PortalPermission.DiagnosticsProfile);
		await Assert.That(Tier(PortalRole.Royalty)).Contains(PortalPermission.WikiDelete);
		await Assert.That(Tier(PortalRole.Royalty)).Contains(PortalPermission.SoftcodeUse);
		await Assert.That(Tier(PortalRole.Royalty)).DoesNotContain(PortalPermission.RolesAdmin);
		await Assert.That(Tier(PortalRole.Wizard)).Contains(PortalPermission.RolesAdmin);
		await Assert.That(Tier(PortalRole.Wizard)).DoesNotContain(PortalPermission.ServerAdmin);
		await Assert.That(Tier(PortalRole.God).Count).IsEqualTo(PortalPermission.AllScopes.Count);
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
