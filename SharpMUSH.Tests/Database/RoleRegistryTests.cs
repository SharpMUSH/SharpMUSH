using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Integration tests for the portal RBAC role registry (sys_roles + edge_account_has_role)
/// against the active database provider. Verifies role upsert/get/list/remove with three-state
/// permission round-tripping, account↔role assignment, and that the built-in roles were seeded.
/// </summary>
public class RoleRegistryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IRoleRegistryService Registry =>
		(IRoleRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static SharpRole Role(string slug, int priority, Dictionary<string, PermissionState>? perms = null) => new()
	{
		Slug = slug,
		Name = $"Role {slug}",
		Color = "#5aa9ff",
		Priority = priority,
		IsSystem = false,
		Permissions = perms ?? new(),
		CreatedAt = 1_000_000,
		UpdatedAt = 1_000_000
	};

	[Test, NotInParallel]
	public async Task Roles_UpsertGetListRemove_WithPermissionRoundTrip()
	{
		await Registry.UpsertRoleAsync(Role("test-alpha", 25, new()
		{
			[PortalPermission.WikiAdmin] = PermissionState.Allow,
			[PortalPermission.ServerAdmin] = PermissionState.Deny
		}));
		await Registry.UpsertRoleAsync(Role("test-beta", 35));

		var fetched = await Registry.GetRoleAsync("test-alpha");
		await Assert.That(fetched.IsT0).IsTrue();
		var role = fetched.AsT0;
		await Assert.That(role.Name).IsEqualTo("Role test-alpha");
		await Assert.That(role.Priority).IsEqualTo(25);
		await Assert.That(role.Permissions[PortalPermission.WikiAdmin]).IsEqualTo(PermissionState.Allow);
		await Assert.That(role.Permissions[PortalPermission.ServerAdmin]).IsEqualTo(PermissionState.Deny);

		// Upsert replaces in full.
		await Registry.UpsertRoleAsync(Role("test-alpha", 99));
		var upgraded = await Registry.GetRoleAsync("test-alpha");
		await Assert.That(upgraded.AsT0.Priority).IsEqualTo(99);
		await Assert.That(upgraded.AsT0.Permissions.Count).IsEqualTo(0);

		// List is ordered by priority DESC (test-alpha=99 before test-beta=35).
		var ours = (await Registry.GetRolesAsync()).Where(r => r.Slug.StartsWith("test-")).ToList();
		await Assert.That(ours.Count).IsEqualTo(2);
		await Assert.That(ours[0].Slug).IsEqualTo("test-alpha");

		await Registry.RemoveRoleAsync("test-alpha");
		await Registry.RemoveRoleAsync("test-beta");
		await Assert.That((await Registry.GetRoleAsync("test-alpha")).IsT1).IsTrue();
	}

	[Test, NotInParallel]
	public async Task Assignment_RoundTrip()
	{
		await Registry.UpsertRoleAsync(Role("test-assign", 20));
		var account = await Db.CreateAccountAsync("rbac-test-user", null, "password-hash-123");

		await Registry.AssignRoleToAccountAsync(account.Id!, "test-assign");
		// Idempotent — assigning twice doesn't duplicate.
		await Registry.AssignRoleToAccountAsync(account.Id!, "test-assign");

		var roles = await Registry.GetRolesForAccountAsync(account.Id!);
		await Assert.That(roles.Count(r => r.Slug == "test-assign")).IsEqualTo(1);

		var accounts = await Registry.GetAccountIdsForRoleAsync("test-assign");
		await Assert.That(accounts.Count).IsGreaterThanOrEqualTo(1);

		await Registry.RemoveRoleFromAccountAsync(account.Id!, "test-assign");
		var after = await Registry.GetRolesForAccountAsync(account.Id!);
		await Assert.That(after.Any(r => r.Slug == "test-assign")).IsFalse();

		await Registry.RemoveRoleAsync("test-assign");
	}

	[Test, NotInParallel]
	public async Task GetRole_Missing_ReturnsNotFound()
	{
		var missing = await Registry.GetRoleAsync("does-not-exist-role");
		await Assert.That(missing.IsT1).IsTrue();
	}

	[Test, NotInParallel]
	public async Task BuiltInRoles_AreSeeded()
	{
		var god = await Registry.GetRoleAsync("god");
		await Assert.That(god.IsT0).IsTrue();
		await Assert.That(god.AsT0.IsSystem).IsTrue();
		await Assert.That(god.AsT0.Permissions[PortalPermission.ServerAdmin]).IsEqualTo(PermissionState.Allow);

		var wizard = await Registry.GetRoleAsync("wizard");
		await Assert.That(wizard.IsT0).IsTrue();
		await Assert.That(wizard.AsT0.IsSystem).IsTrue();
		await Assert.That(wizard.AsT0.Permissions[PortalPermission.WikiAdmin]).IsEqualTo(PermissionState.Allow);
		// Wizard does NOT get server.admin (God-only).
		await Assert.That(wizard.AsT0.Permissions.GetValueOrDefault(PortalPermission.ServerAdmin, PermissionState.Inherit))
			.IsNotEqualTo(PermissionState.Allow);
	}
}
