using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// <see cref="PermissionResolver"/> against Discord's permission computation: owner, administrator,
/// per-account override, pooled role allows over pooled role denies, then <c>everyone</c>.
/// </summary>
public class PermissionResolverTests
{
	private static readonly IPermissionResolver Resolver = new PermissionResolver();

	private static readonly Dictionary<string, PermissionState> NoOverrides = new();

	private static SharpRole Role(string slug, int priority, params (string Scope, PermissionState State)[] perms) => new()
	{
		Slug = slug,
		Name = slug,
		Priority = priority,
		Permissions = perms.ToDictionary(p => p.Scope, p => p.State)
	};

	private static SharpRole Everyone(params (string Scope, PermissionState State)[] perms)
		=> Role(BuiltInRoles.EveryoneSlug, 0, perms);

	private static PermissionContext Context(params SharpRole[] roles) => new(roles, NoOverrides, false);

	private static PermissionContext Context(Dictionary<string, PermissionState> overrides, params SharpRole[] roles)
		=> new(roles, overrides, false);

	private static (string, PermissionState) Allow(string scope) => (scope, PermissionState.Allow);
	private static (string, PermissionState) Deny(string scope) => (scope, PermissionState.Deny);

	[Test]
	public async ValueTask NothingHeld_GrantsNothing()
	{
		await Assert.That(Resolver.Resolve(PermissionContext.None)).IsEmpty();
	}

	[Test]
	public async ValueTask Owner_HoldsEveryScope_EvenWithDenies()
	{
		var context = new PermissionContext([Role("r", 5, Deny(PortalPermission.RolesAdmin))],
			new Dictionary<string, PermissionState> { [PortalPermission.RolesAdmin] = PermissionState.Deny }, IsOwner: true);
		await Assert.That(Resolver.Resolve(context).Count).IsEqualTo(PortalPermission.AllScopes.Count);
		await Assert.That(Resolver.Explain(context, PortalPermission.RolesAdmin).Reason).IsEqualTo("owner");
	}

	[Test]
	public async ValueTask Administrator_GrantsEverything_AndIgnoresAccountDenies()
	{
		var context = Context(new Dictionary<string, PermissionState> { [PortalPermission.ServerAdmin] = PermissionState.Deny },
			Role("admin", 1, Allow(PortalPermission.Administrator)), Role("limits", 50, Deny(PortalPermission.ServerAdmin)));
		var explanation = Resolver.Explain(context, PortalPermission.ServerAdmin);
		await Assert.That(explanation.Allowed).IsTrue();
		await Assert.That(explanation.Reason).IsEqualTo("administrator");
		await Assert.That(explanation.Roles).IsEquivalentTo(["admin"]);
	}

	/// <summary>A guard pass for a role the context does not name is held by no one, so Explain must not say the owner holds it.</summary>
	[Test]
	public async ValueTask Explain_AgreesWithResolve_OnAGuardPassOutsideTheContext()
	{
		var pass = PortalPermission.ControlRole("curator");
		var owner = new PermissionContext([], new Dictionary<string, PermissionState>(), IsOwner: true);
		var admin = Context(Role("admin", 1, Allow(PortalPermission.Administrator)));
		await Assert.That(Resolver.Resolve(owner).Contains(pass)).IsFalse();
		await Assert.That(Resolver.Explain(owner, pass).Allowed).IsFalse();
		await Assert.That(Resolver.Explain(admin, pass).Allowed).IsFalse();
		var named = owner with { ControlScopes = new HashSet<string>([pass]) };
		await Assert.That(Resolver.Resolve(named).Contains(pass)).IsTrue();
		await Assert.That(Resolver.Explain(named, pass).Reason).IsEqualTo("owner");
	}

	[Test]
	[Arguments(1, 50)]
	[Arguments(50, 1)]
	[Arguments(10, 10)]
	public async ValueTask AnyRoleAllow_BeatsAnyRoleDeny_WhateverThePriorities(int allowPriority, int denyPriority)
	{
		var context = Context(Role("allows", allowPriority, Allow(PortalPermission.WikiDelete)),
			Role("denies", denyPriority, Deny(PortalPermission.WikiDelete)));
		var explanation = Resolver.Explain(context, PortalPermission.WikiDelete);
		await Assert.That(explanation.Allowed).IsTrue();
		await Assert.That(explanation.Reason).IsEqualTo("role-allow");
		await Assert.That(explanation.Priority).IsEqualTo(allowPriority);
	}

	[Test]
	public async ValueTask RoleDeny_RemovesWhatEveryoneAllows()
	{
		var context = Context(Everyone(Allow(PortalPermission.WikiRead)), Role("suspended", 1, Deny(PortalPermission.WikiRead)));
		var explanation = Resolver.Explain(context, PortalPermission.WikiRead);
		await Assert.That(explanation.Allowed).IsFalse();
		await Assert.That(explanation.Reason).IsEqualTo("role-deny");
	}

