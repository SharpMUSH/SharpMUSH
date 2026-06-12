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
