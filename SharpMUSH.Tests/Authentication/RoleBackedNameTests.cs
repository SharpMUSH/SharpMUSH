using SharpMUSH.Database.Seed;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// <see cref="GamePowers"/> and <see cref="RoleFlags"/> name exactly what the seeds define, so a power or
/// flag cannot be seeded under one spelling and read for privilege under another.
/// </summary>
public class RoleBackedNameTests
{
	[Test]
	public async Task EverySeededPowerIsAGamePower()
	{
		await Assert.That(GamePowers.All.Select(p => p.Name)).IsEquivalentTo(PowerSeed.Powers.Select(p => p.Name));
		foreach (var (name, aliases, _, _) in PowerSeed.Powers)
			await Assert.That(GamePowers.Find(name)!.Aliases).IsEquivalentTo(aliases).Because(name);
	}

	[Test]
	public async Task EveryGamePowerHasAScopeInTheCatalog()
	{
		foreach (var power in GamePowers.All)
		{
			await Assert.That(PortalPermission.IsKnown(power.Scope)).IsTrue().Because(power.Name);
			await Assert.That(GamePowers.ForScope(power.Scope)).IsEqualTo(power);
		}
	}

	[Test]
	public async Task PowerRolesAreSystemRolesThatAllowThePower()
	{
		foreach (var power in GamePowers.All.Where(p => p.Role is not null))
		{
			var role = BuiltInRoles.All.Single(r => r.Slug == power.Role);
			await Assert.That(role.Permissions[power.Scope]).IsEqualTo(PermissionState.Allow).Because(power.Name);
		}
	}

	[Test]
	public async Task RoleFlagsAreSeededFlagsWithTheirLetters()
	{
		foreach (var flag in RoleFlags.All)
		{
			var seeded = FlagSeed.Flags.Single(f => f.Name == flag.Name);
			await Assert.That(seeded.Symbol).IsEqualTo(flag.Symbol);
			var role = BuiltInRoles.All.Single(r => r.Slug == flag.Role);
			await Assert.That(role.Permissions[flag.Scope]).IsEqualTo(PermissionState.Allow).Because(flag.Name);
		}
	}

	[Test]
	[Arguments("see_all", "See_All")]
	[Arguments("TEL_ANYWHERE", "Tport_Anywhere")]
	[Arguments("@wall", "Announce")]
	[Arguments("Pueblo_Send", "Send_OOB")]
	public async Task PowersAnswerToNameAndAlias(string asked, string power)
		=> await Assert.That(GamePowers.Find(asked)?.Name).IsEqualTo(power);
}
