using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// An override's reach (<see cref="OverrideReach"/>): an Allow applies to targets ranked at or below its role, a Deny
/// to targets ranked at or above it, and outside its reach the layers below decide.
/// </summary>
public class OverrideReachTests
{
	private static readonly IPermissionResolver Resolver = new PermissionResolver();

	private static readonly string Scope = PortalPermission.GamePower("Tport_Anything");

	private static SharpRole Role(string slug, int priority, params (string Scope, PermissionState State)[] perms) => new()
	{
		Slug = slug,
		Name = slug,
		Priority = priority,
		Permissions = perms.ToDictionary(p => p.Scope, p => p.State)
	};

	private static readonly SharpRole Everyone = Role(BuiltInRoles.EveryoneSlug, 0);
	private static readonly SharpRole Player = Role(BuiltInRoles.PlayerSlug, 10);
	private static readonly SharpRole Councilor = Role(BuiltInRoles.CouncilorSlug, 22);
	private static readonly SharpRole Wizard = Role(BuiltInRoles.WizardSlug, 30, (Scope, PermissionState.Allow));
	private static readonly SharpRole[] World = [Everyone, Player, Councilor, Wizard];

	private static PermissionContext Holding(PermissionState state, string? reach, params SharpRole[] roles)
		=> new(roles.Prepend(Everyone).ToArray(), new Dictionary<string, PermissionState>(), false)
		{
			ObjectOverrides = new Dictionary<string, PermissionState> { [Scope] = state },
			ObjectReaches = reach is null ? OverrideReach.None : OverrideReach.Of(new Dictionary<string, string> { [Scope] = reach }, World)
		};

	[Test]
	public async ValueTask Allow_ReachesTargetsAtOrBelowItsRole()
	{
		var context = Holding(PermissionState.Allow, BuiltInRoles.CouncilorSlug, Player);
		await Assert.That(Resolver.Allows(context, Scope, Player.Priority)).IsTrue();
		await Assert.That(Resolver.Allows(context, Scope, Councilor.Priority)).IsTrue();
		await Assert.That(Resolver.Allows(context, Scope, Wizard.Priority)).IsFalse();
	}

	[Test]
	public async ValueTask Deny_ReachesTargetsAtOrAboveItsRole()
	{
		var context = Holding(PermissionState.Deny, BuiltInRoles.CouncilorSlug, Player, Wizard);
		await Assert.That(Resolver.Allows(context, Scope, Player.Priority)).IsTrue();
		await Assert.That(Resolver.Allows(context, Scope, Councilor.Priority)).IsFalse();
		await Assert.That(Resolver.Allows(context, Scope, Wizard.Priority)).IsFalse();
		await Assert.That(Resolver.Explain(context, Scope, Player.Priority).Reason).IsEqualTo("role-allow");
		var refused = Resolver.Explain(context, Scope, Wizard.Priority);
		await Assert.That(refused.Reason).IsEqualTo("object-deny");
		await Assert.That(refused.Reach).IsEqualTo(BuiltInRoles.CouncilorSlug);
	}

	[Test]
	public async ValueTask OutsideItsReach_TheAccountOverrideDecides()
	{
		var context = Holding(PermissionState.Allow, BuiltInRoles.PlayerSlug, Player) with
		{
			Overrides = new Dictionary<string, PermissionState> { [Scope] = PermissionState.Deny }
		};
		await Assert.That(Resolver.Allows(context, Scope, Player.Priority)).IsTrue();
		await Assert.That(Resolver.Explain(context, Scope, Councilor.Priority).Reason).IsEqualTo("account-deny");
	}

	[Test]
	public async ValueTask NoTarget_RanksAsAPlayer()
	{
		var allow = Holding(PermissionState.Allow, BuiltInRoles.CouncilorSlug, Player);
		await Assert.That(Resolver.Resolve(allow)).Contains(Scope);

		var deny = Holding(PermissionState.Deny, BuiltInRoles.CouncilorSlug, Player, Wizard);
		await Assert.That(Resolver.Resolve(deny)).Contains(Scope);

		var denyPlayers = Holding(PermissionState.Deny, BuiltInRoles.PlayerSlug, Player, Wizard);
		await Assert.That(Resolver.Resolve(denyPlayers)).DoesNotContain(Scope);
	}

	[Test]
	public async ValueTask ReachNamingNoRole_AllowsNothingAndDeniesEverything()
	{
		var allow = Holding(PermissionState.Allow, "nosuchrole", Player);
		await Assert.That(Resolver.Allows(allow, Scope, (int)PortalRole.Guest)).IsFalse();

		var deny = Holding(PermissionState.Deny, "nosuchrole", Player, Wizard);
		await Assert.That(Resolver.Allows(deny, Scope, (int)PortalRole.Guest)).IsFalse();
	}

