using System.Security.Cryptography;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// What an apply or rollback must refuse before it writes anything: managed packages whose
/// dependencies or conflicts are unmet (#1169), application registrations that cannot be built
/// (#1170, #1171), rollbacks of packages whose resources the snapshot does not carry (#1172), and
/// rollbacks that must bring the package's objects back to what the revision owned (#1173).
/// Each test asserts the state a refusal must leave behind, not only the error.
/// </summary>
public class PackageInstallAdmissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;
	private IApplicationRegistryService Applications => (IApplicationRegistryService)Database;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private PackageManifestService Manifests { get; } = new();

	private static PackageApplySource Source(string commit = "commit-1") => new(
		"https://github.com/SharpMUSH/SharpMUSH-Packages", "admission/", commit, "main");

	private static PackageApplyRequest Request(
		IReadOnlyDictionary<string, string>? answers = null, bool allowManagedCode = false, string commit = "commit-1") =>
		new(Source(commit), answers ?? new Dictionary<string, string>(), [], 10, allowManagedCode);

	private PackageManifest Parse(string yaml) => Manifests.ParseManifest(yaml) switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	private static string CommandOnlyDllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "command-only", "CommandOnlyPlugin.dll");

	private static readonly string CommandOnlySha =
		Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(CommandOnlyDllPath))).ToLowerInvariant();

	private sealed class FixtureBinarySource : IManagedPackageBinarySource
	{
		public async Task<byte[]?> ReadBinaryAsync(string fileName, CancellationToken cancellationToken = default) =>
			fileName == "CommandOnlyPlugin.dll" ? await File.ReadAllBytesAsync(CommandOnlyDllPath, cancellationToken) : null;
	}

	/// <summary>A scratch plugins root and an installer whose managed deployments land in it, trusting every id.</summary>
	private sealed class ManagedScope : IDisposable
	{
		public required string PluginsRoot { get; init; }
		public required PackageInstallService Installer { get; init; }

		public bool Deposited(string packageId) => Directory.Exists(Path.Combine(PluginsRoot, packageId));

		public void Dispose()
		{
			if (Directory.Exists(PluginsRoot))
			{
				Directory.Delete(PluginsRoot, true);
			}
		}
	}

	private ManagedScope CreateManagedScope()
	{
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpkg-admission-{Guid.NewGuid():N}");
		var services = WebAppFactoryArg.Services;
		var managedInstaller = new ManagedPackageInstaller(
			services.GetRequiredService<IPluginManager>(),
			new ManagedPackageTrustOptions(true, []),
			NullLogger<ManagedPackageInstaller>.Instance,
			pluginsRoot);

		return new ManagedScope
		{
			PluginsRoot = pluginsRoot,
			Installer = new PackageInstallService(
				Database, Database, Database, Database, Registry, Applications,
				services.GetRequiredService<IPackagePlanService>(),
				services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
				services.GetRequiredService<IPackageLifecycleRunner>(),
				managedInstaller,
				services.GetRequiredService<IMediator>())
		};
	}

	private PackageManifest ManagedManifest(string id, string version, string relations = "") => Parse($"""
		package: {id}
		version: "{version}"
		kind: managed
		{relations}
		binaries:
		  min_server_version: ">=1.0"
		  files:
		    - file: CommandOnlyPlugin.dll
		      sha256: {CommandOnlySha}
		""");

	private async Task InstallSoftcodeAsync(string id, string version)
	{
		var manifest = Parse($"""
			package: {id}
			version: "{version}"
			objects:
			  - ref: marker
			    type: thing
			    name: {id} marker
			""");
		await Assert.That((await Installer.ApplyAsync(manifest, Request())).Value).IsTypeOf<PackageApplyResult>();
	}

	private async Task AssertNothingRecordedAsync(string packageId)
	{
		await Assert.That((await Registry.GetInstalledPackageAsync(packageId)).Value).IsTypeOf<NotFound>();
		await Assert.That(await Registry.GetPackageRevisionsAsync(packageId)).IsEmpty();
		await Assert.That(await Registry.GetPackageDependenciesAsync(packageId)).IsEmpty();
	}

	// ── #1169: managed installs honour dependencies and conflicts ───────────

	[Test, NotInParallel]
	public async Task Managed_MissingDependency_IsRejectedAndDepositsNothing()
	{
		using var scope = CreateManagedScope();
		var manifest = ManagedManifest("adm-managed-missing", "1.0.0", """
			depends:
			  - adm-absent-dependency: ">=1.0"
			""");

		var result = await scope.Installer.ApplyAsync(
			manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

		await Assert.That(result.Expect<Error<string>>().Value).Contains("requires adm-absent-dependency");
		await Assert.That(scope.Deposited("adm-managed-missing")).IsFalse();
		await AssertNothingRecordedAsync("adm-managed-missing");
	}

	[Test, NotInParallel]
	public async Task Managed_IncompatibleDependency_IsRejected()
	{
		using var scope = CreateManagedScope();
		await InstallSoftcodeAsync("adm-old-dependency", "1.0");
		try
		{
			var manifest = ManagedManifest("adm-managed-incompatible", "1.0.0", """
				depends:
				  - adm-old-dependency: ">=2.0"
				""");

			var result = await scope.Installer.ApplyAsync(
				manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

			await Assert.That(result.Expect<Error<string>>().Value).Contains("installed: 1.0.0");
			await Assert.That(scope.Deposited("adm-managed-incompatible")).IsFalse();
			await AssertNothingRecordedAsync("adm-managed-incompatible");
		}
		finally
		{
			await Installer.UninstallAsync("adm-old-dependency", force: true);
		}
	}

	[Test, NotInParallel]
	public async Task Managed_MatchingConflict_IsRejected()
	{
		using var scope = CreateManagedScope();
		await InstallSoftcodeAsync("adm-rival", "1.0");
		try
		{
			var manifest = ManagedManifest("adm-managed-conflict", "1.0.0", """
				conflicts:
				  - adm-rival: "<2.0"
				""");

			var result = await scope.Installer.ApplyAsync(
				manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

			await Assert.That(result.Expect<Error<string>>().Value).Contains("conflicts with installed adm-rival");
			await Assert.That(scope.Deposited("adm-managed-conflict")).IsFalse();
			await AssertNothingRecordedAsync("adm-managed-conflict");
		}
		finally
		{
			await Installer.UninstallAsync("adm-rival", force: true);
		}
	}

	[Test, NotInParallel]
	public async Task Managed_SatisfiedDependency_DeploysAndRecordsDependencyRows()
	{
		using var scope = CreateManagedScope();
		await InstallSoftcodeAsync("adm-good-dependency", "1.2");
		try
		{
			var manifest = ManagedManifest("adm-managed-happy", "1.0.0", """
				depends:
				  - adm-good-dependency: ">=1.0"
				""");

			var result = await scope.Installer.ApplyAsync(
				manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

			await Assert.That(result.Value).IsTypeOf<PackageApplyResult>();
			await Assert.That(scope.Deposited("adm-managed-happy")).IsTrue();
			var dependency = (await Registry.GetPackageDependenciesAsync("adm-managed-happy")).Single();
			await Assert.That(dependency.DependsOnId).IsEqualTo("adm-good-dependency");

			await Assert.That((await scope.Installer.UninstallAsync("adm-managed-happy")).Value).IsTypeOf<Success>();
		}
		finally
		{
			await Installer.UninstallAsync("adm-good-dependency", force: true);
		}
	}

	// ── #1170: the application registration is validated before anything is written ──

	private PackageManifest ApplicationManifest(string id, string minimumRole = "\"{{?access}}\"", string zones = "[]") => Parse($"""
		package: {id}
		version: 1.0.0
		kind: application
		configure:
		  access:
		    label: "Minimum role"
		    type: string
		    default: player
		  zone:
		    label: "Zone"
		    type: string
		    default: MainContent
		application:
		  slug: {id}
		  display_name: Admission Application
		  type: widget
		  schema_url: http/{id}/schema
		  minimum_role: {minimumRole}
		  zones: {zones}
		""");

	private async Task AssertApplicationRefusedAsync(
		PackageManifest manifest, IReadOnlyDictionary<string, string> answers, string offendingValue)
	{
		var result = await Installer.ApplyAsync(manifest, Request(answers));

		await Assert.That(result.Expect<Error<string>>().Value).Contains($"'{offendingValue}'");
		await AssertNothingRecordedAsync(manifest.Name);
		await Assert.That((await Applications.GetApplicationAsync(manifest.Application!.Slug)).Value).IsTypeOf<NotFound>();
	}

	[Test, NotInParallel]
	public async Task Application_InvalidConfiguredMinimumRole_PersistsNothing()
	{
		var manifest = ApplicationManifest("adm-app-role-answer");
		await AssertApplicationRefusedAsync(
			manifest, new Dictionary<string, string> { ["access"] = "overlord" }, "overlord");
	}

	[Test, NotInParallel]
	public async Task Application_InvalidLiteralMinimumRole_PersistsNothing()
	{
		// The manifest parser rejects a bad literal role, so the apply sees one only from a manifest
		// built another way; it must still refuse on the same path as a configured one.
		var parsed = ApplicationManifest("adm-app-role-literal");
		var manifest = parsed with { Application = parsed.Application! with { MinimumRole = "overlord" } };
		await AssertApplicationRefusedAsync(manifest, new Dictionary<string, string>(), "overlord");
	}

	[Test, NotInParallel]
	[Arguments("99")]
	[Arguments("-1")]
	public async Task Application_NumericMinimumRoleOutsideTheEnum_PersistsNothing(string role)
	{
		var manifest = ApplicationManifest($"adm-app-role-n{role.Trim('-')}");
		await AssertApplicationRefusedAsync(manifest, new Dictionary<string, string> { ["access"] = role }, role);
	}

	// ── #1171: an invalid zone is refused, not dropped ───────────────────────

	[Test, NotInParallel]
	public async Task Application_InvalidConfiguredZone_PersistsNothing()
	{
		var manifest = ApplicationManifest("adm-app-zone-answer", zones: "[\"{{?zone}}\"]");
		await AssertApplicationRefusedAsync(
			manifest, new Dictionary<string, string> { ["zone"] = "Basement" }, "Basement");
	}

	[Test, NotInParallel]
	public async Task Application_InvalidLiteralZone_PersistsNothing()
	{
		var parsed = ApplicationManifest("adm-app-zone-literal");
		var manifest = parsed with { Application = parsed.Application! with { Zones = ["MainContent", "Basement"] } };
		await AssertApplicationRefusedAsync(manifest, new Dictionary<string, string>(), "Basement");
	}

	[Test, NotInParallel]
	public async Task Application_NumericZoneOutsideTheEnum_PersistsNothing()
	{
		var manifest = ApplicationManifest("adm-app-zone-numeric", zones: "[\"{{?zone}}\"]");
		await AssertApplicationRefusedAsync(manifest, new Dictionary<string, string> { ["zone"] = "42" }, "42");
	}

	[Test, NotInParallel]
	public async Task Application_EmptyZoneList_IsValid()
	{
		var manifest = ApplicationManifest("adm-app-no-zones");
		var result = await Installer.ApplyAsync(manifest, Request(new Dictionary<string, string>()));

		await Assert.That(result.Value).IsTypeOf<PackageApplyResult>();
		var application = (await Applications.GetApplicationAsync("adm-app-no-zones")).Expect<RegisteredApplication>();
		await Assert.That(application.MinimumRole).IsEqualTo(PortalRole.Player);
		await Assert.That((await Installer.UninstallAsync("adm-app-no-zones")).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task Application_ConfiguredZone_IsRegistered()
	{
		var manifest = ApplicationManifest("adm-app-zone-ok", zones: "[\"{{?zone}}\"]");
		var result = await Installer.ApplyAsync(manifest, Request(new Dictionary<string, string> { ["zone"] = "rightsidebar" }));

		await Assert.That(result.Value).IsTypeOf<PackageApplyResult>();
		var application = (await Applications.GetApplicationAsync("adm-app-zone-ok")).Expect<RegisteredApplication>();
		await Assert.That(application.Zones!).IsEquivalentTo([Library.Models.Portal.Widgets.WidgetZone.RightSidebar]);
		await Assert.That((await Installer.UninstallAsync("adm-app-zone-ok")).Value).IsTypeOf<Success>();
	}

	// ── #1172: rollback refuses what its snapshot cannot restore ────────────

	[Test, NotInParallel]
	public async Task Rollback_OfAManagedPackage_IsRefusedAndChangesNothing()
	{
		using var scope = CreateManagedScope();
		var id = "adm-managed-rollback";
		await Assert.That((await scope.Installer.ApplyAsync(ManagedManifest(id, "1.0.0"), Request(allowManagedCode: true),
			CancellationToken.None, new FixtureBinarySource())).Value).IsTypeOf<PackageApplyResult>();
		await Assert.That((await scope.Installer.ApplyAsync(ManagedManifest(id, "2.0.0"), Request(allowManagedCode: true, commit: "commit-2"),
			CancellationToken.None, new FixtureBinarySource())).Value).IsTypeOf<PackageApplyResult>();

		var result = await scope.Installer.RollbackAsync(id, 1);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("managed package");
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Version).IsEqualTo("2.0.0");
		await Assert.That(installed.CurrentRevision).IsEqualTo(2);
		await Assert.That((await Registry.GetPackageRevisionsAsync(id)).Count).IsEqualTo(2);
		await Assert.That(scope.Deposited(id)).IsTrue();

		await Assert.That((await scope.Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task Rollback_OfAnApplicationPackage_IsRefusedAndChangesNothing()
	{
		var id = "adm-app-rollback";
		var v1 = ApplicationManifest(id);
		var v2 = v1 with
		{
			Version = PackageVersion.TryParse("2.0.0", out var two) ? two : throw new InvalidOperationException(),
			Application = v1.Application! with { DisplayName = "Admission Application v2" }
		};
		await Assert.That((await Installer.ApplyAsync(v1, Request())).Value).IsTypeOf<PackageApplyResult>();
		await Assert.That((await Installer.ApplyAsync(v2, Request(commit: "commit-2"))).Value).IsTypeOf<PackageApplyResult>();

		var result = await Installer.RollbackAsync(id, 1);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("application");
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Version).IsEqualTo("2.0.0");
		await Assert.That(installed.CurrentRevision).IsEqualTo(2);
		var application = (await Applications.GetApplicationAsync(id)).Expect<RegisteredApplication>();
		await Assert.That(application.DisplayName).IsEqualTo("Admission Application v2");

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	// ── #1173: rollback brings the package's objects back to what the revision owned ──

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>A softcode package owning a thing per ref, optionally attached to <c>{{?host}}</c>.</summary>
	private PackageManifest ObjectsManifest(string id, string version, bool attach, params string[] refs) => Parse($"""
		package: {id}
		version: "{version}"
		configure:
		  host:
		    label: "Object to attach to"
		objects:
		{string.Concat(refs.Select(r => $"  - ref: {r}\n    type: thing\n    name: {id} {r}\n"))}{(attach ? "  - ref: hook\n    target: \"{{?host}}\"\n    attributes:\n      CMD_ADM: |-\n        $+adm:@pemit %#=attached\n" : "")}
		""");

	private async Task<Dictionary<string, string>> AttachHostAsync()
	{
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<AnySharpObject>();
		var location = pmNode.AsContainer;
		var host = await Database.CreateThingAsync("Admission Attach Host", location, pmNode.Expect<SharpPlayer>(), location);
		var objid = (await Database.GetObjectNodeAsync(host)).Expect<AnySharpObject>().Object().DBRef.ToString();
		return new Dictionary<string, string> { ["host"] = objid };
	}

	private async Task<bool> IsGoingAsync(string objid)
	{
		var node = (await Database.GetObjectNodeAsync(DBRef.Parse(objid))).Expect<AnySharpObject>();
		return await node.Object().Flags.Value.AnyAsync(f => f.Name == "GOING");
	}

	private async Task<string[]> RegisteredObjidsAsync(string packageId) =>
		(await Registry.GetPackageObjectsAsync(packageId)).Select(o => o.Objid).Order().ToArray();

	private async Task<IReadOnlyDictionary<string, string>> InstallAsync(
		PackageManifest manifest, IReadOnlyDictionary<string, string>? answers = null, string commit = "commit-1") =>
		(await Installer.ApplyAsync(manifest, Request(answers, commit: commit))).Expect<PackageApplyResult>().CreatedObjects;

	[Test, NotInParallel]
	public async Task Rollback_ReleasesAnObjectAddedAfterTheTarget()
	{
		const string id = "adm-rb-added";
		var first = await InstallAsync(ObjectsManifest(id, "1.0", false, "keep"));
		var second = await InstallAsync(ObjectsManifest(id, "2.0", false, "keep", "later"), commit: "commit-2");

		await Assert.That((await Installer.RollbackAsync(id, 1)).Value).IsTypeOf<PackageRollbackResult>();

		await Assert.That(await RegisteredObjidsAsync(id)).IsEquivalentTo([first["keep"]]);
		await Assert.That(await IsGoingAsync(second["later"])).IsTrue();
		await Assert.That(await IsGoingAsync(first["keep"])).IsFalse();
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task Rollback_ReRegistersAnObjectRemovedAfterTheTarget()
	{
		const string id = "adm-rb-removed";
		var first = await InstallAsync(ObjectsManifest(id, "1.0", false, "keep", "dropped"));
		await InstallAsync(ObjectsManifest(id, "2.0", false, "keep"), commit: "commit-2");
		await Assert.That(await IsGoingAsync(first["dropped"])).IsTrue();

		await Assert.That((await Installer.RollbackAsync(id, 1)).Value).IsTypeOf<PackageRollbackResult>();

		await Assert.That(await RegisteredObjidsAsync(id)).IsEquivalentTo(new[] { first["keep"], first["dropped"] }.Order().ToArray());
		await Assert.That(await IsGoingAsync(first["dropped"])).IsFalse();
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task Rollback_RefusesWhenAnObjectItOwnedWasDestroyed()
	{
		const string id = "adm-rb-destroyed";
		var first = await InstallAsync(ObjectsManifest(id, "1.0", false, "keep", "gone"));
		await InstallAsync(ObjectsManifest(id, "2.0", false, "keep"), commit: "commit-2");
		await Mediator.Send(new DeleteObjectCommand(DBRef.Parse(first["gone"])));

		var result = await Installer.RollbackAsync(id, 1);

		await Assert.That(result.Expect<Error<string>>().Value).Contains($"{first["gone"]} ({{{{gone}}}}) was destroyed");
		await Assert.That(await RegisteredObjidsAsync(id)).IsEquivalentTo([first["keep"]]);
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Version).IsEqualTo("2.0.0");
		await Assert.That(installed.CurrentRevision).IsEqualTo(2);
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task Rollback_NeverRegistersAnAttachTarget()
	{
		const string id = "adm-rb-attach";
		var answers = await AttachHostAsync();
		var first = await InstallAsync(ObjectsManifest(id, "1.0", true, "keep", "dropped"), answers);
		await InstallAsync(ObjectsManifest(id, "2.0", true, "keep"), answers, "commit-2");

		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(
			(await Registry.GetPackageRevisionAsync(id, 1)).Expect<PackageRevisionRecord>().ManifestSnapshotJson,
			new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
		await Assert.That(snapshot.Objects.Single(o => o.Ref == "hook").Relation).IsEqualTo(PackageObjectRelation.Attached);
		await Assert.That(snapshot.Objects.Single(o => o.Ref == "keep").Relation).IsEqualTo(PackageObjectRelation.Owned);

		await Assert.That((await Installer.RollbackAsync(id, 1)).Value).IsTypeOf<PackageRollbackResult>();

		await Assert.That(await RegisteredObjidsAsync(id)).IsEquivalentTo(new[] { first["keep"], first["dropped"] }.Order().ToArray());
		await Assert.That(await IsGoingAsync(answers["host"])).IsFalse();
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That(await IsGoingAsync(answers["host"])).IsFalse()
			.Because("uninstall destroys only what the package owns, and the rollback must not have made it the host's owner");
	}

	/// <summary>
	/// A revision written before <see cref="PackageObjectRelation"/> existed lists attached and owned
	/// objects alike, with no relation property at all.
	/// </summary>
	private async Task AddLegacyRevisionAsync(string packageId, int revision, params (string Ref, string Objid)[] objects)
	{
		var json = JsonSerializer.Serialize(new
		{
			version = "1.0.0",
			objects = objects.Select(o => new { @ref = o.Ref, objid = o.Objid, type = "thing" }),
			attributes = Array.Empty<object>()
		});
		await Registry.AddPackageRevisionAsync(new PackageRevisionRecord(packageId, revision, PackageRevisionKind.Install,
			"1.0.0", "legacy", json, "{}", "[]", DateTimeOffset.UtcNow));
	}

	[Test, NotInParallel]
	public async Task Rollback_ToALegacyRevision_RefusesAnEntryItCannotClassify()
	{
		const string id = "adm-rb-legacy-ambiguous";
		var answers = await AttachHostAsync();
		var created = await InstallAsync(ObjectsManifest(id, "1.0", true, "keep"), answers);
		await AddLegacyRevisionAsync(id, 50, ("keep", created["keep"]), ("hook", answers["host"]));

		var result = await Installer.RollbackAsync(id, 50);

		await Assert.That(result.Expect<Error<string>>().Value).Contains($"{answers["host"]} ({{{{hook}}}}) is not registered");
		await Assert.That(await RegisteredObjidsAsync(id)).IsEquivalentTo([created["keep"]]);
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>().CurrentRevision).IsEqualTo(1);
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That(await IsGoingAsync(answers["host"])).IsFalse();
	}

	[Test, NotInParallel]
	public async Task Rollback_ToALegacyRevision_KeepsWhatTheRegistryProvesWasOwned()
	{
		const string id = "adm-rb-legacy-owned";
		var first = await InstallAsync(ObjectsManifest(id, "1.0", false, "keep"));
		var second = await InstallAsync(ObjectsManifest(id, "2.0", false, "keep", "later"), commit: "commit-2");
		await AddLegacyRevisionAsync(id, 50, ("keep", first["keep"]));

		await Assert.That((await Installer.RollbackAsync(id, 50)).Value).IsTypeOf<PackageRollbackResult>();

		await Assert.That(await RegisteredObjidsAsync(id)).IsEquivalentTo([first["keep"]]);
		await Assert.That(await IsGoingAsync(second["later"])).IsTrue();
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}
}