	[Test]
	public async ValueTask Everyone_DecidesWhenNoOtherRoleHasAnOpinion()
	{
		var context = Context(Everyone(Allow(PortalPermission.WikiRead)), Role("player", 10));
		await Assert.That(Resolver.Explain(context, PortalPermission.WikiRead).Reason).IsEqualTo("everyone-allow");
		await Assert.That(Resolver.Explain(context, PortalPermission.WikiEdit).Reason).IsEqualTo("default-deny");
	}

	[Test]
	public async ValueTask EveryoneDeny_DoesNotCancelARoleAllow()
	{
		var context = Context(Everyone(Deny(PortalPermission.WikiEdit)), Role("player", 10, Allow(PortalPermission.WikiEdit)));
		await Assert.That(Resolver.Explain(context, PortalPermission.WikiEdit).Allowed).IsTrue();
	}

	[Test]
	[Arguments(PermissionState.Deny, false, "account-deny")]
	[Arguments(PermissionState.Allow, true, "account-allow")]
	[Arguments(PermissionState.Inherit, true, "role-allow")]
	public async ValueTask AccountOverride_BeatsEveryRole(PermissionState state, bool allowed, string reason)
	{
		var context = Context(new Dictionary<string, PermissionState> { [PortalPermission.WikiEdit] = state },
			Role("player", 10, Allow(PortalPermission.WikiEdit)), Role("wizard", 30, Allow(PortalPermission.WikiEdit)));
		var explanation = Resolver.Explain(context, PortalPermission.WikiEdit);
		await Assert.That(explanation.Allowed).IsEqualTo(allowed);
		await Assert.That(explanation.Reason).IsEqualTo(reason);
	}

	[Test]
	public async ValueTask AccountAllow_GrantsWithoutAnyRole()
	{
		var context = Context(new Dictionary<string, PermissionState> { [PortalPermission.WikiDelete] = PermissionState.Allow });
		await Assert.That(Resolver.Resolve(context)).IsEquivalentTo([PortalPermission.WikiDelete]);
	}

	[Test]
	public async ValueTask Umbrella_CoversChildrenLeftOnInherit()
	{
		var granted = Resolver.Resolve(Context(Role("wiki", 5, Allow(PortalPermission.WikiAdmin))));
		await Assert.That(granted).Contains(PortalPermission.WikiRead);
		await Assert.That(granted).Contains(PortalPermission.WikiCreate);
		await Assert.That(granted).Contains(PortalPermission.WikiEdit);
		await Assert.That(granted).Contains(PortalPermission.WikiDelete);
		await Assert.That(granted).DoesNotContain(PortalPermission.MediaUpload);
	}

	[Test]
	public async ValueTask ExplicitChild_BeatsItsUmbrella_WithinTheSameRole()
	{
		var context = Context(Role("wiki", 5, Allow(PortalPermission.WikiAdmin), Deny(PortalPermission.WikiDelete)));
		await Assert.That(Resolver.Explain(context, PortalPermission.WikiDelete).Allowed).IsFalse();
		await Assert.That(Resolver.Explain(context, PortalPermission.WikiEdit).Allowed).IsTrue();
	}

	[Test]
	public async ValueTask ChildDenyOnOneRole_LosesToUmbrellaAllowOnAnother()
	{
		var context = Context(Role("restricted", 50, Deny(PortalPermission.JobsManageOwn)),
			Role("operator", 1, Allow(PortalPermission.JobsManage)));
		await Assert.That(Resolver.Explain(context, PortalPermission.JobsManageOwn).Allowed).IsTrue();
	}

	[Test]
	public async ValueTask AccountDenyOnUmbrella_CoversItsChildren()
	{
		var context = Context(new Dictionary<string, PermissionState> { [PortalPermission.WikiAdmin] = PermissionState.Deny },
			Role("player", 10, Allow(PortalPermission.WikiEdit)));
		await Assert.That(Resolver.Explain(context, PortalPermission.WikiEdit).Reason).IsEqualTo("account-deny");
	}

	[Test]
	public async ValueTask UnknownScope_IsRefused()
	{
		var context = new PermissionContext([], NoOverrides, IsOwner: true);
		await Assert.That(Resolver.Explain(context, "no.such.scope").Reason).IsEqualTo("unknown-scope");
	}

	[Test]
	public async ValueTask StateOf_IsCaseInsensitive()
	{
		var permissions = new Dictionary<string, PermissionState> { ["WIKI.EDIT"] = PermissionState.Allow };
		await Assert.That(PermissionResolver.StateOf(permissions, PortalPermission.WikiEdit)).IsEqualTo(PermissionState.Allow);
	}