	[Test]
	public async ValueTask UmbrellaOverride_CarriesItsReachToTheScopesItCovers()
	{
		var context = new PermissionContext([Everyone, Player], new Dictionary<string, PermissionState>(), false)
		{
			ObjectOverrides = new Dictionary<string, PermissionState> { [PortalPermission.WikiAdmin] = PermissionState.Allow },
			ObjectReaches = OverrideReach.Of(new Dictionary<string, string> { [PortalPermission.WikiAdmin] = BuiltInRoles.PlayerSlug }, World)
		};
		await Assert.That(Resolver.Allows(context, PortalPermission.WikiEdit, Player.Priority)).IsTrue();
		await Assert.That(Resolver.Allows(context, PortalPermission.WikiEdit, Wizard.Priority)).IsFalse();
		await Assert.That(Resolver.Explain(context, PortalPermission.WikiEdit, Player.Priority).Reach).IsEqualTo(BuiltInRoles.PlayerSlug);
	}

	[Test]
	public async ValueTask Owner_IsNotNarrowedByAReach()
	{
		var context = Holding(PermissionState.Deny, BuiltInRoles.PlayerSlug) with { IsOwner = true };
		await Assert.That(Resolver.Allows(context, Scope, Wizard.Priority)).IsTrue();
	}

	[Test]
	public async ValueTask ObjectGrants_ReadReachesAndRankTargets()
	{
		var grants = ObjectGrants.For(7, true, World, [], new Dictionary<string, PermissionState> { [Scope] = PermissionState.Allow },
			null, [], new Dictionary<string, string> { [Scope] = BuiltInRoles.CouncilorSlug });
		var player = ObjectGrants.For(8, true, World, [], new Dictionary<string, PermissionState>(), null, []);
		var wizard = ObjectGrants.For(9, true, World, [BuiltInRoles.WizardSlug], new Dictionary<string, PermissionState>(), null, []);

		await Assert.That(player.Rank).IsEqualTo(Player.Priority);
		await Assert.That(wizard.Rank).IsEqualTo(Wizard.Priority);
		await Assert.That(grants.Has(Scope)).IsTrue();
		await Assert.That(grants.Has(Scope, player)).IsTrue();
		await Assert.That(grants.Has(Scope, wizard)).IsFalse();
		await Assert.That(grants.Shows(Scope, player)).IsTrue();
		await Assert.That(grants.Shows(Scope, wizard)).IsFalse();
	}

	[Test]
	public async ValueTask ObjectGrants_IgnoreAReachLeftWithoutItsOverride()
	{
		var grants = ObjectGrants.For(7, true, World, [], new Dictionary<string, PermissionState>(),
			null, [], new Dictionary<string, string> { [Scope] = BuiltInRoles.CouncilorSlug });
		await Assert.That(grants.Context.HasReaches).IsFalse();
	}

	[Test]
	public async ValueTask ObjectGrants_UnderACeiling_KeepTheDenyReach()
	{
		var grants = ObjectGrants.For(7, true, World, [BuiltInRoles.WizardSlug],
			new Dictionary<string, PermissionState> { [Scope] = PermissionState.Deny }, null, [],
			new Dictionary<string, string> { [Scope] = BuiltInRoles.CouncilorSlug });
		var capped = grants.AtMost(Wizard.Priority);
		await Assert.That(capped.Has(Scope, Player.Priority)).IsTrue();
		await Assert.That(capped.Has(Scope, Wizard.Priority)).IsFalse();
	}

	[Test]
	public async ValueTask ObjectGrants_WithinScopes_KeepTheAllowReach()
	{
		var guest = Role(BuiltInRoles.GuestSlug, 0);
		var grants = ObjectGrants.For(7, true, [.. World, guest], [], new Dictionary<string, PermissionState> { [Scope] = PermissionState.Allow },
			null, [], new Dictionary<string, string> { [Scope] = BuiltInRoles.GuestSlug });
		var game = grants.Within([SharpMUSH.Library.OAuth.DelegatedScopes.Game]);
		var content = grants.Within([SharpMUSH.Library.OAuth.DelegatedScopes.Content]);
		await Assert.That(grants.Has(Scope)).IsFalse();
		await Assert.That(game.Has(Scope, guest.Priority)).IsTrue();
		await Assert.That(game.Has(Scope, Player.Priority)).IsFalse();
		await Assert.That(content.Has(Scope, guest.Priority)).IsFalse();
	}
}
