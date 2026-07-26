using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// Boot-time bootstrap upgrades an already-installed bundled package when the build ships a newer
/// version, instead of skipping it forever. This exercises the apply path that upgrade relies on:
/// re-applying a newer manifest of an installed package must add the routes it gained, keep the
/// admin's local edits, and move the recorded version forward.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class BundledPackageUpgradeIntegrationTests(ServerWebAppFactory factory)
{
	private ISharpDatabase Database => factory.Services.GetRequiredService<ISharpDatabase>();
	private IPackageManifestService Manifests => factory.Services.GetRequiredService<IPackageManifestService>();
	private IPackageInstallService Installer => factory.Services.GetRequiredService<IPackageInstallService>();

	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	private const string PackageId = "upgrade-probe";

	/// <param name="version">Manifest version.</param>
	/// <param name="originalValue">Value the manifest ships for PROBE`ORIGINAL.</param>
	/// <param name="extraAttribute">Optional extra attribute lines (already indented).</param>
	private static string Manifest(string version, string originalValue, string extraAttribute) => string.Join('\n',
		"format: 1",
		$"package: {PackageId}",
		$"version: {version}",
		"authors: [SharpMUSH]",
		"description: \"Upgrade path probe.\"",
		"license: MIT",
		"requires_server: \">=0.1\"",
		"",
		"objects:",
		"  - ref: probe",
		"    type: thing",
		"    name: Upgrade Probe Object",
		"    attributes:",
		"      PROBE`ORIGINAL: |-",
		$"        {originalValue}",
		extraAttribute);

	/// <summary>
	/// Applies a manifest the way bootstrap does: plan, answer every conflict with KeepMine, apply.
	/// Returns how many conflicts were resolved, so a test can prove the KeepMine path was actually
	/// taken rather than passing because no conflict ever arose.
	/// </summary>
	private async Task<int> ApplyAsync(string yaml)
	{
		var manifest = Manifests.ParseManifest(yaml).AsT0.Manifest;
		var plan = await Installer.PlanAsync(manifest);
		var decisions = plan.Attributes
			.Where(a => a.Action == PackageAttributeAction.Conflict)
			.Select(a => new PackageConflictDecision(a.TargetRef, a.Attribute, PackageConflictResolution.KeepMine))
			.ToList();

		var result = await Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("bundled:sharpmush", PackageId, "bundled", null),
			new Dictionary<string, string>(), decisions));

		await Assert.That(result.IsT0).IsTrue().Because(result.IsT1 ? result.AsT1.Value : "applied");
		return decisions.Count;
	}

	private DBRef ProbeDbref(IReadOnlyList<PackageObjectRecord> objects) =>
		PackageInstallService.ParseObjid(objects.Single(o => o.Ref == "probe").Objid)!.Value;

	private async Task<string> ReadAttributeAsync(DBRef dbref, string attribute)
	{
		var leaf = await Database.GetAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Value.ToPlainText() ?? "";
	}

	[Test]
	public async Task ReapplyingANewerManifest_AddsNewAttributes_KeepsLocalEdits_AndRecordsTheNewVersion()
	{
		try
		{
			await ApplyAsync(Manifest("1.0.0", "first", string.Empty));

			var installed = await Registry.GetInstalledPackageAsync(PackageId);
			await Assert.That(installed.IsT0).IsTrue();
			await Assert.That(installed.AsT0.Version).IsEqualTo("1.0.0");
			await Assert.That((await Registry.GetManagedAttributesAsync(PackageId)).Select(m => m.Attribute))
				.DoesNotContain("PROBE`ADDED");

			// An admin edits the installed softcode in-game. The upgrade must not clobber it — that is
			// the whole reason bootstrap answers every conflict with KeepMine.
			var probe = ProbeDbref(await Registry.GetPackageObjectsAsync(PackageId));
			var packageManager = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
				.CurrentValue.Database.PackageManager ?? 7;
			var owner = (await Database.GetObjectNodeAsync(new DBRef((int)packageManager)))
				.Known.Match(player => player, _ => null!, _ => null!, _ => null!);
			await Database.SetAttributeAsync(probe, ["PROBE", "ORIGINAL"], MModule.single("admin-edit"), owner);

			// v1.1.0: adds a route AND changes the value the admin edited, so the upgrade produces both
			// an Add and a ModifyModify conflict — exactly what profile-handler 1.1.0 did when it gained
			// GET`ONLINE, and what a game installed at 1.0.0 never received.
			var conflictsResolved = await ApplyAsync(Manifest("1.1.0", "second", "      PROBE`ADDED: |-\n        added"));

			// Without this the KeepMine assertion below could pass simply because nothing conflicted.
			await Assert.That(conflictsResolved)
				.IsGreaterThan(0)
				.Because("the edited attribute must reach the plan as a conflict for KeepMine to mean anything");

			var upgraded = await Registry.GetInstalledPackageAsync(PackageId);
			await Assert.That(upgraded.IsT0).IsTrue();
			await Assert.That(upgraded.AsT0.Version).IsEqualTo("1.1.0");

			var managed = (await Registry.GetManagedAttributesAsync(PackageId)).Select(m => m.Attribute).ToList();
			await Assert.That(managed).Contains("PROBE`ADDED");
			await Assert.That(managed).Contains("PROBE`ORIGINAL");

			// The attribute the package gained is live...
			await Assert.That(await ReadAttributeAsync(probe, "PROBE`ADDED")).IsEqualTo("added");
			// ...and the admin's edit survived rather than being reset to the manifest's "second".
			await Assert.That(await ReadAttributeAsync(probe, "PROBE`ORIGINAL")).IsEqualTo("admin-edit");
		}
		finally
		{
			await Installer.UninstallAsync(PackageId, force: true);
		}
	}
}
