using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
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

	/// <summary>
	/// The registry is the session database's, which the package install tests write to as well, so
	/// every package id, remote name and objid these tests count by is unique to this run.
	/// </summary>
	private static readonly string Run = Guid.NewGuid().ToString("N")[..8];

	private static string Id(string name) => $"{name}-{Run}";

	private static readonly long RunStamp = Random.Shared.NextInt64(1, long.MaxValue);
	private static readonly string Objid900 = $"#900:{RunStamp}";
	private static readonly string Objid901 = $"#901:{RunStamp}";

	private static InstalledPackageRecord SamplePackage(string id, string version = "1.2.0", int revision = 1) => new(
		id, version, "https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/",
		"a3f8c1d000000", "main", Anchor, revision);

	[Test]
	public async Task InstalledPackages_UpsertGetListRemove()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("reg-alpha")));
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("reg-beta")));

		var fetched = await Registry.GetInstalledPackageAsync(Id("reg-alpha"));
		await Assert.That(fetched.Value).IsEqualTo(SamplePackage(Id("reg-alpha")));

		// Upsert replaces in full.
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("reg-alpha"), "1.3.0", revision: 2));
		var upgraded = (await Registry.GetInstalledPackageAsync(Id("reg-alpha"))).Expect<InstalledPackageRecord>();
		await Assert.That(upgraded.Version).IsEqualTo("1.3.0");
		await Assert.That(upgraded.CurrentRevision).IsEqualTo(2);

		var all = await Registry.GetInstalledPackagesAsync();
		await Assert.That(all.Count(p => p.Id.StartsWith("reg-") && p.Id.EndsWith(Run))).IsEqualTo(2);

		await Registry.RemoveInstalledPackageAsync(Id("reg-alpha"));
		await Registry.RemoveInstalledPackageAsync(Id("reg-beta"));
		var missing = await Registry.GetInstalledPackageAsync(Id("reg-alpha"));
		await Assert.That(missing.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task PackageObjects_UpsertListRemove()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("obj-pkg")));
		await Registry.UpsertPackageObjectAsync(new PackageObjectRecord(Id("obj-pkg"), "global_thing", "#900:123", "thing"));
		await Registry.UpsertPackageObjectAsync(new PackageObjectRecord(Id("obj-pkg"), "lounge", "#901:123", "room"));

		// Upsert on (package, ref) replaces the objid (re-create scenario).
		await Registry.UpsertPackageObjectAsync(new PackageObjectRecord(Id("obj-pkg"), "global_thing", "#950:456", "thing"));

		var objects = await Registry.GetPackageObjectsAsync(Id("obj-pkg"));
		await Assert.That(objects.Count).IsEqualTo(2);
		await Assert.That(objects.First(o => o.Ref == "global_thing").Objid).IsEqualTo("#950:456");

		await Registry.RemovePackageObjectAsync(Id("obj-pkg"), "lounge");
		await Assert.That((await Registry.GetPackageObjectsAsync(Id("obj-pkg"))).Count).IsEqualTo(1);

		await Registry.RemoveInstalledPackageAsync(Id("obj-pkg"));
		await Assert.That((await Registry.GetPackageObjectsAsync(Id("obj-pkg"))).Count).IsEqualTo(0);
	}

	[Test]
	public async Task ManagedAttributes_FullBaselines_CrossPackageQueries()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("attr-pkg")));
		var baseline = new ManagedAttributeRecord(
			Id("attr-pkg"), Objid900, "CMD_+BBREAD",
			"$+bbread *:@pemit %#=[u(#950/FN_READ,%0)]", "hash-1", "1.2.0");
		await Registry.UpsertManagedAttributeAsync(baseline);
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			Id("attr-pkg"), Objid901, "FN_FORMAT", "format-value", "hash-2", "1.2.0"));
		// A second package managing an attr on the same object (cross-package).
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("attr-pkg2")));
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			Id("attr-pkg2"), Objid900, "CMD_+BBADMIN", "admin-value", "hash-3", "0.1.0"));

		var byPackage = await Registry.GetManagedAttributesAsync(Id("attr-pkg"));
		await Assert.That(byPackage.Count).IsEqualTo(2);
		await Assert.That(byPackage.First(a => a.Attribute == "CMD_+BBREAD").BaselineValue)
			.IsEqualTo(baseline.BaselineValue);

		var byObject = await Registry.GetManagedAttributesForObjectAsync(Objid900);
		await Assert.That(byObject.Count).IsEqualTo(2);
		await Assert.That(byObject.Select(a => a.PackageId).Distinct().Count()).IsEqualTo(2);

		// Upsert replaces the baseline on the identity triple.
		await Registry.UpsertManagedAttributeAsync(baseline with { BaselineValue = "new-value", BaselineHash = "hash-9" });
		var replaced = await Registry.GetManagedAttributesAsync(Id("attr-pkg"));
		await Assert.That(replaced.First(a => a.Attribute == "CMD_+BBREAD").BaselineHash).IsEqualTo("hash-9");

		await Registry.RemoveManagedAttributeAsync(Id("attr-pkg"), Objid901, "FN_FORMAT");
		await Assert.That((await Registry.GetManagedAttributesAsync(Id("attr-pkg"))).Count).IsEqualTo(1);

		await Registry.RemoveInstalledPackageAsync(Id("attr-pkg"));
		await Registry.RemoveInstalledPackageAsync(Id("attr-pkg2"));
		await Assert.That((await Registry.GetManagedAttributesForObjectAsync(Objid900)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task ManagedStructures_UpsertGetReplaceRemove_ClearedOnUninstall()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("struct-pkg")));
		const string json = """{"flags":["no_command","dark"],"powers":["pueblo"],"locks":{"use":"=#1"},"attributeFlags":{"FN_X":["veiled"]}}""";
		await Registry.UpsertManagedStructureAsync(new ManagedStructureRecord(Id("struct-pkg"), "#900:123", json, "1.2.0"));
		await Registry.UpsertManagedStructureAsync(new ManagedStructureRecord(Id("struct-pkg"), "#901:123", "{}", "1.2.0"));

		var fetched = await Registry.GetManagedStructuresAsync(Id("struct-pkg"));
		await Assert.That(fetched.Count).IsEqualTo(2);
		await Assert.That(fetched.First(s => s.Objid == "#900:123").StructureJson).IsEqualTo(json);

		// Upsert on (package, objid) replaces the JSON payload.
		await Registry.UpsertManagedStructureAsync(new ManagedStructureRecord(Id("struct-pkg"), "#900:123", "{}", "1.3.0"));
		var replaced = await Registry.GetManagedStructuresAsync(Id("struct-pkg"));
		await Assert.That(replaced.First(s => s.Objid == "#900:123").StructureJson).IsEqualTo("{}");
		await Assert.That(replaced.First(s => s.Objid == "#900:123").BaselineVersion).IsEqualTo("1.3.0");

		await Registry.RemoveManagedStructureAsync(Id("struct-pkg"), "#901:123");
		await Assert.That((await Registry.GetManagedStructuresAsync(Id("struct-pkg"))).Count).IsEqualTo(1);

		// RemoveInstalledPackage cascades to structure baselines.
		await Registry.RemoveInstalledPackageAsync(Id("struct-pkg"));
		await Assert.That((await Registry.GetManagedStructuresAsync(Id("struct-pkg"))).Count).IsEqualTo(0);
	}

	[Test]
	public async Task Dependencies_SetGetDependents_ReplaceSemantics()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("dep-core")));
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("dep-bbs")));
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("dep-jobs")));

		await Registry.SetPackageDependenciesAsync(Id("dep-bbs"),
			[new PackageDependencyRecord(Id("dep-bbs"), Id("dep-core"), ">=1.0 <2.0")]);
		await Registry.SetPackageDependenciesAsync(Id("dep-jobs"),
			[new PackageDependencyRecord(Id("dep-jobs"), Id("dep-core"), "")]);

		var bbsDeps = await Registry.GetPackageDependenciesAsync(Id("dep-bbs"));
		await Assert.That(bbsDeps.Count).IsEqualTo(1);
		await Assert.That(bbsDeps[0].DependsOnId).IsEqualTo(Id("dep-core"));
		await Assert.That(bbsDeps[0].Constraint).IsEqualTo(">=1.0 <2.0");

		var dependents = await Registry.GetPackageDependentsAsync(Id("dep-core"));
		await Assert.That(dependents.Count).IsEqualTo(2);
		await Assert.That(dependents.Select(d => d.PackageId).Order().ToArray())
			.IsEquivalentTo((string[])[Id("dep-bbs"), Id("dep-jobs")]);

		// Set replaces the whole outbound edge set.
		await Registry.SetPackageDependenciesAsync(Id("dep-bbs"), []);
		await Assert.That((await Registry.GetPackageDependenciesAsync(Id("dep-bbs"))).Count).IsEqualTo(0);
		await Assert.That((await Registry.GetPackageDependentsAsync(Id("dep-core"))).Count).IsEqualTo(1);

		// Removing a package clears edges in both directions.
		await Registry.RemoveInstalledPackageAsync(Id("dep-core"));
		await Assert.That((await Registry.GetPackageDependenciesAsync(Id("dep-jobs"))).Count).IsEqualTo(0);

		await Registry.RemoveInstalledPackageAsync(Id("dep-bbs"));
		await Registry.RemoveInstalledPackageAsync(Id("dep-jobs"));
	}

	[Test]
	public async Task Remotes_UpsertGetListRemove()
	{
		await Registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			Id("SharpMUSH Official"), "https://github.com/SharpMUSH/SharpMUSH-Packages", PackageRemoteTrust.Official, "main"));
		await Registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			Id("Volund Suite"), "https://example.com/volund/mush-suite", PackageRemoteTrust.Community, null));

		var fetched = await Registry.GetPackageRemoteAsync(Id("SharpMUSH Official"));
		await Assert.That(fetched.Expect<PackageRemoteRecord>().Trust).IsEqualTo(PackageRemoteTrust.Official);

		// Upsert replaces (trust downgrade scenario).
		await Registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			Id("Volund Suite"), "https://example.com/volund/mush-suite", PackageRemoteTrust.Unknown, "stable"));
		var downgraded = (await Registry.GetPackageRemoteAsync(Id("Volund Suite"))).Expect<PackageRemoteRecord>();
		await Assert.That(downgraded.Trust).IsEqualTo(PackageRemoteTrust.Unknown);
		await Assert.That(downgraded.Branch).IsEqualTo("stable");

		var all = await Registry.GetPackageRemotesAsync();
		await Assert.That(all.Count(remote => remote.Name.EndsWith(Run))).IsEqualTo(2);

		await Registry.RemovePackageRemoteAsync(Id("SharpMUSH Official"));
		await Registry.RemovePackageRemoteAsync(Id("Volund Suite"));
		await Assert.That((await Registry.GetPackageRemoteAsync(Id("Volund Suite"))).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task Revisions_AddGetPrune()
	{
		await Registry.UpsertInstalledPackageAsync(SamplePackage(Id("rev-pkg")));
		for (var i = 1; i <= 5; i++)
		{
			await Registry.AddPackageRevisionAsync(new PackageRevisionRecord(
				Id("rev-pkg"), i, i == 1 ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
				$"1.{i}.0", $"commit-{i}",
				"""{"objects":[]}""", """{"bbs_storage":"#123"}""", """{"overwritten":{}}""",
				Anchor.AddMinutes(i)));
		}

		var revisions = await Registry.GetPackageRevisionsAsync(Id("rev-pkg"));
		await Assert.That(revisions.Count).IsEqualTo(5);
		// Newest first.
		await Assert.That(revisions[0].Revision).IsEqualTo(5);
		await Assert.That(revisions[0].Kind).IsEqualTo(PackageRevisionKind.Upgrade);

		var second = (await Registry.GetPackageRevisionAsync(Id("rev-pkg"), 2)).Expect<PackageRevisionRecord>();
		await Assert.That(second.Version).IsEqualTo("1.2.0");
		await Assert.That(second.ConfigureAnswersJson).Contains("bbs_storage");
		await Assert.That(second.AppliedAt).IsEqualTo(Anchor.AddMinutes(2));

		await Registry.PrunePackageRevisionsAsync(Id("rev-pkg"), keep: 2);
		var pruned = await Registry.GetPackageRevisionsAsync(Id("rev-pkg"));
		await Assert.That(pruned.Count).IsEqualTo(2);
		await Assert.That(pruned.Select(r => r.Revision).ToArray()).IsEquivalentTo((int[])[5, 4]);

		await Registry.RemoveInstalledPackageAsync(Id("rev-pkg"));
		await Assert.That((await Registry.GetPackageRevisionsAsync(Id("rev-pkg"))).Count).IsEqualTo(0);
	}
}
