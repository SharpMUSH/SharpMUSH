using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Integration tests for the package registry system collections
/// (sys_packages, sys_package_objects, sys_package_depends,
/// sys_managed_attributes, sys_remotes, sys_package_revisions) against the
/// active database provider.
/// </summary>
public class PackageRegistryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageRegistryService Registry =>
		(IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.ISharpDatabase>();

	private static readonly DateTimeOffset Anchor = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

	private static InstalledPackageRecord SamplePackage(string id, string version = "1.2.0", int revision = 1) => new(
		id, version, "https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/",
		"a3f8c1d000000", "main", Anchor, revision);

	[Test, NotInParallel]
	public async Task InstalledPackages_UpsertGetListRemove()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage("reg-alpha"));
		await Registry.UpsertInstalledPackageAsync(SamplePackage("reg-beta"));

		var fetched = await Registry.GetInstalledPackageAsync("reg-alpha");
		await Assert.That(fetched.IsT0).IsTrue();
		await Assert.That(fetched.AsT0).IsEqualTo(SamplePackage("reg-alpha"));

		// Upsert replaces in full.
		await Registry.UpsertInstalledPackageAsync(SamplePackage("reg-alpha", "1.3.0", revision: 2));
		var upgraded = await Registry.GetInstalledPackageAsync("reg-alpha");
		await Assert.That(upgraded.AsT0.Version).IsEqualTo("1.3.0");
		await Assert.That(upgraded.AsT0.CurrentRevision).IsEqualTo(2);

		var all = await Registry.GetInstalledPackagesAsync();
		await Assert.That(all.Count(p => p.Id.StartsWith("reg-"))).IsEqualTo(2);

		await Registry.RemoveInstalledPackageAsync("reg-alpha");
		await Registry.RemoveInstalledPackageAsync("reg-beta");
		var missing = await Registry.GetInstalledPackageAsync("reg-alpha");
		await Assert.That(missing.IsT1).IsTrue();
	}

	[Test, NotInParallel]
	public async Task PackageObjects_UpsertListRemove()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage("obj-pkg"));
		await Registry.UpsertPackageObjectAsync(new PackageObjectRecord("obj-pkg", "global_thing", "#900:123", "thing"));
		await Registry.UpsertPackageObjectAsync(new PackageObjectRecord("obj-pkg", "lounge", "#901:123", "room"));

		// Upsert on (package, ref) replaces the objid (re-create scenario).
		await Registry.UpsertPackageObjectAsync(new PackageObjectRecord("obj-pkg", "global_thing", "#950:456", "thing"));

		var objects = await Registry.GetPackageObjectsAsync("obj-pkg");
		await Assert.That(objects.Count).IsEqualTo(2);
		await Assert.That(objects.First(o => o.Ref == "global_thing").Objid).IsEqualTo("#950:456");

		await Registry.RemovePackageObjectAsync("obj-pkg", "lounge");
		await Assert.That((await Registry.GetPackageObjectsAsync("obj-pkg")).Count).IsEqualTo(1);

		await Registry.RemoveInstalledPackageAsync("obj-pkg");
		await Assert.That((await Registry.GetPackageObjectsAsync("obj-pkg")).Count).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task ManagedAttributes_FullBaselines_CrossPackageQueries()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage("attr-pkg"));
		var baseline = new ManagedAttributeRecord(
			"attr-pkg", "#900:123", "CMD_+BBREAD",
			"$+bbread *:@pemit %#=[u(#950/FN_READ,%0)]", "hash-1", "1.2.0");
		await Registry.UpsertManagedAttributeAsync(baseline);
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			"attr-pkg", "#901:123", "FN_FORMAT", "format-value", "hash-2", "1.2.0"));
		// A second package managing an attr on the same object (cross-package).
		await Registry.UpsertInstalledPackageAsync(SamplePackage("attr-pkg2"));
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			"attr-pkg2", "#900:123", "CMD_+BBADMIN", "admin-value", "hash-3", "0.1.0"));

		var byPackage = await Registry.GetManagedAttributesAsync("attr-pkg");
		await Assert.That(byPackage.Count).IsEqualTo(2);
		await Assert.That(byPackage.First(a => a.Attribute == "CMD_+BBREAD").BaselineValue)
			.IsEqualTo(baseline.BaselineValue);

		var byObject = await Registry.GetManagedAttributesForObjectAsync("#900:123");
		await Assert.That(byObject.Count).IsEqualTo(2);
		await Assert.That(byObject.Select(a => a.PackageId).Distinct().Count()).IsEqualTo(2);

		// Upsert replaces the baseline on the identity triple.
		await Registry.UpsertManagedAttributeAsync(baseline with { BaselineValue = "new-value", BaselineHash = "hash-9" });
		var replaced = await Registry.GetManagedAttributesAsync("attr-pkg");
		await Assert.That(replaced.First(a => a.Attribute == "CMD_+BBREAD").BaselineHash).IsEqualTo("hash-9");

		await Registry.RemoveManagedAttributeAsync("attr-pkg", "#901:123", "FN_FORMAT");
		await Assert.That((await Registry.GetManagedAttributesAsync("attr-pkg")).Count).IsEqualTo(1);

		await Registry.RemoveInstalledPackageAsync("attr-pkg");
		await Registry.RemoveInstalledPackageAsync("attr-pkg2");
		await Assert.That((await Registry.GetManagedAttributesForObjectAsync("#900:123")).Count).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task ManagedStructures_UpsertGetReplaceRemove_ClearedOnUninstall()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage("struct-pkg"));
		const string json = """{"flags":["no_command","dark"],"powers":["pueblo"],"locks":{"use":"=#1"},"attributeFlags":{"FN_X":["veiled"]}}""";
		await Registry.UpsertManagedStructureAsync(new ManagedStructureRecord("struct-pkg", "#900:123", json, "1.2.0"));
		await Registry.UpsertManagedStructureAsync(new ManagedStructureRecord("struct-pkg", "#901:123", "{}", "1.2.0"));

		var fetched = await Registry.GetManagedStructuresAsync("struct-pkg");
		await Assert.That(fetched.Count).IsEqualTo(2);
		await Assert.That(fetched.First(s => s.Objid == "#900:123").StructureJson).IsEqualTo(json);

		// Upsert on (package, objid) replaces the JSON payload.
		await Registry.UpsertManagedStructureAsync(new ManagedStructureRecord("struct-pkg", "#900:123", "{}", "1.3.0"));
		var replaced = await Registry.GetManagedStructuresAsync("struct-pkg");
		await Assert.That(replaced.First(s => s.Objid == "#900:123").StructureJson).IsEqualTo("{}");
		await Assert.That(replaced.First(s => s.Objid == "#900:123").BaselineVersion).IsEqualTo("1.3.0");

		await Registry.RemoveManagedStructureAsync("struct-pkg", "#901:123");
		await Assert.That((await Registry.GetManagedStructuresAsync("struct-pkg")).Count).IsEqualTo(1);

		// RemoveInstalledPackage cascades to structure baselines.
		await Registry.RemoveInstalledPackageAsync("struct-pkg");
		await Assert.That((await Registry.GetManagedStructuresAsync("struct-pkg")).Count).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task Dependencies_SetGetDependents_ReplaceSemantics()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage("dep-core"));
		await Registry.UpsertInstalledPackageAsync(SamplePackage("dep-bbs"));
		await Registry.UpsertInstalledPackageAsync(SamplePackage("dep-jobs"));

		await Registry.SetPackageDependenciesAsync("dep-bbs",
			[new PackageDependencyRecord("dep-bbs", "dep-core", ">=1.0 <2.0")]);
		await Registry.SetPackageDependenciesAsync("dep-jobs",
			[new PackageDependencyRecord("dep-jobs", "dep-core", "")]);

		var bbsDeps = await Registry.GetPackageDependenciesAsync("dep-bbs");
		await Assert.That(bbsDeps.Count).IsEqualTo(1);
		await Assert.That(bbsDeps[0].DependsOnId).IsEqualTo("dep-core");
		await Assert.That(bbsDeps[0].Constraint).IsEqualTo(">=1.0 <2.0");

		var dependents = await Registry.GetPackageDependentsAsync("dep-core");
		await Assert.That(dependents.Count).IsEqualTo(2);
		await Assert.That(dependents.Select(d => d.PackageId).Order().ToArray())
			.IsEquivalentTo((string[])["dep-bbs", "dep-jobs"]);

		// Set replaces the whole outbound edge set.
		await Registry.SetPackageDependenciesAsync("dep-bbs", []);
		await Assert.That((await Registry.GetPackageDependenciesAsync("dep-bbs")).Count).IsEqualTo(0);
		await Assert.That((await Registry.GetPackageDependentsAsync("dep-core")).Count).IsEqualTo(1);

		// Removing a package clears edges in both directions.
		await Registry.RemoveInstalledPackageAsync("dep-core");
		await Assert.That((await Registry.GetPackageDependenciesAsync("dep-jobs")).Count).IsEqualTo(0);

		await Registry.RemoveInstalledPackageAsync("dep-bbs");
		await Registry.RemoveInstalledPackageAsync("dep-jobs");
	}

	[Test, NotInParallel]
	public async Task Remotes_UpsertGetListRemove()
	{
		await Registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			"SharpMUSH Official", "https://github.com/SharpMUSH/SharpMUSH-Packages", PackageRemoteTrust.Official, "main"));
		await Registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			"Volund Suite", "https://example.com/volund/mush-suite", PackageRemoteTrust.Community, null));

		var fetched = await Registry.GetPackageRemoteAsync("SharpMUSH Official");
		await Assert.That(fetched.IsT0).IsTrue();
		await Assert.That(fetched.AsT0.Trust).IsEqualTo(PackageRemoteTrust.Official);

		// Upsert replaces (trust downgrade scenario).
		await Registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			"Volund Suite", "https://example.com/volund/mush-suite", PackageRemoteTrust.Unknown, "stable"));
		var downgraded = await Registry.GetPackageRemoteAsync("Volund Suite");
		await Assert.That(downgraded.AsT0.Trust).IsEqualTo(PackageRemoteTrust.Unknown);
		await Assert.That(downgraded.AsT0.Branch).IsEqualTo("stable");

		var all = await Registry.GetPackageRemotesAsync();
		await Assert.That(all.Count).IsEqualTo(2);

		await Registry.RemovePackageRemoteAsync("SharpMUSH Official");
		await Registry.RemovePackageRemoteAsync("Volund Suite");
		await Assert.That((await Registry.GetPackageRemoteAsync("Volund Suite")).IsT1).IsTrue();
	}

	[Test, NotInParallel]
	public async Task Revisions_AddGetPrune()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage("rev-pkg"));
		for (var i = 1; i <= 5; i++)
		{
			await Registry.AddPackageRevisionAsync(new PackageRevisionRecord(
				"rev-pkg", i, i == 1 ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
				$"1.{i}.0", $"commit-{i}",
				"""{"objects":[]}""", """{"bbs_storage":"#123"}""", """{"overwritten":{}}""",
				Anchor.AddMinutes(i)));
		}

		var revisions = await Registry.GetPackageRevisionsAsync("rev-pkg");
		await Assert.That(revisions.Count).IsEqualTo(5);
		// Newest first.
		await Assert.That(revisions[0].Revision).IsEqualTo(5);
		await Assert.That(revisions[0].Kind).IsEqualTo(PackageRevisionKind.Upgrade);

		var second = await Registry.GetPackageRevisionAsync("rev-pkg", 2);
		await Assert.That(second.IsT0).IsTrue();
		await Assert.That(second.AsT0.Version).IsEqualTo("1.2.0");
		await Assert.That(second.AsT0.ConfigureAnswersJson).Contains("bbs_storage");
		await Assert.That(second.AsT0.AppliedAt).IsEqualTo(Anchor.AddMinutes(2));

		await Registry.PrunePackageRevisionsAsync("rev-pkg", keep: 2);
		var pruned = await Registry.GetPackageRevisionsAsync("rev-pkg");
		await Assert.That(pruned.Count).IsEqualTo(2);
		await Assert.That(pruned.Select(r => r.Revision).ToArray()).IsEquivalentTo((int[])[5, 4]);

		await Registry.RemoveInstalledPackageAsync("rev-pkg");
		await Assert.That((await Registry.GetPackageRevisionsAsync("rev-pkg")).Count).IsEqualTo(0);
	}
}