	[Test]
	public async ValueTask CustomPermission_ResolvesOnlyWhileDefined()
	{
		var helper = Role("helper", 12, Allow("scene.close"));
		var defined = Context(helper) with { CustomScopes = new HashSet<string>(["scene.close"]) };
		await Assert.That(Resolver.Resolve(defined)).Contains("scene.close");
		await Assert.That(Resolver.Explain(defined, "scene.close").Reason).IsEqualTo("role-allow");

		// A role still naming a permission the game no longer defines grants nothing.
		await Assert.That(Resolver.Resolve(Context(helper))).DoesNotContain("scene.close");
		await Assert.That(Resolver.Explain(Context(helper), "scene.close").Reason).IsEqualTo("unknown-scope");
	}

	[Test]
	public async ValueTask CustomPermission_IsHeldByTheOwnerOnceDefined()
	{
		var owner = new PermissionContext([], NoOverrides, true) { CustomScopes = new HashSet<string>(["scene.close"]) };
		await Assert.That(Resolver.Resolve(owner)).Contains("scene.close");
	}

	/// <summary>Resolve decides each scope without building an explanation; it must reach Explain's verdict on every one.</summary>
	[Test]
	public async ValueTask Resolve_AgreesWithExplain_OnEveryScope()
	{
		var customScopes = new HashSet<string>(["scene.close", "scene.open"], StringComparer.OrdinalIgnoreCase);
		var objectOverrides = new Dictionary<string, PermissionState>
		{
			[PortalPermission.WikiEdit] = PermissionState.Deny, ["GAME.SEE_ALL"] = PermissionState.Allow, ["scene.open"] = PermissionState.Deny
		};
		var accountOverrides = new Dictionary<string, PermissionState>
		{
			[PortalPermission.MediaAdmin] = PermissionState.Allow, [PortalPermission.WikiRead] = PermissionState.Deny
		};
		SharpRole[] roles =
		[
			Everyone(Allow(PortalPermission.WikiRead), Allow("scene.close"), Allow(PortalPermission.JobsManageOwn)),
			Role("staff", 10, Allow("WIKI.ADMIN"), Deny(PortalPermission.WikiDelete), Allow(PortalPermission.GameWizard)),
			Role("muted", 5, Deny(PortalPermission.JobsManage), Deny("scene.close"), Allow(PortalPermission.QueueInspect)),
			Role("players", 1, Deny(PortalPermission.PlayersModerate), Allow(PortalPermission.PlayersView))
		];
		var contexts = new List<PermissionContext>();
		for (var mask = 0; mask < 1 << roles.Length; mask++)
		{
			var held = roles.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
			contexts.Add(new PermissionContext(held, NoOverrides, false) { CustomScopes = customScopes });
			contexts.Add(new PermissionContext(held, accountOverrides, false) { ObjectOverrides = objectOverrides, CustomScopes = customScopes });
		}

		contexts.Add(new PermissionContext([Role("admin", 1, Allow(PortalPermission.Administrator)), roles[2]], accountOverrides, false) { CustomScopes = customScopes });
		contexts.Add(new PermissionContext([roles[2]], accountOverrides, true) { CustomScopes = customScopes });

		foreach (var context in contexts)
		{
			var expected = PortalPermission.AllScopes.Concat(context.CustomScopes)
				.Where(scope => Resolver.Explain(context, scope).Allowed).ToHashSet(StringComparer.Ordinal);
			await Assert.That(Resolver.Resolve(context).Order(StringComparer.Ordinal)).IsEquivalentTo(expected.Order(StringComparer.Ordinal));
		}
	}

	[Test]
	public async ValueTask CustomPermission_WizardMayGrantIt()
	{
		await Assert.That(RoleHierarchy.CanGrant(new HashSet<string>([PortalPermission.GameWizard]), "scene.close")).IsTrue();
		await Assert.That(RoleHierarchy.CanGrant(new HashSet<string>([PortalPermission.RolesAdmin]), "scene.close")).IsFalse();
		await Assert.That(RoleHierarchy.CanGrant(new HashSet<string>(["scene.close"]), "scene.close")).IsTrue();
	}

	[Test]
	[Arguments("scene.close", true)]
	[Arguments("jobs.approve_all", true)]
	[Arguments("a.b.c", true)]
	[Arguments("scene", false)]
	[Arguments("Scene.Close", false)]
	[Arguments("scene..close", false)]
	[Arguments("1scene.close", false)]
	[Arguments("wiki.read", false)]
	[Arguments("game.see_all", false)]
	[Arguments("game.newthing", false)]
	[Arguments("control.extra", false)]
	[Arguments("protect.extra", false)]
	public async ValueTask CustomPermission_Names(string scope, bool valid)
	{
		await Assert.That(CustomPermissions.IsValidName(scope)).IsEqualTo(valid);
	}
}
