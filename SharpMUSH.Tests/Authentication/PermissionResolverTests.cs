using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="PermissionResolver"/> — the Discord-style priority/three-state
/// permission resolution (highest-priority explicit Allow/Deny wins, Deny wins same-priority
/// ties, default deny).
/// </summary>
public class PermissionResolverTests
{
	private static readonly IPermissionResolver Resolver = new PermissionResolver();

	private static SharpRole Role(string slug, int priority, params (string Scope, PermissionState State)[] perms)
		=> new()
		{
			Slug = slug,
			Name = slug,
			Priority = priority,
			Permissions = perms.ToDictionary(p => p.Scope, p => p.State)
		};

	[Test]
	public async ValueTask NoRoles_GrantsNothing()
	{
		var granted = Resolver.Resolve([]);
		await Assert.That(granted.Count).IsEqualTo(0);
	}

	[Test]
	public async ValueTask SingleAllow_IsGranted()
	{
		var granted = Resolver.Resolve([Role("editor", 50, (PortalPermission.WikiAdmin, PermissionState.Allow))]);
		await Assert.That(granted.Contains(PortalPermission.WikiAdmin)).IsTrue();
		await Assert.That(granted.Contains(PortalPermission.ServerAdmin)).IsFalse();
	}

	[Test]
	public async ValueTask InheritOnly_DefaultsToDeny()
	{
		var granted = Resolver.Resolve([Role("r", 50, (PortalPermission.WikiAdmin, PermissionState.Inherit))]);
		await Assert.That(granted.Contains(PortalPermission.WikiAdmin)).IsFalse();
	}

	[Test]
	public async ValueTask HigherPriorityDeny_BeatsLowerPriorityAllow()
	{
		var allow = Role("members", 10, (PortalPermission.WikiAdmin, PermissionState.Allow));
		var deny = Role("muted", 100, (PortalPermission.WikiAdmin, PermissionState.Deny));
		var granted = Resolver.Resolve([allow, deny]);
		await Assert.That(granted.Contains(PortalPermission.WikiAdmin)).IsFalse();
	}

	[Test]
	public async ValueTask HigherPriorityAllow_BeatsLowerPriorityDeny()
	{
		var deny = Role("base", 10, (PortalPermission.WikiAdmin, PermissionState.Deny));
		var allow = Role("admins", 100, (PortalPermission.WikiAdmin, PermissionState.Allow));
		var granted = Resolver.Resolve([deny, allow]);
		await Assert.That(granted.Contains(PortalPermission.WikiAdmin)).IsTrue();
	}

	[Test]
	public async ValueTask SamePriorityTie_DenyWins()
	{
		var allow = Role("a", 50, (PortalPermission.WikiAdmin, PermissionState.Allow));
		var deny = Role("b", 50, (PortalPermission.WikiAdmin, PermissionState.Deny));
		var granted = Resolver.Resolve([allow, deny]);
		await Assert.That(granted.Contains(PortalPermission.WikiAdmin)).IsFalse();
	}

	[Test]
	public async ValueTask LowerPriorityOpinion_AppliesWhenHigherIsInherit()
	{
		// The high-priority role is Inherit on this scope (no opinion), so the lower role decides.
		var high = Role("high", 100, (PortalPermission.WikiAdmin, PermissionState.Inherit));
		var low = Role("low", 10, (PortalPermission.WikiAdmin, PermissionState.Allow));
		var granted = Resolver.Resolve([high, low]);
		await Assert.That(granted.Contains(PortalPermission.WikiAdmin)).IsTrue();
	}

	[Test]
	public async ValueTask BuiltInGod_GrantsEveryScope()
	{
		var god = BuiltInRoles.All.Single(r => r.Slug == "god");
		var granted = Resolver.Resolve([god]);
		foreach (var scope in PortalPermission.AllScopes)
			await Assert.That(granted.Contains(scope)).IsTrue();
	}

	[Test]
	public async ValueTask BuiltInWizard_GrantsAllExceptServerAdmin()
	{
		var wizard = BuiltInRoles.All.Single(r => r.Slug == "wizard");
		var granted = Resolver.Resolve([wizard]);
		await Assert.That(granted.Contains(PortalPermission.WikiAdmin)).IsTrue();
		await Assert.That(granted.Contains(PortalPermission.ServerAdmin)).IsFalse();
	}
}
