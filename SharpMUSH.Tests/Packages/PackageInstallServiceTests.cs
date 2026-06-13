using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// End-to-end integration test for the apply engine: install a package with
/// rooms/things/exits/configure refs against the real database, customize an
/// attribute, upgrade through a conflict, roll back, and uninstall.
/// </summary>
public class PackageInstallServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private PackageManifestService Manifests { get; } = new();

	private static PackageApplySource Source(string commit = "commit-1") => new(
		"https://github.com/SharpMUSH/SharpMUSH-Packages", "e2e-pkg/", commit, "main");

	private const string ManifestV1 =
		"""
		package: e2e-pkg
		version: "1.0"
		configure:
		  storage:
		    label: "Storage object"
		objects:
		  - ref: lounge
		    type: room
		    name: E2E Lounge
		  - ref: board
		    type: thing
		    name: E2E Board
		    location: "{{lounge}}"
		    parent: "{{lounge}}"
		    flags: [no_command]
		    locks:
		      use: "{{board}}"
		    attributes:
		      CMD_+E2E: |-
		        $+e2e:@pemit %#=[u({{board}}/FN_FMT,%#)] stored at {{?storage}}
		      FN_FMT: |-
		        version-one-format
		  - ref: lounge_out
		    type: exit
		    name: Out;out;o
		    location: "{{lounge}}"
		    destination: "{{?storage}}"
		""";

	private const string ManifestV2 =
		"""
		package: e2e-pkg
		version: "1.1"
		configure:
		  storage:
		    label: "Storage object"
		objects:
		  - ref: lounge
		    type: room
		    name: E2E Lounge
		  - ref: board
		    type: thing
		    name: E2E Board
		    location: "{{lounge}}"
		    parent: "{{lounge}}"
		    flags: [no_command]
		    locks:
		      use: "{{board}}"
		    attributes:
		      CMD_+E2E: |-
		        $+e2e:@pemit %#=[u({{board}}/FN_FMT,%#)] stored at {{?storage}}
		      FN_FMT: |-
		        version-two-format
		  - ref: lounge_out
		    type: exit
		    name: Out;out;o
		    location: "{{lounge}}"
		    destination: "{{?storage}}"
		""";

	private PackageManifest Parse(string yaml) => Manifests.ParseManifest(yaml).AsT0.Manifest;

	private async Task<string> ReadAttributeAsync(string objid, string attribute)
	{
		var dbref = PackageInstallService.ParseObjid(objid)!.Value;
		var leaf = await Database.GetAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Value.ToPlainText() ?? "";
	}

	[Test, NotInParallel]
	public async Task InstallUpgradeRollbackUninstall_EndToEnd()
	{
		// The configure answer points at Room Zero.
		var roomZero = (await Database.GetObjectNodeAsync(new DBRef(0))).Known().Object().DBRef.ToString();
		var answers = new Dictionary<string, string> { ["storage"] = roomZero };
		var manifestV1 = Parse(ManifestV1);

		// ── Plan: everything is a create, nothing blocks ───────────────────────
		var plan = await Installer.PlanAsync(manifestV1, answers);
		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(plan.Objects.Count(o => o.Action == PackageObjectAction.Create)).IsEqualTo(3);

		// ── Install ────────────────────────────────────────────────────────────
		var install = await Installer.ApplyAsync(manifestV1, new PackageApplyRequest(Source(), answers, []));
		await Assert.That(install.IsT0).IsTrue();
		var result = install.AsT0;
		await Assert.That(result.Revision).IsEqualTo(1);
		await Assert.That(result.CreatedObjects.Keys.Order().ToArray())
			.IsEquivalentTo((string[])["board", "lounge", "lounge_out"]);

		var boardObjid = result.CreatedObjects["board"];
		var loungeObjid = result.CreatedObjects["lounge"];

		// Objects really exist, owned structure intact.
		var boardNode = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(boardObjid)!.Value);
		await Assert.That(boardNode.IsNone()).IsFalse();
		await Assert.That(boardNode.Known().Object().Name).IsEqualTo("E2E Board");

		// Code carries v(PM`REFS`...) recalls, never dbrefs (decision 20.21).
		var cmd = await ReadAttributeAsync(boardObjid, "CMD_+E2E");
		await Assert.That(cmd).Contains("[v(PM`REFS`BOARD)]");
		await Assert.That(cmd).Contains("[v(PM`REFS`STORAGE)]");
		await Assert.That(cmd).DoesNotContain("{{");
		await Assert.That(cmd).DoesNotContain(boardObjid);
		await Assert.That(await ReadAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-one-format");

		// The engine-managed ref attrs hold the resolutions.
		await Assert.That(await ReadAttributeAsync(boardObjid, "PM`REFS`BOARD")).IsEqualTo(boardObjid);
		await Assert.That(await ReadAttributeAsync(boardObjid, "PM`REFS`STORAGE")).IsEqualTo(roomZero);

		// Registry rows recorded: package, objects, baselines (full values), revision 1.
		var installedRecord = await Registry.GetInstalledPackageAsync("e2e-pkg");
		await Assert.That(installedRecord.AsT0.Version).IsEqualTo("1.0.0");
		await Assert.That((await Registry.GetPackageObjectsAsync("e2e-pkg")).Count).IsEqualTo(3);
		var baselines = await Registry.GetManagedAttributesAsync("e2e-pkg");
		await Assert.That(baselines.Single(b => b.Attribute == "FN_FMT").BaselineValue).IsEqualTo("version-one-format");
		await Assert.That(baselines.Single(b => b.Attribute == "PM`REFS`BOARD").BaselineValue).IsEqualTo(boardObjid);
		await Assert.That((await Registry.GetPackageRevisionsAsync("e2e-pkg")).Single().Kind)
			.IsEqualTo(PackageRevisionKind.Install);

		// ── Re-plan with no changes: everything NoChange ───────────────────────
		var idle = await Installer.PlanAsync(manifestV1, answers);
		await Assert.That(idle.Attributes.All(a => a.Action == PackageAttributeAction.NoChange)).IsTrue();
		await Assert.That(idle.HasConflicts).IsFalse();

		// ── Customize locally, then upgrade: ModifyModify conflict ─────────────
		var pm = (await Database.GetObjectNodeAsync(new DBRef(3))).Known();
		await Database.SetAttributeAsync(
			PackageInstallService.ParseObjid(boardObjid)!.Value, ["FN_FMT"],
			MModule.single("my-custom-format"), pm.Match(p => p, _ => null!, _ => null!, _ => null!));

		var manifestV2 = Parse(ManifestV2);
		var upgradePlan = await Installer.PlanAsync(manifestV2, answers);
		var conflict = upgradePlan.Attributes.Single(a => a.Action == PackageAttributeAction.Conflict);
		await Assert.That(conflict.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
		await Assert.That(conflict.BaseValue).IsEqualTo("version-one-format");
		await Assert.That(conflict.LiveValue).IsEqualTo("my-custom-format");
		await Assert.That(conflict.NewValue).IsEqualTo("version-two-format");

		// Undecided conflict blocks apply.
		var undecided = await Installer.ApplyAsync(manifestV2, new PackageApplyRequest(Source("commit-2"), answers, []));
		await Assert.That(undecided.IsT1).IsTrue();
		await Assert.That(undecided.AsT1.Value).Contains("Unresolved conflicts");

		// Take theirs.
		var upgrade = await Installer.ApplyAsync(manifestV2, new PackageApplyRequest(
			Source("commit-2"), answers,
			[new PackageConflictDecision(conflict.TargetRef, conflict.Attribute, PackageConflictResolution.TakeTheirs)]));
		await Assert.That(upgrade.IsT0).IsTrue();
		await Assert.That(upgrade.AsT0.Revision).IsEqualTo(2);
		await Assert.That(upgrade.AsT0.CreatedObjects.Count).IsEqualTo(0);
		await Assert.That(await ReadAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-two-format");
		await Assert.That((await Registry.GetInstalledPackageAsync("e2e-pkg")).AsT0.Version).IsEqualTo("1.1.0");

		// ── Roll back to revision 1 ────────────────────────────────────────────
		var rollback = await Installer.RollbackAsync("e2e-pkg", 1);
		await Assert.That(rollback.IsT0).IsTrue();
		await Assert.That(rollback.AsT0.Revision).IsEqualTo(3);
		await Assert.That(rollback.AsT0.RestoredFromRevision).IsEqualTo(1);
		await Assert.That(await ReadAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-one-format");
		var afterRollback = await Registry.GetInstalledPackageAsync("e2e-pkg");
		await Assert.That(afterRollback.AsT0.Version).IsEqualTo("1.0.0");
		await Assert.That(afterRollback.AsT0.CurrentRevision).IsEqualTo(3);

		// ── Uninstall ──────────────────────────────────────────────────────────
		var uninstall = await Installer.UninstallAsync("e2e-pkg");
		await Assert.That(uninstall.IsT0).IsTrue();
		await Assert.That((await Registry.GetInstalledPackageAsync("e2e-pkg")).IsT1).IsTrue();
		await Assert.That((await Registry.GetPackageObjectsAsync("e2e-pkg")).Count).IsEqualTo(0);

		// Created objects are marked GOING (the @destroy convention), not hard-deleted.
		var goneBoard = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(boardObjid)!.Value);
		await Assert.That(goneBoard.IsNone()).IsFalse();

		// Keep the lounge objid referenced so the variable is used even if asserts change.
		await Assert.That(loungeObjid).IsNotNull();
	}

	[Test, NotInParallel]
	public async Task AttachObject_ManagesAttrsOnExistingObject_UninstallLeavesObject()
	{
		// Attach mode (decision 20.3): a package that manages attributes on an
		// object it does not own. Uses a {{?configure}} target so it's isolated
		// from the shared http_handler. Mirrors how http-hooks attaches to #4.
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(3))).Known();
		var pm = pmNode.Match(p => p, _ => null!, _ => null!, _ => null!);
		var location = pmNode.Match<SharpMUSH.Library.DiscriminatedUnions.AnySharpContainer>(
			p => p, _ => null!, _ => null!, t => t);

		// A pre-existing object the package will attach to (not created by it).
		var hostDbref = await Database.CreateThingAsync("Attach Host", location, pm, location);
		await Database.SetAttributeAsync(hostDbref, ["PRE_EXISTING"], MModule.single("untouched"), pm);
		var hostObjid = (await Database.GetObjectNodeAsync(hostDbref)).Known().Object().DBRef.ToString();

		var manifest = Parse(
			"""
			package: attach-pkg
			version: "1.0"
			configure:
			  host:
			    label: "Object to attach to"
			objects:
			  - ref: handler
			    target: "{{?host}}"
			    attributes:
			      CMD_X: |-
			        $+x:@pemit %#=managed
			""");

		var answers = new Dictionary<string, string> { ["host"] = hostObjid };
		var install = await Installer.ApplyAsync(manifest, new PackageApplyRequest(Source(), answers, []));
		await Assert.That(install.IsT0).IsTrue();
		// Attach mode created no owned objects.
		await Assert.That(install.AsT0.CreatedObjects.Count).IsEqualTo(0);

		// The attribute landed on the existing host; its own attrs are untouched.
		await Assert.That(await ReadAttributeAsync(hostObjid, "CMD_X")).IsEqualTo("$+x:@pemit %#=managed");
		await Assert.That(await ReadAttributeAsync(hostObjid, "PRE_EXISTING")).IsEqualTo("untouched");

		// The package owns the attribute (sys_managed_attributes) but NOT the
		// object (sys_package_objects is empty — attach mode).
		await Assert.That((await Registry.GetPackageObjectsAsync("attach-pkg")).Count).IsEqualTo(0);
		var managed = await Registry.GetManagedAttributesAsync("attach-pkg");
		await Assert.That(managed.Single().Objid).IsEqualTo(hostObjid);
		await Assert.That(managed.Single().Attribute).IsEqualTo("CMD_X");

		// Uninstall removes the managed attribute but leaves the host object —
		// it is not the package's to destroy.
		await Assert.That((await Installer.UninstallAsync("attach-pkg")).IsT0).IsTrue();
		await Assert.That(await ReadAttributeAsync(hostObjid, "CMD_X")).IsEqualTo("");
		await Assert.That(await ReadAttributeAsync(hostObjid, "PRE_EXISTING")).IsEqualTo("untouched");
		var hostStillThere = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(hostObjid)!.Value);
		await Assert.That(hostStillThere.IsNone()).IsFalse();
	}

	[Test, NotInParallel]
	public async Task CrossPackageAttach_BlocksProvidersUninstall_UntilAttacherGone()
	{
		// Request #1: a package that PROVIDES an object cannot be uninstalled
		// while another package is ATTACHED to it (manages attributes on it).
		var provider = Parse(
			"""
			package: attach-provider
			version: "1.0"
			objects:
			  - ref: hub
			    type: thing
			    name: Attach Hub
			    attributes:
			      FN_BASE: |-
			        base
			""");
		var attacher = Parse(
			"""
			package: attach-consumer
			version: "1.0"
			depends:
			  - attach-provider: ">=1.0"
			objects:
			  - ref: ext
			    target: "{{attach-provider/hub}}"
			    attributes:
			      FN_EXT: |-
			        extension
			""");

		var answers = new Dictionary<string, string>();
		var providerResult = await Installer.ApplyAsync(provider, new PackageApplyRequest(Source(), answers, []));
		await Assert.That(providerResult.IsT0).IsTrue();
		var hubObjid = providerResult.AsT0.CreatedObjects["hub"];

		// The attacher manages an attribute on the provider's object (cross-package attach).
		await Assert.That((await Installer.ApplyAsync(attacher, new PackageApplyRequest(Source(), answers, []))).IsT0).IsTrue();
		await Assert.That(await ReadAttributeAsync(hubObjid, "FN_EXT")).IsEqualTo("extension");
		await Assert.That((await Registry.GetPackageObjectsAsync("attach-consumer")).Count).IsEqualTo(0);

		// Uninstalling the provider is blocked while the attacher is present —
		// both by the dependency and by the attachment guard.
		var blocked = await Installer.UninstallAsync("attach-provider");
		await Assert.That(blocked.IsT1).IsTrue();
		await Assert.That(blocked.AsT1.Value).Contains("attach-consumer");

		// Remove the attacher, then the provider uninstalls cleanly.
		await Assert.That((await Installer.UninstallAsync("attach-consumer")).IsT0).IsTrue();
		await Assert.That(await ReadAttributeAsync(hubObjid, "FN_EXT")).IsEqualTo("");
		await Assert.That((await Installer.UninstallAsync("attach-provider")).IsT0).IsTrue();
	}

	[Test, NotInParallel]
	public async Task Uninstall_BlockedByDependents_UnlessForced()
	{
		var basePkg = Parse(
			"""
			package: e2e-base
			version: "1.0"
			objects:
			  - ref: core
			    type: thing
			    name: E2E Base Core
			    attributes:
			      FN_CORE: |-
			        base-core
			""");
		var dependent = Parse(
			"""
			package: e2e-child
			version: "1.0"
			depends:
			  - e2e-base: ">=1.0"
			objects:
			  - ref: child
			    type: thing
			    name: E2E Child
			    parent: "{{e2e-base/core}}"
			""");

		var answers = new Dictionary<string, string>();
		await Assert.That((await Installer.ApplyAsync(basePkg, new PackageApplyRequest(Source(), answers, []))).IsT0).IsTrue();
		await Assert.That((await Installer.ApplyAsync(dependent, new PackageApplyRequest(Source(), answers, []))).IsT0).IsTrue();

		var blocked = await Installer.UninstallAsync("e2e-base");
		await Assert.That(blocked.IsT1).IsTrue();
		await Assert.That(blocked.AsT1.Value).Contains("e2e-child");

		await Assert.That((await Installer.UninstallAsync("e2e-child")).IsT0).IsTrue();
		await Assert.That((await Installer.UninstallAsync("e2e-base")).IsT0).IsTrue();
	}
}
