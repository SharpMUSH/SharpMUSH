using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Queries.Database;
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
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
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

	private PackageManifest Parse(string yaml) => Manifests.ParseManifest(yaml) switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	private async Task<string> ReadAttributeAsync(string objid, string attribute)
	{
		var dbref = PackageInstallService.ParseObjid(objid)!.Value;
		var leaf = await Database.GetAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Value.ToPlainText() ?? "";
	}

	private async Task<string> EvaluateAttributeAsync(string objid, string attribute)
	{
		var result = await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"[u({objid}/{attribute})]"));
		return result!.Message!.ToPlainText();
	}

	private async Task<IReadOnlyList<string>> ReadAttributeFlagsAsync(string objid, string attribute)
	{
		var dbref = PackageInstallService.ParseObjid(objid)!.Value;
		var leaf = await Database.GetAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Flags.Select(f => f.Name).ToList() ?? [];
	}

	[Test, NotInParallel]
	public async Task WellKnownRefs_InstallAndUpgradeWithExistingRefTree()
	{
		var manifest = Parse("""
			package: well-known-refs
			version: "1.0"
			objects:
			  - ref: core
			    type: thing
			    name: Reference Core
			    attributes:
			      FN_ZERO: "{{$room_zero}}"
			""");
		var answers = new Dictionary<string, string>();
		var installed = await Installer.ApplyAsync(manifest, new PackageApplyRequest(Source(), answers, []));
		var applied = installed.Expect<PackageApplyResult>();
		var objid = applied.CreatedObjects["core"];
		var upgraded = Parse("""
			package: well-known-refs
			version: "1.1"
			objects:
			  - ref: core
			    type: thing
			    name: Reference Core
			    attributes:
			      FN_ZERO: "{{$room_zero}}"
			      FN_PM: "{{$package_manager}}"
			      FN_GOD: "{{$god}}"
			      FN_START: "{{$player_start}}"
			      FN_MASTER: "{{$master_room}}"
			""");
		var plan = await Installer.PlanAsync(upgraded, answers);
		await Assert.That(plan.HasConflicts).IsFalse();
		await Assert.That((await Installer.ApplyAsync(upgraded, new PackageApplyRequest(Source("commit-2"), answers, []))).Value).IsTypeOf<PackageApplyResult>();
		foreach (var (name, number) in new[] { ("ROOM_ZERO", 0), ("PACKAGE_MANAGER", 7), ("GOD", 1), ("PLAYER_START", 0), ("MASTER_ROOM", 2) })
		{
			var expected = (await Database.GetObjectNodeAsync(new DBRef(number))).Expect<AnySharpObject>().Object().DBRef.ToString();
			await Assert.That(await ReadAttributeAsync(objid, $"PM`REFS`{name}")).IsEqualTo(expected);
		}
		await Assert.That((await Installer.PlanAsync(upgraded, answers)).Attributes.All(a => a.Action == PackageAttributeAction.NoChange)).IsTrue();
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		await Database.SetAttributeAsync(PackageInstallService.ParseObjid(objid)!.Value,
			["PM", "REFS", "PACKAGE_MANAGER"], MarkupText.Empty, pm);
		var beforeRepair = await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"[u({objid}/FN_PM)]"));
		await Assert.That(beforeRepair!.Message!.ToPlainText()).IsEqualTo("");
		var repair = await Installer.PlanAsync(upgraded, answers);
		await Assert.That(repair.Attributes.Single(a => a.Attribute == "PM`REFS`PACKAGE_MANAGER").Action)
			.IsEqualTo(PackageAttributeAction.AutoUpgrade);
		await Assert.That((await Installer.ApplyAsync(upgraded, new PackageApplyRequest(Source("commit-3"), answers, []))).Value).IsTypeOf<PackageApplyResult>();
		var expectedPm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<AnySharpObject>().Object().DBRef.ToString();
		await Assert.That(await ReadAttributeAsync(objid, "PM`REFS`PACKAGE_MANAGER")).IsEqualTo(expectedPm);
		var evaluated = await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"[u({objid}/FN_PM)]"));
		await Assert.That(evaluated!.Message!.ToPlainText()).IsEqualTo(expectedPm);
		await Assert.That((await Installer.UninstallAsync(manifest.Name)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task LegacyAttachedRefUpgrade_MigratesLocalValueAndRetainsSharedPath()
	{
		var manifest = Parse("""
			package: legacy-ref-probe
			version: "1.0"
			objects:
			  - ref: registration
			    target: "{{$room_zero}}"
			    attributes:
			      LEGACY_SRC: "{{$god}}"
			""");
		var answers = new Dictionary<string, string>();
		await Assert.That((await Installer.ApplyAsync(manifest, new PackageApplyRequest(Source(), answers, []))).Value).IsTypeOf<PackageApplyResult>();
		var host = (await Database.GetObjectNodeAsync(new DBRef(0))).Expect<AnySharpObject>().Object().DBRef.ToString();
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().DBRef.ToString();
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		var custom = pm.Object.DBRef.ToString();
		const string isolated = "PM`ATTACHED_REFS`LEGACY-REF-PROBE`GOD";
		// Seed the state left by the old installer, including a local re-point.
		await Database.ClearAttributeAsync(new DBRef(0), isolated.Split('`'));
		await Registry.RemoveManagedAttributeAsync(manifest.Name, host, isolated);
		await Database.SetAttributeAsync(new DBRef(0), ["PM", "REFS", "GOD"], MarkupText.Plain(custom), pm);
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(manifest.Name, host, "PM`REFS`GOD", god, "h", "1.0"));
		await Database.SetAttributeAsync(new DBRef(0), ["LEGACY_SRC"], MarkupText.Plain("[v(PM`REFS`GOD)]"), pm);
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(manifest.Name, host, "LEGACY_SRC", "[v(PM`REFS`GOD)]", "h", "1.0"));

		var plan = await Installer.PlanAsync(manifest, answers);
		await Assert.That(plan.HasConflicts).IsTrue();
		await Assert.That(plan.Attributes.Single(a => a.Attribute == isolated).PreviousAttribute).IsEqualTo("PM`REFS`GOD");
		await Assert.That((await Installer.ApplyAsync(manifest, new PackageApplyRequest(Source("commit-2"), answers,
			[new PackageConflictDecision("registration", isolated, PackageConflictResolution.KeepMine)]))).Value).IsTypeOf<PackageApplyResult>();
		await Assert.That(await ReadAttributeAsync(host, isolated)).IsEqualTo(custom);
		await Assert.That(await ReadAttributeAsync(host, "PM`REFS`GOD")).IsEqualTo(custom);
		var evaluated = await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"[u({host}/LEGACY_SRC)]"));
		await Assert.That(evaluated!.Message!.ToPlainText()).IsEqualTo(custom);
		await Assert.That((await Installer.PlanAsync(manifest, answers)).HasConflicts).IsFalse();
		await Assert.That((await Installer.UninstallAsync(manifest.Name)).Value).IsTypeOf<Success>();
		await Assert.That(await ReadAttributeAsync(host, "PM`REFS`GOD")).IsEqualTo(custom);
		await Database.ClearAttributeAsync(new DBRef(0), ["PM", "REFS", "GOD"]);
	}

	[Test, NotInParallel]
	public async Task UninstallLegacyAttacher_PreservesRefsStillManagedByAnotherPackage()
	{
		PackageManifest Consumer(string name) => Parse($$$"""
			package: {{{name}}}
			version: "1.0"
			objects:
			  - ref: registration
			    target: "{{$room_zero}}"
			    attributes:
			      {{{name}}}: "source"
			""");
		var first = Consumer("legacy-uninstall-a");
		var second = Consumer("legacy-uninstall-b");
		var answers = new Dictionary<string, string>();
		await Assert.That((await Installer.ApplyAsync(first, new PackageApplyRequest(Source(), answers, []))).Value).IsTypeOf<PackageApplyResult>();
		await Assert.That((await Installer.ApplyAsync(second, new PackageApplyRequest(Source(), answers, []))).Value).IsTypeOf<PackageApplyResult>();
		var host = (await Database.GetObjectNodeAsync(new DBRef(0))).Expect<AnySharpObject>().Object().DBRef.ToString();
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		var value = pm.Object.DBRef.ToString();
		await Database.SetAttributeAsync(new DBRef(0), ["PM", "REFS", "SHARED"], MarkupText.Plain(value), pm);
		foreach (var package in new[] { first.Name, second.Name })
		{
			await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(package, host, "PM`REFS`SHARED", value, "h", "1.0"));
		}
		await Assert.That((await Installer.UninstallAsync(first.Name)).Value).IsTypeOf<Success>();
		await Assert.That(await ReadAttributeAsync(host, "PM`REFS`SHARED")).IsEqualTo(value);
		await Assert.That((await Installer.UninstallAsync(second.Name)).Value).IsTypeOf<Success>();
		await Assert.That(await ReadAttributeAsync(host, "PM`REFS`SHARED")).IsEqualTo("");
	}

	[Test, NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LegacyRollback_ProtectsOtherPackagesBeforeAnyWrites(bool conflictingValue)
	{
		const string package = "legacy-rollback-a";
		const string other = "legacy-rollback-b";
		const string restoredRef = "PM`REFS`ROLLBACK_RESTORE";
		const string removedRef = "PM`REFS`ROLLBACK_REMOVE";
		var host = (await Database.GetObjectNodeAsync(new DBRef(0))).Expect<AnySharpObject>().Object().DBRef.ToString();
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().DBRef.ToString();
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		var liveRef = conflictingValue ? pm.Object.DBRef.ToString() : god;
		foreach (var id in new[] { package, other })
		{
			await Registry.UpsertInstalledPackageAsync(new InstalledPackageRecord(id, "1.1.0",
				Source().Repo, Source().Path, "current", "main", DateTimeOffset.UtcNow, 2));
			await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(id, host, removedRef, god, "h", "1.1.0"));
		}
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(other, host, restoredRef, liveRef, "h", "1.1.0"));
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(package, host, "ROLLBACK_CODE", "current", "h", "1.1.0"));
		await Database.SetAttributeAsync(new DBRef(0), ["ROLLBACK_CODE"], MarkupText.Plain("current"), pm);
		await Database.SetAttributeAsync(new DBRef(0), restoredRef.Split('`'), MarkupText.Plain(liveRef), pm);
		await Database.SetAttributeAsync(new DBRef(0), removedRef.Split('`'), MarkupText.Plain(god), pm);
		var snapshot = new PackageRevisionSnapshot("1.0.0", [],
		[
			new PackageRevisionSnapshotAttribute(host, "ROLLBACK_CODE", "old"),
			new PackageRevisionSnapshotAttribute(host, restoredRef, god)
		]);
		await Registry.AddPackageRevisionAsync(new PackageRevisionRecord(package, 1, PackageRevisionKind.Install,
			"1.0.0", "old", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "{}", "[]", DateTimeOffset.UtcNow));

		var rolledBack = await Installer.RollbackAsync(package, 1);
		if (conflictingValue)
		{
			var refusal = rolledBack.Expect<Error<string>>();
			await Assert.That(refusal.Value).Contains("shared");
			await Assert.That(await ReadAttributeAsync(host, "ROLLBACK_CODE")).IsEqualTo("current");
			await Assert.That(await ReadAttributeAsync(host, restoredRef)).IsEqualTo(liveRef);
			var untouched = (await Registry.GetInstalledPackageAsync(package)).Expect<InstalledPackageRecord>();
			await Assert.That(untouched.CurrentRevision).IsEqualTo(2);
			await Registry.RemoveManagedAttributeAsync(other, host, restoredRef);
			rolledBack = await Installer.RollbackAsync(package, 1);
		}
		await Assert.That(rolledBack.Value).IsTypeOf<PackageRollbackResult>();
		await Assert.That(await ReadAttributeAsync(host, "ROLLBACK_CODE")).IsEqualTo("old");
		await Assert.That(await ReadAttributeAsync(host, restoredRef)).IsEqualTo(god);
		await Assert.That(await ReadAttributeAsync(host, removedRef)).IsEqualTo(god);
		await Assert.That((await Registry.GetManagedAttributesAsync(package)).Any(a => a.Attribute == removedRef)).IsFalse();
		await Assert.That((await Installer.UninstallAsync(package)).Value).IsTypeOf<Success>();
		await Assert.That((await Installer.UninstallAsync(other)).Value).IsTypeOf<Success>();
	}

	/// <summary>
	/// A revision written before lock names were canonical names <see cref="LockType.Teleport"/> the way
	/// that world spelled it — <c>tport</c>, which is why <see cref="LockNames"/> still carries it as
	/// an alias. Rolling back to it must leave the lock in place: the raw <c>tport</c> add and the
	/// folded baseline's <c>Teleport</c> removal both canonicalise in the provider, and the removal is
	/// applied last, so the rollback used to delete the very lock the revision asked it to keep — and
	/// an absent lock reads as "no lock", which passes everybody.
	/// </summary>
	[Test, NotInParallel]
	public async Task LegacyRollback_KeepsALockTheOldWorldSpelledDifferently()
	{
		const string package = "legacy-lock-rollback";
		var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		var store = WebAppFactoryArg.Services.GetRequiredService<IObjectStore>();
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var player = (await store.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var target = await mediator.Send(new CreateRoomCommand("legacy-lock-rollback-" + Guid.NewGuid().ToString("N"), player));
		var node = (await store.GetObjectNodeAsync(target)).Expect<AnySharpObject>();
		var objid = node.Object().DBRef.ToString();
		await store.SetLockAsync(node.Object(), nameof(LockType.Teleport), new SharpLockData("=#1"));

		await Registry.UpsertInstalledPackageAsync(new InstalledPackageRecord(package, "1.1.0",
			Source().Repo, Source().Path, "current", "main", DateTimeOffset.UtcNow, 2));
		// What the package holds now: canonical, because every baseline is folded when it is read.
		await Registry.UpsertManagedStructureAsync(new ManagedStructureRecord(package, objid,
			JsonSerializer.Serialize(new PackageStructureBaseline([], [],
				new Dictionary<string, string> { [nameof(LockType.Teleport)] = "=#1" },
				new Dictionary<string, IReadOnlyList<string>>()), json), "1.1.0"));
		// What the revision holds: the pre-canonicalisation spelling of the same lock.
		var snapshot = new PackageRevisionSnapshot("1.0.0", [], [],
		[
			new PackageRevisionSnapshotStructure(objid, [], [],
				new Dictionary<string, string> { ["tport"] = "=#1" },
				new Dictionary<string, IReadOnlyList<string>>())
		]);
		await Registry.AddPackageRevisionAsync(new PackageRevisionRecord(package, 1, PackageRevisionKind.Install,
			"1.0.0", "old", JsonSerializer.Serialize(snapshot, json), "{}", "[]", DateTimeOffset.UtcNow));

		var rolledBack = await Installer.RollbackAsync(package, 1);

		await Assert.That(rolledBack.Value).IsTypeOf<PackageRollbackResult>();
		var locks = (await store.GetObjectNodeAsync(target)).Expect<AnySharpObject>().Object().Locks;
		await Assert.That(locks.ContainsKey(nameof(LockType.Teleport))).IsTrue()
			.Because("rolling back to a revision that carries the lock must not remove it");
		await Assert.That(locks[nameof(LockType.Teleport)].LockString).IsEqualTo("=#1");
		// And the baseline it persists is canonical, so the next rollback does not repeat the round trip.
		var persisted = JsonSerializer.Deserialize<PackageStructureBaseline>(
			(await Registry.GetManagedStructuresAsync(package)).Single(s => s.Objid == objid).StructureJson, json);
		await Assert.That(persisted!.Locks.Keys.Single()).IsEqualTo(nameof(LockType.Teleport));
		await Assert.That((await Installer.UninstallAsync(package)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task AttachedRefs_AreIsolatedAcrossPackagesAndUninstall()
	{
		PackageManifest Consumer(string name) => Parse($$$"""
			package: {{{name}}}
			version: "1.0"
			objects:
			  - ref: help
			    type: thing
			    name: Reference Help
			  - ref: registration
			    target: "{{$room_zero}}"
			    attributes:
			      SRC`{{{name}}}: "{{help}}"
			""");
		var answers = new Dictionary<string, string>();
		var first = Consumer("ref-consumer-a");
		var second = Consumer("ref-consumer-b");
		var a = await Installer.ApplyAsync(first, new PackageApplyRequest(Source(), answers, []));
		var appliedA = a.Expect<PackageApplyResult>();
		var b = await Installer.ApplyAsync(second, new PackageApplyRequest(Source(), answers, []));
		var appliedB = b.Expect<PackageApplyResult>();
		var host = (await Database.GetObjectNodeAsync(new DBRef(0))).Expect<AnySharpObject>().Object().DBRef.ToString();
		await Assert.That(await ReadAttributeAsync(host, "SRC`REF-CONSUMER-A")).IsEqualTo("[v(PM`ATTACHED_REFS`REF-CONSUMER-A`HELP)]");
		await Assert.That(await ReadAttributeAsync(host, "PM`ATTACHED_REFS`REF-CONSUMER-A`HELP")).IsEqualTo(appliedA.CreatedObjects["help"]);
		await Assert.That(await ReadAttributeAsync(host, "PM`ATTACHED_REFS`REF-CONSUMER-B`HELP")).IsEqualTo(appliedB.CreatedObjects["help"]);
		await Assert.That((await Installer.PlanAsync(first, answers)).Attributes.All(x => x.Action == PackageAttributeAction.NoChange)).IsTrue();
		await Assert.That(await EvaluateAttributeAsync(host, "SRC`REF-CONSUMER-A")).IsEqualTo(appliedA.CreatedObjects["help"]);
		await Assert.That(await EvaluateAttributeAsync(host, "SRC`REF-CONSUMER-B")).IsEqualTo(appliedB.CreatedObjects["help"]);
		await Assert.That((await Installer.UninstallAsync(first.Name)).Value).IsTypeOf<Success>();
		await Assert.That(await EvaluateAttributeAsync(host, "SRC`REF-CONSUMER-A")).IsEqualTo("");
		await Assert.That(await EvaluateAttributeAsync(host, "SRC`REF-CONSUMER-B")).IsEqualTo(appliedB.CreatedObjects["help"]);
		await Assert.That(await ReadAttributeAsync(host, "PM`ATTACHED_REFS`REF-CONSUMER-B`HELP")).IsEqualTo(appliedB.CreatedObjects["help"]);
		await Assert.That((await Installer.UninstallAsync(second.Name)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task InstallUpgradeRollbackUninstall_EndToEnd()
	{
		var roomZero = (await Database.GetObjectNodeAsync(new DBRef(0))).Expect<AnySharpObject>().Object().DBRef.ToString();
		var answers = new Dictionary<string, string> { ["storage"] = roomZero };
		var manifestV1 = Parse(ManifestV1);

		var plan = await Installer.PlanAsync(manifestV1, answers);
		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(plan.Objects.Count(o => o.Action == PackageObjectAction.Create)).IsEqualTo(3);

		var install = await Installer.ApplyAsync(manifestV1, new PackageApplyRequest(Source(), answers, []));
		var result = install.Expect<PackageApplyResult>();
		await Assert.That(result.Revision).IsEqualTo(1);
		await Assert.That(result.CreatedObjects.Keys.Order().ToArray())
			.IsEquivalentTo((string[])["board", "lounge", "lounge_out"]);

		var boardObjid = result.CreatedObjects["board"];
		var loungeObjid = result.CreatedObjects["lounge"];

		var boardNode = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(boardObjid)!.Value);
		await Assert.That(boardNode.IsNone).IsFalse();
		await Assert.That(boardNode.Expect<AnySharpObject>().Object().Name).IsEqualTo("E2E Board");

		// Code carries v(PM`REFS`...) recalls, never dbrefs (decision 20.21).
		var cmd = await ReadAttributeAsync(boardObjid, "CMD_+E2E");
		await Assert.That(cmd).Contains("[v(PM`REFS`BOARD)]");
		await Assert.That(cmd).Contains("[v(PM`REFS`STORAGE)]");
		await Assert.That(cmd).DoesNotContain("{{");
		await Assert.That(cmd).DoesNotContain(boardObjid);
		await Assert.That(await ReadAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-one-format");
		await Assert.That(await EvaluateAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-one-format");

		await Assert.That(await ReadAttributeAsync(boardObjid, "PM`REFS`BOARD")).IsEqualTo(boardObjid);
		await Assert.That(await ReadAttributeAsync(boardObjid, "PM`REFS`STORAGE")).IsEqualTo(roomZero);

		var installedRecord = (await Registry.GetInstalledPackageAsync("e2e-pkg")).Expect<InstalledPackageRecord>();
		await Assert.That(installedRecord.Version).IsEqualTo("1.0.0");
		await Assert.That((await Registry.GetPackageObjectsAsync("e2e-pkg")).Count).IsEqualTo(3);
		var baselines = await Registry.GetManagedAttributesAsync("e2e-pkg");
		await Assert.That(baselines.Single(b => b.Attribute == "FN_FMT").BaselineValue).IsEqualTo("version-one-format");
		await Assert.That(baselines.Single(b => b.Attribute == "PM`REFS`BOARD").BaselineValue).IsEqualTo(boardObjid);
		await Assert.That((await Registry.GetPackageRevisionsAsync("e2e-pkg")).Single().Kind)
			.IsEqualTo(PackageRevisionKind.Install);

		var idle = await Installer.PlanAsync(manifestV1, answers);
		await Assert.That(idle.Attributes.All(a => a.Action == PackageAttributeAction.NoChange)).IsTrue();
		await Assert.That(idle.HasConflicts).IsFalse();

		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		await Database.SetAttributeAsync(
			PackageInstallService.ParseObjid(boardObjid)!.Value, ["FN_FMT"],
			MarkupText.Plain("my-custom-format"), pm);

		var manifestV2 = Parse(ManifestV2);
		var upgradePlan = await Installer.PlanAsync(manifestV2, answers);
		var conflict = upgradePlan.Attributes.Single(a => a.Action == PackageAttributeAction.Conflict);
		await Assert.That(conflict.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
		await Assert.That(conflict.BaseValue).IsEqualTo("version-one-format");
		await Assert.That(conflict.LiveValue).IsEqualTo("my-custom-format");
		await Assert.That(conflict.NewValue).IsEqualTo("version-two-format");

		var undecided = (await Installer.ApplyAsync(manifestV2, new PackageApplyRequest(Source("commit-2"), answers, []))).Expect<Error<string>>();
		await Assert.That(undecided.Value).Contains("Unresolved conflicts");

		var upgrade = await Installer.ApplyAsync(manifestV2, new PackageApplyRequest(
			Source("commit-2"), answers,
			[new PackageConflictDecision(conflict.TargetRef, conflict.Attribute, PackageConflictResolution.TakeTheirs)]));
		var upgraded = upgrade.Expect<PackageApplyResult>();
		await Assert.That(upgraded.Revision).IsEqualTo(2);
		await Assert.That(upgraded.CreatedObjects.Count).IsEqualTo(0);
		await Assert.That(await ReadAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-two-format");
		await Assert.That(await EvaluateAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-two-format");
		var upgradedRecord = (await Registry.GetInstalledPackageAsync("e2e-pkg")).Expect<InstalledPackageRecord>();
		await Assert.That(upgradedRecord.Version).IsEqualTo("1.1.0");

		var rollback = await Installer.RollbackAsync("e2e-pkg", 1);
		var rolledBack = rollback.Expect<PackageRollbackResult>();
		await Assert.That(rolledBack.Revision).IsEqualTo(3);
		await Assert.That(rolledBack.RestoredFromRevision).IsEqualTo(1);
		await Assert.That(await ReadAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-one-format");
		await Assert.That(await EvaluateAttributeAsync(boardObjid, "FN_FMT")).IsEqualTo("version-one-format");
		var afterRollback = (await Registry.GetInstalledPackageAsync("e2e-pkg")).Expect<InstalledPackageRecord>();
		await Assert.That(afterRollback.Version).IsEqualTo("1.0.0");
		await Assert.That(afterRollback.CurrentRevision).IsEqualTo(3);

		var uninstall = await Installer.UninstallAsync("e2e-pkg");
		await Assert.That(uninstall.Value).IsTypeOf<Success>();
		await Assert.That((await Registry.GetInstalledPackageAsync("e2e-pkg")).Value).IsTypeOf<NotFound>();
		await Assert.That((await Registry.GetPackageObjectsAsync("e2e-pkg")).Count).IsEqualTo(0);

		// Created objects are marked GOING (the @destroy convention), not hard-deleted.
		var goneBoard = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(boardObjid)!.Value);
		await Assert.That(goneBoard.IsNone).IsFalse();

		// Keep the lounge objid referenced so the variable is used even if asserts change.
		await Assert.That(loungeObjid).IsNotNull();
	}

	[Test, NotInParallel]
	public async Task AttachObject_ManagesAttrsOnExistingObject_UninstallLeavesObject()
	{
		// Attach mode (decision 20.3): a package that manages attributes on an
		// object it does not own. Uses a {{?configure}} target so it's isolated
		// from the shared http_handler. Mirrors how http-hooks attaches to #4.
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<AnySharpObject>();
		var pm = pmNode.Expect<SharpPlayer>();
		var location = pmNode.AsContainer;

		// A pre-existing object the package will attach to (not created by it).
		var hostDbref = await Database.CreateThingAsync("Attach Host", location, pm, location);
		await Database.SetAttributeAsync(hostDbref, ["PRE_EXISTING"], MarkupText.Plain("untouched"), pm);
		var hostObjid = (await Database.GetObjectNodeAsync(hostDbref)).Expect<AnySharpObject>().Object().DBRef.ToString();

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
		var applied = install.Expect<PackageApplyResult>();
		await Assert.That(applied.CreatedObjects.Count).IsEqualTo(0);

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
		await Assert.That((await Installer.UninstallAsync("attach-pkg")).Value).IsTypeOf<Success>();
		await Assert.That(await ReadAttributeAsync(hostObjid, "CMD_X")).IsEqualTo("");
		await Assert.That(await ReadAttributeAsync(hostObjid, "PRE_EXISTING")).IsEqualTo("untouched");
		var hostStillThere = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(hostObjid)!.Value);
		await Assert.That(hostStillThere.IsNone).IsFalse();
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
		var providerApplied = providerResult.Expect<PackageApplyResult>();
		var hubObjid = providerApplied.CreatedObjects["hub"];

		// The attacher manages an attribute on the provider's object (cross-package attach).
		await Assert.That((await Installer.ApplyAsync(attacher, new PackageApplyRequest(Source(), answers, []))).Value).IsTypeOf<PackageApplyResult>();
		await Assert.That(await ReadAttributeAsync(hubObjid, "FN_EXT")).IsEqualTo("extension");
		await Assert.That((await Registry.GetPackageObjectsAsync("attach-consumer")).Count).IsEqualTo(0);

		// Uninstalling the provider is blocked while the attacher is present —
		// both by the dependency and by the attachment guard.
		var blocked = (await Installer.UninstallAsync("attach-provider")).Expect<Error<string>>();
		await Assert.That(blocked.Value).Contains("attach-consumer");

		await Assert.That((await Installer.UninstallAsync("attach-consumer")).Value).IsTypeOf<Success>();
		await Assert.That(await ReadAttributeAsync(hubObjid, "FN_EXT")).IsEqualTo("");
		await Assert.That((await Installer.UninstallAsync("attach-provider")).Value).IsTypeOf<Success>();
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
		await Assert.That((await Installer.ApplyAsync(basePkg, new PackageApplyRequest(Source(), answers, []))).Value).IsTypeOf<PackageApplyResult>();
		await Assert.That((await Installer.ApplyAsync(dependent, new PackageApplyRequest(Source(), answers, []))).Value).IsTypeOf<PackageApplyResult>();

		var blocked = (await Installer.UninstallAsync("e2e-base")).Expect<Error<string>>();
		await Assert.That(blocked.Value).Contains("e2e-child");

		await Assert.That((await Installer.UninstallAsync("e2e-child")).Value).IsTypeOf<Success>();
		await Assert.That((await Installer.UninstallAsync("e2e-base")).Value).IsTypeOf<Success>();
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

		await Assert.That((await Installer.ApplyAsync(routes, new PackageApplyRequest(Source(), new Dictionary<string, string>(), []))).Value).IsTypeOf<PackageApplyResult>();

		var plan = await Installer.PlanAsync(appPackage, answers);
		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(plan.Objects.Count).IsEqualTo(0);
		await Assert.That(plan.Notes.Any(n => n.Contains("Registers application 'appdep'"))).IsTrue();

		var apply = await Installer.ApplyAsync(appPackage, new PackageApplyRequest(Source(), answers, []));
		var applied = apply.Expect<PackageApplyResult>();
		await Assert.That(applied.CreatedObjects.Count).IsEqualTo(0);

		var registered = await Applications.GetApplicationAsync("appdep");
		var app = registered.Expect<RegisteredApplication>();
		await Assert.That(app.DisplayName).IsEqualTo("Appdep Application");
		await Assert.That(app.Kind).IsEqualTo(ApplicationKind.Page);
		await Assert.That(app.SchemaUrl).IsEqualTo("http/appdep/schema");
		await Assert.That(app.MinimumRole).IsEqualTo(PortalRole.Wizard);
		await Assert.That(app.OwningPackage).IsEqualTo("appdep-app");

		// The dependency cannot be removed while the application depends on it.
		var blockedUninstall = (await Installer.UninstallAsync("appdep-routes")).Expect<Error<string>>();
		await Assert.That(blockedUninstall.Value).Contains("appdep-app");

		await Assert.That((await Installer.UninstallAsync("appdep-app")).Value).IsTypeOf<Success>();
		await Assert.That((await Applications.GetApplicationAsync("appdep")).Value).IsTypeOf<NotFound>();
		await Assert.That((await Registry.GetInstalledPackageAsync("appdep-app")).Value).IsTypeOf<NotFound>();

		await Assert.That((await Installer.UninstallAsync("appdep-routes")).Value).IsTypeOf<Success>();
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
		var applied = install.Expect<PackageApplyResult>();
		var widgetObjid = applied.CreatedObjects["widget"];

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
		await Assert.That(upgrade.Value).IsTypeOf<PackageApplyResult>();

		await Assert.That(await ReadAttributeAsync(widgetObjid, "FN_DROP")).IsEqualTo("");
		await Assert.That((await Registry.GetManagedAttributesAsync("flags-pkg")).Any(b => b.Attribute == "FN_DROP")).IsFalse();
		await Assert.That(await ReadAttributeAsync(widgetObjid, "FN_KEEP")).IsEqualTo("keep-me");
		await Assert.That(await ReadAttributeFlagsAsync(widgetObjid, "FN_KEEP")).Contains("veiled");

		await Assert.That((await Installer.UninstallAsync("flags-pkg")).Value).IsTypeOf<Success>();
	}

	private async Task<IReadOnlyList<string>> ReadObjectFlagsAsync(string objid)
	{
		var node = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(objid)!.Value);
		var flags = new List<string>();
		await foreach (var flag in node.Expect<AnySharpObject>().Object().Flags.Value)
		{
			flags.Add(flag.Name);
		}

		return flags;
	}

	private async Task<bool> HasLockAsync(string objid, string lockType)
	{
		var node = await Database.GetObjectNodeAsync(PackageInstallService.ParseObjid(objid)!.Value);
		return node.Expect<AnySharpObject>().Object().Locks.ContainsKey(lockType);
	}

	/// <summary>
	/// Every other assertion in this file reads the object back through <c>Database</c>, which is
	/// the store and therefore never sees the cache at all. This one reads it the way the rest of
	/// the engine does — <see cref="GetObjectNodeQuery"/>, the cached <c>object:#N</c> entry — and
	/// so is the only test here that can observe an install writing around the cache.
	/// </summary>
	/// <remarks>
	/// The upgrade changes exactly the three things the installer used to write straight at the
	/// store: the name (<c>UpdateMetadata</c>), the parent, and a lock. The widget carries no
	/// attributes, so no attribute command incidentally invalidates <c>object:#N</c> for it and
	/// masks the bug. Engine data trunk §1: "a direct store write leaves the cache stale for the
	/// entry's whole lifetime".
	/// </remarks>
	[Test, NotInParallel]
	public async Task UpgradeRewiring_IsVisibleThroughTheCachedObjectRead()
	{
		var v1 = Parse(
			"""
			package: coherence-pkg
			version: "1.0"
			objects:
			  - ref: hall_a
			    type: room
			    name: Coherence Hall A
			  - ref: hall_b
			    type: room
			    name: Coherence Hall B
			  - ref: widget
			    type: thing
			    name: Coherence Widget One
			    location: "{{hall_a}}"
			    parent: "{{hall_a}}"
			    locks:
			      use: "{{hall_a}}"
			""");

		var answers = new Dictionary<string, string>();
		var install = await Installer.ApplyAsync(v1, new PackageApplyRequest(Source(), answers, []));
		var applied = install.Expect<PackageApplyResult>();

		var widget = PackageInstallService.ParseObjid(applied.CreatedObjects["widget"])!.Value;
		var hallA = PackageInstallService.ParseObjid(applied.CreatedObjects["hall_a"])!.Value;
		var hallB = PackageInstallService.ParseObjid(applied.CreatedObjects["hall_b"])!.Value;

		// Prime object:#N. From here on every cached read serves this snapshot until a write
		// declaring the key removes it.
		var before = (await Mediator.Send(new GetObjectNodeQuery(widget))).Expect<AnySharpObject>();
		await Assert.That(before.Object().Name).IsEqualTo("Coherence Widget One");
		await Assert.That((await before.Object().Parent.WithCancellation(CancellationToken.None))
			.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(hallA.Number);

		var v2 = Parse(
			"""
			package: coherence-pkg
			version: "1.1"
			objects:
			  - ref: hall_a
			    type: room
			    name: Coherence Hall A
			  - ref: hall_b
			    type: room
			    name: Coherence Hall B
			  - ref: widget
			    type: thing
			    name: Coherence Widget Two
			    location: "{{hall_a}}"
			    parent: "{{hall_b}}"
			    locks:
			      use: "{{hall_b}}"
			""");

		var upgrade = await Installer.ApplyAsync(v2, new PackageApplyRequest(Source("commit-2"), answers, []));
		await Assert.That(upgrade.Value).IsTypeOf<PackageApplyResult>();

		var after = (await Mediator.Send(new GetObjectNodeQuery(widget))).Expect<AnySharpObject>();
		await Assert.That(after.Object().Name).IsEqualTo("Coherence Widget Two")
			.Because("the rename must invalidate object:#N, not just land in the store");
		await Assert.That((await after.Object().Parent.WithCancellation(CancellationToken.None))
			.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(hallB.Number)
			.Because("the re-parent must invalidate object:#N, not just land in the store");
		await Assert.That(after.Object().Locks.TryGetValue("use", out var useLock)).IsTrue();
		await Assert.That(useLock!.LockString).Contains($"#{hallB.Number}")
			.Because("the lock rewrite must invalidate object:#N, not just land in the store");

		await Assert.That((await Installer.UninstallAsync("coherence-pkg")).Value).IsTypeOf<Success>();
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
		var applied = install.Expect<PackageApplyResult>();
		var gadgetObjid = applied.CreatedObjects["gadget"];

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
		await Assert.That(upgrade.Value).IsTypeOf<PackageApplyResult>();

		var flagsV2 = await ReadObjectFlagsAsync(gadgetObjid);
		await Assert.That(flagsV2).Contains("DARK");
		await Assert.That(flagsV2).DoesNotContain("OPAQUE");
		await Assert.That(await HasLockAsync(gadgetObjid, "use")).IsFalse();

		await Assert.That((await Installer.UninstallAsync("struct-pkg")).Value).IsTypeOf<Success>();
	}

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
				Database,
				Database,
				Database,
				Registry,
				Applications,
				WebAppFactoryArg.Services.GetRequiredService<IPackagePlanService>(),
				WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>(),
				WebAppFactoryArg.Services.GetRequiredService<IPackageLifecycleRunner>(),
				managedInstaller,
				WebAppFactoryArg.Services.GetRequiredService<IMediator>());

			var refused = await installer.ApplyAsync(
				manifest,
				new PackageApplyRequest(Source(), new Dictionary<string, string>(), [], 10, AllowManagedCode: false),
				CancellationToken.None,
				new DirectoryBinarySource(sourceDir));
			await Assert.That(refused.Value).IsTypeOf<Error<string>>().Because("a managed install without the opt-in must be refused");
			await Assert.That((await Registry.GetInstalledPackageAsync("e2e-managed")).Value).IsTypeOf<NotFound>()
				.Because("a refused managed install records nothing");

			var applied = await installer.ApplyAsync(
				manifest,
				new PackageApplyRequest(Source(), new Dictionary<string, string>(), [], 10, AllowManagedCode: true),
				CancellationToken.None,
				new DirectoryBinarySource(sourceDir));
			await Assert.That(applied.Value).IsTypeOf<PackageApplyResult>().Because("the opt-in + allow-list + matching hash should install");

			var depositedDll = System.IO.Path.Combine(pluginsRoot, "e2e-managed", "CommandOnlyPlugin.dll");
			await Assert.That(System.IO.File.Exists(depositedDll)).IsTrue();

			var record = (await Registry.GetInstalledPackageAsync("e2e-managed")).Expect<InstalledPackageRecord>();
			await Assert.That(record.DeployedFiles).IsNotNull();
			await Assert.That(record.DeployedFiles!).Contains("CommandOnlyPlugin.dll")
				.Because("the deployed file list must round-trip through the active DB provider");

			var uninstalled = await installer.UninstallAsync("e2e-managed");
			await Assert.That(uninstalled.Value).IsTypeOf<Success>();
			await Assert.That(System.IO.Directory.Exists(System.IO.Path.Combine(pluginsRoot, "e2e-managed"))).IsFalse();
			await Assert.That((await Registry.GetInstalledPackageAsync("e2e-managed")).Value).IsTypeOf<NotFound>();
		}
		finally
		{
			System.IO.Directory.Delete(sourceDir, true);
			if (System.IO.Directory.Exists(pluginsRoot)) System.IO.Directory.Delete(pluginsRoot, true);
		}
	}
}
