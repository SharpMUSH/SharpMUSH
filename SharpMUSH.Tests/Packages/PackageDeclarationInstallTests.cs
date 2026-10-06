using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>A package's roles, permissions and categories, installed, upgraded and uninstalled against the real world.</summary>
public class PackageDeclarationInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private IRoleRegistryService Roles => Services.GetRequiredService<IRoleRegistryService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Services.GetRequiredService<ISharpDatabase>();

	/// <summary>Names unique to one test, since every test shares the world.</summary>
	private sealed record Names(string Package, string Category, string Permission, string Role)
	{
		public static Names Fresh()
		{
			var id = "d" + Guid.NewGuid().ToString("N")[..8];
			return new Names($"decl-{id}", $"Requests {id}", $"{id}.handle", $"handler-{id}");
		}
	}

	private static PackageManifest Manifest(Names names, string version, int priority = 11)
		=> new PackageManifestService().ParseManifest($$"""
			format: 1.2
			package: {{names.Package}}
			version: "{{version}}"
			objects:
			  - ref: desk
			    type: thing
			    name: Request Desk
			categories:
			  roles:
			    - name: {{names.Category}}
			      description: Roles for the request queue.
			  permissions:
			    - name: {{names.Category}}
			      description: Permissions for the request queue.
			permissions:
			  - name: {{names.Permission}}
			    category: {{names.Category}}
			    description: Work on any request.
			roles:
			  - slug: {{names.Role}}
			    name: Handler
			    category: {{names.Category}}
			    priority: {{priority}}
			    permissions:
			      {{names.Permission}}: allow
			""").Expect<ParsedPackageManifest>().Manifest;

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, string commit = "commit-1")
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{manifest.Name}/", commit, "main"),
			new Dictionary<string, string>(), []));

	private async Task<SharpRole?> RoleAsync(string slug) => await Roles.GetRoleAsync(slug) is SharpRole role ? role : null;

	private async Task<bool> PermissionExistsAsync(string scope) => (await Roles.GetCustomPermissionsAsync()).Any(p => p.Scope == scope);

	private async Task<bool> CategoryExistsAsync(CategoryKind kind, string name)
		=> (await Roles.GetCategoriesAsync(kind)).Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

	[Test]
	public async Task InstallCreatesTheDeclaredItemsAndUninstallRemovesThem()
	{
		var names = Names.Fresh();
		var plan = await Installer.PlanAsync(Manifest(names, "1.0"));
		await Assert.That(plan.Declarations!.Select(d => d.Action).Distinct()).IsEquivalentTo([PackageDeclarationAction.Create]);

		(await ApplyAsync(Manifest(names, "1.0"))).Expect<PackageApplyResult>();

		var role = await RoleAsync(names.Role);
		await Assert.That(role).IsNotNull();
		await Assert.That(role!.Category).IsEqualTo(names.Category);
		await Assert.That(role.Permissions[names.Permission]).IsEqualTo(PermissionState.Allow);
		await Assert.That(await PermissionExistsAsync(names.Permission)).IsTrue();
		await Assert.That(await CategoryExistsAsync(CategoryKind.Role, names.Category)).IsTrue();
		await Assert.That(await CategoryExistsAsync(CategoryKind.Permission, names.Category)).IsTrue();
		var installed = (await Registry.GetInstalledPackageAsync(names.Package)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Owned!.Roles.Single().Slug).IsEqualTo(names.Role);

		await Assert.That((await Installer.UninstallAsync(names.Package)).Value).IsTypeOf<Success>();
		await Assert.That(await RoleAsync(names.Role)).IsNull();
		await Assert.That(await PermissionExistsAsync(names.Permission)).IsFalse();
		await Assert.That(await CategoryExistsAsync(CategoryKind.Role, names.Category)).IsFalse();
		await Assert.That(await CategoryExistsAsync(CategoryKind.Permission, names.Category)).IsFalse();
	}

	[Test]
	public async Task UninstallKeepsARoleSomeoneHolds()
	{
		var names = Names.Fresh();
		var applied = (await ApplyAsync(Manifest(names, "1.0"))).Expect<PackageApplyResult>();
		var desk = DBRef.Parse(applied.CreatedObjects["desk"]);
		await Roles.AssignRoleToObjectAsync(desk.Number, names.Role);

		try
		{
			await Assert.That((await Installer.UninstallAsync(names.Package)).Value).IsTypeOf<Success>();

			// The role stays because the desk holds it, the permission because the role allows it, and each
			// category because something is still in it.
			await Assert.That(await RoleAsync(names.Role)).IsNotNull();
			await Assert.That(await PermissionExistsAsync(names.Permission)).IsTrue();
			await Assert.That(await CategoryExistsAsync(CategoryKind.Role, names.Category)).IsTrue();
			await Assert.That(await CategoryExistsAsync(CategoryKind.Permission, names.Category)).IsTrue();

			// Kept items are the game's now: installing again leaves them as they are.
			var again = await Installer.PlanAsync(Manifest(names, "1.0"));
			await Assert.That(again.Declarations!.Single(d => d.Kind == PackageDeclarationKind.Role).Action).IsEqualTo(PackageDeclarationAction.Existing);
		}
		finally
		{
			await Roles.RemoveRoleFromObjectAsync(desk.Number, names.Role);
			await Roles.RemoveRoleAsync(names.Role);
			await Roles.RemoveCustomPermissionAsync(names.Permission);
			await Roles.RemoveCategoryAsync(CategoryKind.Role, names.Category);
			await Roles.RemoveCategoryAsync(CategoryKind.Permission, names.Category);
		}
	}

	[Test]
	public async Task AnUpgradeKeepsAnAdministratorsEdit()
	{
		var names = Names.Fresh();
		(await ApplyAsync(Manifest(names, "1.0"))).Expect<PackageApplyResult>();
		var role = (await RoleAsync(names.Role))!;
		role.Name = "Desk Staff";
		await Roles.UpsertRoleAsync(role);

		(await ApplyAsync(Manifest(names, "1.1", priority: 14), "commit-2")).Expect<PackageApplyResult>();

		var upgraded = (await RoleAsync(names.Role))!;
		await Assert.That(upgraded.Name).IsEqualTo("Desk Staff");
		await Assert.That(upgraded.Priority).IsEqualTo(14);

		await Assert.That((await Installer.UninstallAsync(names.Package)).Value).IsTypeOf<Success>();
		await Assert.That(await RoleAsync(names.Role)).IsNull();
	}

	[Test]
	public async Task AMissingCategoryRefusesTheApplyBeforeAnyWrite()
	{
		var names = Names.Fresh();
		var manifest = new PackageManifestService().ParseManifest($$"""
			format: 1.2
			package: {{names.Package}}
			version: "1.0"
			objects:
			  - ref: desk
			    type: thing
			    name: Request Desk
			permissions:
			  - name: {{names.Permission}}
			    category: {{names.Category}}
			    description: Work on any request.
			roles:
			  - slug: {{names.Role}}
			    category: {{names.Category}}
			""").Expect<ParsedPackageManifest>().Manifest;

		var plan = await Installer.PlanAsync(manifest);
		await Assert.That(plan.IsBlocked).IsTrue();
		var refused = (await ApplyAsync(manifest)).Expect<Error<string>>();
		await Assert.That(refused.Value).Contains(names.Category);

		await Assert.That(await PermissionExistsAsync(names.Permission)).IsFalse();
		await Assert.That(await RoleAsync(names.Role)).IsNull();
		await Assert.That(await Registry.GetInstalledPackageAsync(names.Package) is NotFound).IsTrue();
	}

	[Test]
	public async Task ARoleAnotherPackageOwnsBlocksTheApply()
	{
		var names = Names.Fresh();
		(await ApplyAsync(Manifest(names, "1.0"))).Expect<PackageApplyResult>();
		try
		{
			var rival = Manifest(names with { Package = names.Package + "-rival" }, "1.0");
			var plan = await Installer.PlanAsync(rival);
			await Assert.That(plan.IsBlocked).IsTrue();
			var refused = (await ApplyAsync(rival)).Expect<Error<string>>();
			await Assert.That(refused.Value).Contains(names.Package);
		}
		finally
		{
			await Installer.UninstallAsync(names.Package);
		}
	}
}
