using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
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

	/// <summary>A role slug no other test uses, so listing by its prefix finds only this test's roles.</summary>
	private static string UniqueSlug() => $"test-{Guid.NewGuid():N}";

	[Test]
	public async Task Roles_UpsertGetListRemove_WithPermissionRoundTrip()
	{
		var prefix = UniqueSlug();
		var alpha = $"{prefix}-alpha";
		var beta = $"{prefix}-beta";
		await Registry.UpsertRoleAsync(Role(alpha, 25, new()
		{
			[PortalPermission.WikiAdmin] = PermissionState.Allow,
			[PortalPermission.ServerAdmin] = PermissionState.Deny
		}));
		await Registry.UpsertRoleAsync(Role(beta, 35));

		var fetched = await Registry.GetRoleAsync(alpha);
		var role = fetched.Expect<SharpRole>();
		await Assert.That(role.Name).IsEqualTo($"Role {alpha}");
		await Assert.That(role.Priority).IsEqualTo(25);
		await Assert.That(role.Permissions[PortalPermission.WikiAdmin]).IsEqualTo(PermissionState.Allow);
		await Assert.That(role.Permissions[PortalPermission.ServerAdmin]).IsEqualTo(PermissionState.Deny);

		await Registry.UpsertRoleAsync(Role(alpha, 99));
		var upgraded = (await Registry.GetRoleAsync(alpha)).Expect<SharpRole>();
		await Assert.That(upgraded.Priority).IsEqualTo(99);
		await Assert.That(upgraded.Permissions.Count).IsEqualTo(0);

		var ours = (await Registry.GetRolesAsync()).Where(r => r.Slug.StartsWith(prefix)).ToList();
		await Assert.That(ours.Count).IsEqualTo(2);
		await Assert.That(ours[0].Slug).IsEqualTo(alpha);

		await Registry.RemoveRoleAsync(alpha);
		await Registry.RemoveRoleAsync(beta);
		await Assert.That((await Registry.GetRoleAsync(alpha)).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task Assignment_RoundTrip()
	{
		var slug = $"{UniqueSlug()}-assign";
		await Registry.UpsertRoleAsync(Role(slug, 20));
		var account = await Db.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("rbac-test-user"), null, "password-hash-123");

		await Registry.AssignRoleToAccountAsync(account.Id!, slug);
		await Registry.AssignRoleToAccountAsync(account.Id!, slug);

		var roles = await Registry.GetRolesForAccountAsync(account.Id!);
		await Assert.That(roles.Count(r => r.Slug == slug)).IsEqualTo(1);

		var accounts = await Registry.GetAccountIdsForRoleAsync(slug);
		await Assert.That(accounts.Count).IsGreaterThanOrEqualTo(1);

		await Registry.RemoveRoleFromAccountAsync(account.Id!, slug);
		var after = await Registry.GetRolesForAccountAsync(account.Id!);
		await Assert.That(after.Any(r => r.Slug == slug)).IsFalse();

		await Registry.RemoveRoleAsync(slug);
	}

	[Test]
	public async Task GetRole_Missing_ReturnsNotFound()
	{
		var missing = await Registry.GetRoleAsync("does-not-exist-role");
		await Assert.That(missing.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task BuiltInRoles_AreSeeded()
	{
		var god = (await Registry.GetRoleAsync("god")).Expect<SharpRole>();
		await Assert.That(god.IsSystem).IsTrue();
		await Assert.That(god.Permissions[PortalPermission.ServerAdmin]).IsEqualTo(PermissionState.Allow);

		var wizard = (await Registry.GetRoleAsync("wizard")).Expect<SharpRole>();
		await Assert.That(wizard.IsSystem).IsTrue();
		await Assert.That(wizard.Permissions[PortalPermission.WikiAdmin]).IsEqualTo(PermissionState.Allow);
		await Assert.That(wizard.Permissions.GetValueOrDefault(PortalPermission.ServerAdmin, PermissionState.Inherit))
			.IsNotEqualTo(PermissionState.Allow);
	}
}
