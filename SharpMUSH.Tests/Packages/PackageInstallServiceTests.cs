using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
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
	private IApplicationRegistryService Applications => (IApplicationRegistryService)Database;
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

	private async Task<IReadOnlyList<string>> ReadAttributeFlagsAsync(string objid, string attribute)
	{
		var dbref = PackageInstallService.ParseObjid(objid)!.Value;
		var leaf = await Database.GetAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Flags.Select(f => f.Name).ToList() ?? [];
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
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Known();
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
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(7))).Known();
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

	[Test, NotInParallel]
	public async Task ApplicationPackage_RegistersAndUnregisters_PortalApplication()
	{
		// An application package owns no objects: it depends on a softcode package
		// and registers a portal application, with a {{?configure}} ref tuning the
		// minimum role at apply (decision 20.22).
		var routes = Parse(
			"""
			package: appdep-routes
			version: "1.0"
			objects:
			  - ref: marker
			    type: thing
			    name: Appdep Routes Marker
			""");

		var appPackage = Parse(
			"""
			package: appdep-app
			version: 1.0.0
			kind: application
			depends:
			  - appdep-routes: ">=1.0"
			configure:
			  access:
			    label: "Minimum role"
			    type: string
			    default: player
			application:
			  slug: appdep
			  display_name: Appdep Application
			  icon: assignment_ind
			  type: page
			  schema_url: http/appdep/schema
			  submit_route: http/appdep
			  minimum_role: "{{?access}}"
			  nav_placement: main
			  order: 50
			""");

		var answers = new Dictionary<string, string> { ["access"] = "wizard" };

		// The dependency must be present first — the plan blocks until then.
		var blockedPlan = await Installer.PlanAsync(appPackage, answers);
		await Assert.That(blockedPlan.IsBlocked).IsTrue();

		await Assert.That((await Installer.ApplyAsync(routes, new PackageApplyRequest(Source(), new Dictionary<string, string>(), []))).IsT0).IsTrue();

		// Now the plan resolves and surfaces the registration as a note.
		var plan = await Installer.PlanAsync(appPackage, answers);
		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(plan.Objects.Count).IsEqualTo(0);
		await Assert.That(plan.Notes.Any(n => n.Contains("Registers application 'appdep'"))).IsTrue();

		// Apply registers the portal application with the configure-resolved role.
		var apply = await Installer.ApplyAsync(appPackage, new PackageApplyRequest(Source(), answers, []));
		await Assert.That(apply.IsT0).IsTrue();
		await Assert.That(apply.AsT0.CreatedObjects.Count).IsEqualTo(0);

		var registered = await Applications.GetApplicationAsync("appdep");
		await Assert.That(registered.IsT0).IsTrue();
		var app = registered.AsT0;
		await Assert.That(app.DisplayName).IsEqualTo("Appdep Application");
		await Assert.That(app.Kind).IsEqualTo(ApplicationKind.Page);
		await Assert.That(app.SchemaUrl).IsEqualTo("http/appdep/schema");
		await Assert.That(app.MinimumRole).IsEqualTo(PortalRole.Wizard);
		await Assert.That(app.OwningPackage).IsEqualTo("appdep-app");

		// The dependency cannot be removed while the application depends on it.
		var blockedUninstall = await Installer.UninstallAsync("appdep-routes");
		await Assert.That(blockedUninstall.IsT1).IsTrue();
		await Assert.That(blockedUninstall.AsT1.Value).Contains("appdep-app");

		// Uninstalling the application package reclaims the registration.
		await Assert.That((await Installer.UninstallAsync("appdep-app")).IsT0).IsTrue();
		await Assert.That((await Applications.GetApplicationAsync("appdep")).IsT1).IsTrue();
		await Assert.That((await Registry.GetInstalledPackageAsync("appdep-app")).IsT1).IsTrue();

		await Assert.That((await Installer.UninstallAsync("appdep-routes")).IsT0).IsTrue();
	}

	[Test, NotInParallel]
	public async Task Upgrade_AppliesAttributeFlags_AndRemovesDroppedAttribute()
	{
		// Two apply-side upgrade behaviours the end-to-end test above does not
		// cover: declared attribute flags are applied to the live attribute, and
		// an attribute dropped from the new version (locally untouched) is cleanly
		// deleted — value cleared AND its baseline record removed.
		var v1 = Parse(
			"""
			package: flags-pkg
			version: "1.0"
			objects:
			  - ref: widget
			    type: thing
			    name: Flags Widget
			    attributes:
			      FN_KEEP:
			        value: |-
			          keep-me
			        flags: [no_command, veiled]
			      FN_DROP: |-
			        drop-me-next-version
			""");

		var install = await Installer.ApplyAsync(v1, new PackageApplyRequest(Source(), new Dictionary<string, string>(), []));
		await Assert.That(install.IsT0).IsTrue();
		var widgetObjid = install.AsT0.CreatedObjects["widget"];

		// Attribute flags landed on the live attribute (applied additively at apply).
		var keepFlags = await ReadAttributeFlagsAsync(widgetObjid, "FN_KEEP");
		await Assert.That(keepFlags).Contains("no_command");
		await Assert.That(keepFlags).Contains("veiled");
		await Assert.That(await ReadAttributeAsync(widgetObjid, "FN_DROP")).IsEqualTo("drop-me-next-version");
		await Assert.That((await Registry.GetManagedAttributesAsync("flags-pkg")).Any(b => b.Attribute == "FN_DROP")).IsTrue();

		// Upgrade to v2: FN_DROP is gone from the manifest and was never modified
		// locally, so the plan classifies it as a clean Delete.
		var v2 = Parse(
			"""
			package: flags-pkg
			version: "1.1"
			objects:
			  - ref: widget
			    type: thing
			    name: Flags Widget
			    attributes:
			      FN_KEEP:
			        value: |-
			          keep-me
			        flags: [no_command, veiled]
			""");

		var plan = await Installer.PlanAsync(v2);
		await Assert.That(plan.Attributes.Single(a => a.Attribute == "FN_DROP").Action)
			.IsEqualTo(PackageAttributeAction.Delete);

		var upgrade = await Installer.ApplyAsync(v2, new PackageApplyRequest(Source("commit-2"), new Dictionary<string, string>(), []));
		await Assert.That(upgrade.IsT0).IsTrue();

		// FN_DROP is gone from both the live object and the baseline registry...
		await Assert.That(await ReadAttributeAsync(widgetObjid, "FN_DROP")).IsEqualTo("");
		await Assert.That((await Registry.GetManagedAttributesAsync("flags-pkg")).Any(b => b.Attribute == "FN_DROP")).IsFalse();
		// ...while FN_KEEP and its flags survive the upgrade untouched.
		await Assert.That(await ReadAttributeAsync(widgetObjid, "FN_KEEP")).IsEqualTo("keep-me");
		await Assert.That(await ReadAttributeFlagsAsync(widgetObjid, "FN_KEEP")).Contains("veiled");

		await Assert.That((await Installer.UninstallAsync("flags-pkg")).IsT0).IsTrue();
	}

	private async Task<IReadOnlyList<string>> ReadObjectFlagsAsync(string objid)
	{
		var node = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(objid)!.Value);
		var flags = new List<string>();
		await foreach (var flag in node.Known().Object().Flags.Value)
		{
			flags.Add(flag.Name);
		}

		return flags;
	}

	private async Task<bool> HasLockAsync(string objid, string lockType)
	{
		var node = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(objid)!.Value);
		return node.Known().Object().Locks.ContainsKey(lockType);
	}

	[Test, NotInParallel]
	public async Task Upgrade_AddsAndRemovesObjectFlagsAndLocks()
	{
		// v1 sets two flags and a lock; v2 drops one flag and the lock entirely.
		// Full object-structure diff: the dropped flag is unset and the lock removed,
		// while the retained flag survives — never additive.
		var v1 = Parse(
			"""
			package: struct-pkg
			version: "1.0"
			objects:
			  - ref: gadget
			    type: thing
			    name: Struct Gadget
			    flags: [dark, opaque]
			    locks:
			      use: "=#1"
			    attributes:
			      FN: |-
			        x
			""");

		var install = await Installer.ApplyAsync(v1, new PackageApplyRequest(Source(), new Dictionary<string, string>(), []));
		await Assert.That(install.IsT0).IsTrue();
		var gadgetObjid = install.AsT0.CreatedObjects["gadget"];

		var flagsV1 = await ReadObjectFlagsAsync(gadgetObjid);
		await Assert.That(flagsV1).Contains("DARK");
		await Assert.That(flagsV1).Contains("OPAQUE");
		await Assert.That(await HasLockAsync(gadgetObjid, "use")).IsTrue();

		var v2 = Parse(
			"""
			package: struct-pkg
			version: "1.1"
			objects:
			  - ref: gadget
			    type: thing
			    name: Struct Gadget
			    flags: [dark]
			    attributes:
			      FN: |-
			        x
			""");

		var plan = await Installer.PlanAsync(v2);
		await Assert.That(plan.Structure.Single(s =>
				s.Kind == PackageStructureKind.ObjectFlag && string.Equals(s.Element, "opaque", StringComparison.OrdinalIgnoreCase))
			.Action).IsEqualTo(PackageStructureAction.Remove);
		await Assert.That(plan.Structure.Single(s => s.Kind == PackageStructureKind.Lock).Action)
			.IsEqualTo(PackageStructureAction.Remove);

		var upgrade = await Installer.ApplyAsync(v2, new PackageApplyRequest(Source("commit-2"), new Dictionary<string, string>(), []));
		await Assert.That(upgrade.IsT0).IsTrue();

		var flagsV2 = await ReadObjectFlagsAsync(gadgetObjid);
		await Assert.That(flagsV2).Contains("DARK");
		await Assert.That(flagsV2).DoesNotContain("OPAQUE");
		await Assert.That(await HasLockAsync(gadgetObjid, "use")).IsFalse();

		await Assert.That((await Installer.UninstallAsync("struct-pkg")).IsT0).IsTrue();
	}

	// ── Phase 4: managed (compiled C# plugin DLL) packages ───────────────────

	private static string CommandOnlyDllPath =>
		System.IO.Path.Combine(AppContext.BaseDirectory, "plugins-unit", "command-only", "CommandOnlyPlugin.dll");

	private sealed class DirectoryBinarySource(string directory)
		: SharpMUSH.Library.Services.Interfaces.IManagedPackageBinarySource
	{
		public async Task<byte[]?> ReadBinaryAsync(string fileName, CancellationToken cancellationToken = default)
		{
			var path = System.IO.Path.Combine(directory, fileName);
			return System.IO.File.Exists(path) ? await System.IO.File.ReadAllBytesAsync(path, cancellationToken) : null;
		}
	}

	/// <summary>
	/// End-to-end through the real DB registry: a managed package deposits its
	/// carried DLL into a scratch plugins root, the install records the deployed
	/// file list on the installed-package record (proving the registry-record
	/// extension threads through the active provider), and uninstall removes the
	/// directory. Uses a dedicated installer pointed at a scratch root + allow-all
	/// trust so the real plugins/ folder is never touched.
	/// </summary>
	[Test, NotInParallel]
	public async Task ManagedPackage_InstallRecordsDeployedFiles_UninstallRemovesThem()
	{
		await Assert.That(System.IO.File.Exists(CommandOnlyDllPath)).IsTrue()
			.Because("the CommandOnlyPlugin fixture DLL is reused as the carried managed binary");

		var sourceDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mpkg-src-{Guid.NewGuid():N}");
		var pluginsRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mpkg-plugins-{Guid.NewGuid():N}");
		System.IO.Directory.CreateDirectory(sourceDir);
		System.IO.File.Copy(CommandOnlyDllPath, System.IO.Path.Combine(sourceDir, "CommandOnlyPlugin.dll"));
		var sha = Convert.ToHexString(
			System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(CommandOnlyDllPath))).ToLowerInvariant();

		try
		{
			var manifest = Parse($"""
				package: e2e-managed
				version: "1.0.0"
				kind: managed
				binaries:
				  min_server_version: ">=1.0"
				  files:
				    - file: CommandOnlyPlugin.dll
				      sha256: {sha}
				""");

			var pluginManager = WebAppFactoryArg.Services.GetRequiredService<IPluginManager>();
			var managedInstaller = new SharpMUSH.Library.Services.ManagedPackageInstaller(
				pluginManager,
				new SharpMUSH.Library.Services.ManagedPackageTrustOptions(false, ["e2e-managed"]),
				Microsoft.Extensions.Logging.Abstractions.NullLogger<SharpMUSH.Library.Services.ManagedPackageInstaller>.Instance,
				pluginsRoot);

			var installer = new PackageInstallService(
				Database,
				Registry,
				Applications,
				WebAppFactoryArg.Services.GetRequiredService<IPackagePlanService>(),
				WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>(),
				WebAppFactoryArg.Services.GetRequiredService<IPackageLifecycleRunner>(),
				managedInstaller);

			// Refused without the per-apply opt-in, and nothing deposited.
			var refused = await installer.ApplyAsync(
				manifest,
				new PackageApplyRequest(Source(), new Dictionary<string, string>(), [], 10, AllowManagedCode: false),
				CancellationToken.None,
				new DirectoryBinarySource(sourceDir));
			await Assert.That(refused.IsT1).IsTrue().Because("a managed install without the opt-in must be refused");
			await Assert.That((await Registry.GetInstalledPackageAsync("e2e-managed")).IsT1).IsTrue()
				.Because("a refused managed install records nothing");

			// Opt-in: deposit + record.
			var applied = await installer.ApplyAsync(
				manifest,
				new PackageApplyRequest(Source(), new Dictionary<string, string>(), [], 10, AllowManagedCode: true),
				CancellationToken.None,
				new DirectoryBinarySource(sourceDir));
			await Assert.That(applied.IsT0).IsTrue().Because("the opt-in + allow-list + matching hash should install");

			var depositedDll = System.IO.Path.Combine(pluginsRoot, "e2e-managed", "CommandOnlyPlugin.dll");
			await Assert.That(System.IO.File.Exists(depositedDll)).IsTrue();

			var record = await Registry.GetInstalledPackageAsync("e2e-managed");
			await Assert.That(record.IsT0).IsTrue();
			await Assert.That(record.AsT0.DeployedFiles).IsNotNull();
			await Assert.That(record.AsT0.DeployedFiles!).Contains("CommandOnlyPlugin.dll")
				.Because("the deployed file list must round-trip through the active DB provider");

			// Uninstall removes the directory and the registry record.
			var uninstalled = await installer.UninstallAsync("e2e-managed");
			await Assert.That(uninstalled.IsT0).IsTrue();
			await Assert.That(System.IO.Directory.Exists(System.IO.Path.Combine(pluginsRoot, "e2e-managed"))).IsFalse();
			await Assert.That((await Registry.GetInstalledPackageAsync("e2e-managed")).IsT1).IsTrue();
		}
		finally
		{
			System.IO.Directory.Delete(sourceDir, true);
			if (System.IO.Directory.Exists(pluginsRoot)) System.IO.Directory.Delete(pluginsRoot, true);
		}
	}
}
