using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Round-trip coverage for the four registries (layouts, applications, roles,
/// packages), directly against a <see cref="LightningDatabase"/> instance — no host, no DI —
/// mirroring <see cref="MigrationTests"/>'s fixture. The full-stack suites under
/// <c>SharpMUSH.Tests/Database</c> (LayoutRegistryTests, ApplicationRegistryTests, RoleRegistryTests,
/// PackageRegistryTests) already exercise these interfaces through <c>ServerWebAppFactory</c> against
/// whichever provider is active; this class pins down the Lightning-specific storage shape —
/// composite keys and the account-role/package-dependency dup-sort tables — as its own fast,
/// provider-scoped test.
/// </summary>
public class RegistriesTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static async Task WithDatabaseAsync(Func<LightningDatabase, Task> body)
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);
		try
		{
			await body(db);
		}
		finally
		{
			await db.DisposeAsync();
			if (Directory.Exists(path))
			{
				try
				{
					Directory.Delete(path, recursive: true);
				}
				catch (IOException)
				{
					// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
					// join by a few milliseconds under load — matches MigrationTests's own cleanup.
				}
			}
		}
	}

	[Test]
	public async Task Layout_RoundTrips() => await WithDatabaseAsync(async db =>
	{
		ILayoutRegistryService registry = db;
		var layout = new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("SomeWidget", 0, null)]
			},
			new LayoutSettings(LeftSidebarEnabled: true, RightSidebarEnabled: false));

		await registry.UpsertLayoutAsync("scope-a", layout);
		var fetched = await registry.GetLayoutAsync("scope-a");
		await Assert.That(fetched.Expect<LayoutConfiguration>().Zones[WidgetZone.MainContent][0].WidgetName).IsEqualTo("SomeWidget");
		await Assert.That((await registry.GetCustomizedScopesAsync())).Contains("scope-a");

		await registry.RemoveLayoutAsync("scope-a");
		await Assert.That((await registry.GetLayoutAsync("scope-a")).Value).IsTypeOf<NotFound>();
	});

	[Test]
	public async Task Application_RoundTrips() => await WithDatabaseAsync(async db =>
	{
		IApplicationRegistryService registry = db;
		var app = new RegisteredApplication(
			"app-a", "App A", null, ApplicationKind.Page, "http/app-a/schema", null, null,
			PortalRole.Player, "main", null, 0);

		await registry.UpsertApplicationAsync(app);
		var fetched = await registry.GetApplicationAsync("app-a");
		await Assert.That(fetched.Value).IsEqualTo(app);

		await registry.RemoveApplicationAsync("app-a");
		await Assert.That((await registry.GetApplicationAsync("app-a")).Value).IsTypeOf<NotFound>();
	});

	[Test]
	public async Task Application_RoundTrips_ScopeAndOobPackage() => await WithDatabaseAsync(async db =>
	{
		IApplicationRegistryService registry = db;
		var app = new RegisteredApplication(
			"weather", "Weather", null, ApplicationKind.Widget, "http/weather/schema", null, null,
			PortalRole.Player, null, [WidgetZone.RightSidebar], 30,
			Scope: "play", OobPackage: "weather.now", Permission: "weather.staff", NavUrl: "http/weather/nav");

		await registry.UpsertApplicationAsync(app);
		var fetched = (await registry.GetApplicationAsync("weather")).Expect<RegisteredApplication>();

		await Assert.That(fetched.Scope).IsEqualTo("play");
		await Assert.That(fetched.OobPackage).IsEqualTo("weather.now");
		await Assert.That(fetched.Permission).IsEqualTo("weather.staff");
		await Assert.That(fetched.NavUrl).IsEqualTo("http/weather/nav");
	});

	[Test]
	public async Task Application_StoredBeforeScopeExisted_LoadsWithoutScope() => await WithDatabaseAsync(async db =>
	{
		// The record exactly as a build without Scope/OobPackage wrote it: neither key is present.
		const string legacy = """
			{"Slug":"old-app","DisplayName":"Old App","Icon":null,"Kind":"Page","SchemaUrl":"http/old/schema",
			 "DataUrl":null,"SubmitRoute":null,"MinimumRole":"Player","NavPlacement":"main","Zones":"",
			 "SortOrder":4,"OwningPackage":null,"RenderKind":"Schema","ComponentAssemblyUrl":null,"ComponentTypeName":null}
			""";
		await db.Store.WriteAsync(tx =>
			tx.Put(Tables.App, Keys.Str("old-app"), System.Text.Encoding.UTF8.GetBytes(legacy)));

		IApplicationRegistryService registry = db;
		var fetched = (await registry.GetApplicationAsync("old-app")).Expect<RegisteredApplication>();

		await Assert.That(fetched.DisplayName).IsEqualTo("Old App");
		await Assert.That(fetched.Order).IsEqualTo(4);
		await Assert.That(fetched.Scope).IsNull();
		await Assert.That(fetched.OobPackage).IsNull();
		await Assert.That(fetched.Permission).IsNull();
		await Assert.That(fetched.NavUrl).IsNull();
	});

	[Test]
	public async Task Role_RoundTrips_WithAccountAssignment() => await WithDatabaseAsync(async db =>
	{
		IRoleRegistryService registry = db;
		var role = new SharpRole
		{
			Slug = "role-a",
			Name = "Role A",
			Priority = 10,
			Permissions = new Dictionary<string, PermissionState> { [PortalPermission.WikiAdmin] = PermissionState.Allow },
			CreatedAt = 1,
			UpdatedAt = 1
		};

		await registry.UpsertRoleAsync(role);
		var fetched = await registry.GetRoleAsync("role-a");
		await Assert.That(fetched.Expect<SharpRole>().Permissions[PortalPermission.WikiAdmin]).IsEqualTo(PermissionState.Allow);

		await registry.AssignRoleToAccountAsync("acct-1", "role-a");
		await registry.AssignRoleToAccountAsync("acct-1", "role-a"); // idempotent
		var roles = await registry.GetRolesForAccountAsync("acct-1");
		await Assert.That(roles.Count(r => r.Slug == "role-a")).IsEqualTo(1);
		await Assert.That((await registry.GetAccountIdsForRoleAsync("role-a")).Count).IsEqualTo(1);

		await registry.RemoveRoleFromAccountAsync("acct-1", "role-a");
		await Assert.That((await registry.GetRolesForAccountAsync("acct-1")).Any(r => r.Slug == "role-a")).IsFalse();

		await registry.RemoveRoleAsync("role-a");
		await Assert.That((await registry.GetRoleAsync("role-a")).Value).IsTypeOf<NotFound>();
	});

	[Test]
	public async Task Package_CompositeKeys_RoundTrip() => await WithDatabaseAsync(async db =>
	{
		IPackageRegistryService registry = db;
		var installed = new InstalledPackageRecord(
			"pkg-a", "1.0.0", "https://example.com/repo", null, "commit-1", "main",
			new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1);
		await registry.UpsertInstalledPackageAsync(installed);

		// ManagedAttributeRecord is the one package record keyed by a three-segment composite
		// (packageId/objid/attribute) — the identity every other package record type falls short of.
		await registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			"pkg-a", "#100:1", "FN_ONE", "value-one", "hash-1", "1.0.0"));
		await registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			"pkg-a", "#100:1", "FN_TWO", "value-two", "hash-2", "1.0.0"));
		await registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			"pkg-a", "#101:1", "FN_ONE", "value-three", "hash-3", "1.0.0"));

		var byPackage = await registry.GetManagedAttributesAsync("pkg-a");
		await Assert.That(byPackage.Count).IsEqualTo(3);

		var byObject = await registry.GetManagedAttributesForObjectAsync("#100:1");
		await Assert.That(byObject.Count).IsEqualTo(2);
		await Assert.That(byObject.Select(a => a.Attribute).Order().ToArray())
			.IsEquivalentTo((string[])["FN_ONE", "FN_TWO"]);

		await registry.RemoveManagedAttributeAsync("pkg-a", "#100:1", "FN_ONE");
		await Assert.That((await registry.GetManagedAttributesAsync("pkg-a")).Count).IsEqualTo(2);

		// Dependency edges use a dup-sort table keyed by the owning package; a second package's
		// entry pointing at pkg-a must survive pkg-a's own outbound edges being cleared.
		await registry.UpsertInstalledPackageAsync(installed with { Id = "pkg-b" });
		await registry.SetPackageDependenciesAsync("pkg-b", [new PackageDependencyRecord("pkg-b", "pkg-a", "")]);
		await Assert.That((await registry.GetPackageDependentsAsync("pkg-a")).Count).IsEqualTo(1);

		await registry.RemoveInstalledPackageAsync("pkg-a");
		await Assert.That((await registry.GetManagedAttributesAsync("pkg-a")).Count).IsEqualTo(0);
		await Assert.That((await registry.GetPackageDependenciesAsync("pkg-b")).Count).IsEqualTo(0);

		await registry.RemoveInstalledPackageAsync("pkg-b");
	});
}
