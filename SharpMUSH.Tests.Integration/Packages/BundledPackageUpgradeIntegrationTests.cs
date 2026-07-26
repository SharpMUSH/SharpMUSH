using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Packages;
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
	private IPackageManifestService Manifests => factory.Services.GetRequiredService<IPackageManifestService>();
	private IPackageInstallService Installer => factory.Services.GetRequiredService<IPackageInstallService>();

	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	private const string PackageId = "upgrade-probe";

	private static string Manifest(string version, string extraAttribute) => string.Join('\n',
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
		"        first",
		extraAttribute);

	/// <summary>Applies a manifest the way bootstrap does: plan, keep local edits, apply.</summary>
	private async Task ApplyAsync(string yaml)
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
	}

	[Test]
	public async Task ReapplyingANewerManifest_AddsTheNewAttributesAndRecordsTheNewVersion()
	{
		try
		{
			await ApplyAsync(Manifest("1.0.0", string.Empty));

			var installed = await Registry.GetInstalledPackageAsync(PackageId);
			await Assert.That(installed.IsT0).IsTrue();
			await Assert.That(installed.AsT0.Version).IsEqualTo("1.0.0");
			await Assert.That((await Registry.GetManagedAttributesAsync(PackageId)).Select(m => m.Attribute))
				.DoesNotContain("PROBE`ADDED");

			// The v1.1.0 shape: same object, one route added — exactly what profile-handler 1.1.0 did
			// when it gained GET`ONLINE, and what a game installed at 1.0.0 never received.
			await ApplyAsync(Manifest("1.1.0", "      PROBE`ADDED: |-\n        second"));

			var upgraded = await Registry.GetInstalledPackageAsync(PackageId);
			await Assert.That(upgraded.IsT0).IsTrue();
			await Assert.That(upgraded.AsT0.Version).IsEqualTo("1.1.0");

			var managed = (await Registry.GetManagedAttributesAsync(PackageId)).Select(m => m.Attribute).ToList();
			await Assert.That(managed).Contains("PROBE`ADDED");
			await Assert.That(managed).Contains("PROBE`ORIGINAL");
		}
		finally
		{
			await Installer.UninstallAsync(PackageId, force: true);
		}
	}
}
