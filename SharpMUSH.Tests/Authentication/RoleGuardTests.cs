using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Guarded roles as <see cref="ObjectGrants"/> resolve them: which roles guard their holders
/// (<see cref="PortalPermission.ProtectRole"/>), and who passes a guard (<see cref="PortalPermission.ControlRole"/>), with
/// the built-in roles and RhostMUSH's levels in the world. <c>Controls()</c> itself is <c>RoleGuardControlTests</c>.
/// </summary>
public class RoleGuardTests
{
	private static readonly Dictionary<string, PermissionState> None = new();

	private static readonly SharpRole Overseer = new()
	{
		Slug = "overseer", Name = "Overseer", Priority = 38,
		Permissions = new Dictionary<string, PermissionState> { [PortalPermission.Administrator] = PermissionState.Allow }
	};

	private static ObjectGrants Holding(int number, params string[] roles)
		=> Holding(number, None, roles);

	private static ObjectGrants Holding(int number, IReadOnlyDictionary<string, PermissionState> overrides, params string[] roles)
		=> ObjectGrants.For(number, isPlayer: true, [.. BuiltInRoles.All, .. BuiltInRoles.RhostLevels, Overseer],
			roles, overrides, null, []);

	[Test]
	public async Task TheBuiltInStaffRolesAreGuarded()
	{
		await Assert.That(Holding(1).GuardedRoles).Contains(BuiltInRoles.GodSlug);
		await Assert.That(Holding(42, BuiltInRoles.WizardSlug, BuiltInRoles.RoyaltySlug).GuardedRoles)
			.IsEquivalentTo([BuiltInRoles.WizardSlug, BuiltInRoles.RoyaltySlug]);
		await Assert.That(Holding(42, BuiltInRoles.BuilderSlug, "guildmaster", "architect").GuardedRoles).IsEmpty();
	}

	[Test]
	public async Task AWizardPassesWizardsRoyaltysAndCouncilorsGuardsButNotImmortals()
	{
		var wizard = Holding(42, BuiltInRoles.WizardSlug);

		await Assert.That(wizard.PassesGuardsOf(Holding(43, BuiltInRoles.WizardSlug))).IsTrue();
		await Assert.That(wizard.PassesGuardsOf(Holding(43, BuiltInRoles.RoyaltySlug))).IsTrue();
		await Assert.That(wizard.PassesGuardsOf(Holding(43, BuiltInRoles.CouncilorSlug))).IsTrue();
		await Assert.That(wizard.PassesGuardsOf(Holding(43, BuiltInRoles.ImmortalSlug))).IsFalse();
		await Assert.That(Holding(43, BuiltInRoles.ImmortalSlug).PassesGuardsOf(wizard)).IsTrue();
	}

	[Test]
	public async Task RoyaltyPassesOnlyRoyaltysGuard()
	{
		var royalty = Holding(42, BuiltInRoles.RoyaltySlug);

		await Assert.That(royalty.PassesGuardsOf(Holding(43, BuiltInRoles.RoyaltySlug))).IsTrue();
		await Assert.That(royalty.PassesGuardsOf(Holding(43, BuiltInRoles.CouncilorSlug))).IsFalse();
		await Assert.That(royalty.PassesGuardsOf(Holding(43, BuiltInRoles.WizardSlug))).IsFalse();
		await Assert.That(Holding(42, BuiltInRoles.BuilderSlug).PassesGuardsOf(Holding(43, BuiltInRoles.RoyaltySlug))).IsFalse();
	}

	[Test]
	public async Task AHolderOfSeveralGuardedRolesMustBePassedForEach()
		=> await Assert.That(Holding(42, BuiltInRoles.RoyaltySlug)
			.PassesGuardsOf(Holding(43, BuiltInRoles.RoyaltySlug, BuiltInRoles.WizardSlug))).IsFalse();

	/// <summary>God's guard is #1's alone: an administrator role, which grants every other permission, does not pass it.</summary>
	[Test]
	public async Task NothingButTheOwnerPassesGodsGuard()
	{
		var god = Holding(1);
		var overrides = new Dictionary<string, PermissionState>
		{
			[PortalPermission.ControlRole(BuiltInRoles.GodSlug)] = PermissionState.Allow
		};

		await Assert.That(god.PassesGuardsOf(Holding(42, BuiltInRoles.ImmortalSlug, BuiltInRoles.WizardSlug))).IsTrue();
		await Assert.That(Holding(42, Overseer.Slug).PassesGuardsOf(god)).IsFalse();
		await Assert.That(Holding(42, Overseer.Slug).PassesGuardsOf(Holding(43, BuiltInRoles.ImmortalSlug))).IsTrue();
		await Assert.That(Holding(42, overrides, BuiltInRoles.WizardSlug).PassesGuardsOf(god)).IsFalse();
	}

	/// <summary>A guard is read on the role: an override on the object neither guards it nor lifts its role's guard.</summary>
	[Test]
	public async Task AnOverrideNeitherAddsAGuardNorLiftsOne()
	{
		var guards = new Dictionary<string, PermissionState> { [PortalPermission.ProtectRole] = PermissionState.Allow };
		var unguards = new Dictionary<string, PermissionState> { [PortalPermission.ProtectRole] = PermissionState.Deny };

		await Assert.That(Holding(43, guards, BuiltInRoles.BuilderSlug).GuardedRoles).IsEmpty();
		await Assert.That(Holding(43, unguards, BuiltInRoles.RoyaltySlug).GuardedRoles).IsEquivalentTo([BuiltInRoles.RoyaltySlug]);
	}

	/// <summary>A privilege ceiling (<c>ueval()</c>) keeps only the passes of the roles at or below it, the owner's included.</summary>
	[Test]
	public async Task ACeilingTakesAwayThePassesOfTheRolesAboveIt()
	{
		var royalty = Holding(43, BuiltInRoles.RoyaltySlug);

		await Assert.That(Holding(42, BuiltInRoles.WizardSlug).AtMost(BuiltInRoles.All.Single(r => r.Slug == BuiltInRoles.BuilderSlug).Priority)
			.PassesGuardsOf(royalty)).IsFalse();
		await Assert.That(Holding(1).AtMost(BuiltInRoles.All.Single(r => r.Slug == BuiltInRoles.WizardSlug).Priority)
			.PassesGuardsOf(Holding(43, BuiltInRoles.ImmortalSlug))).IsFalse();
	}

	/// <summary>An app acting as a wizard passes guards only when its scopes cover the Game group.</summary>
	[Test]
	public async Task AnAppWithoutTheGameScopePassesNoGuard()
	{
		var wizard = Holding(42, BuiltInRoles.WizardSlug);
		var royalty = Holding(43, BuiltInRoles.RoyaltySlug);

		await Assert.That(wizard.Within([SharpMUSH.Library.OAuth.DelegatedScopes.Content]).PassesGuardsOf(royalty)).IsFalse();
		await Assert.That(wizard.Within([SharpMUSH.Library.OAuth.DelegatedScopes.Game]).PassesGuardsOf(royalty)).IsTrue();
	}
}
