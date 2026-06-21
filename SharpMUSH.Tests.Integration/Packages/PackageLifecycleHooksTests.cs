using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// Lifecycle hooks (decision 20.x): after a successful apply, the package
/// manager runs <c>AINSTALL</c> on a first install and <c>AUPDATE</c> on an
/// upgrade (never <c>AINSTALL</c> again). The hooks are evaluated as softcode on
/// the package's own object, so a package can self-configure after deployment.
///
/// NOTE: these run only once the integrator registers
/// <c>IPackageLifecycleRunner -&gt; PackageLifecycleRunner</c> in Startup.cs (see the
/// task report). Until then resolving <see cref="IPackageInstallService"/> fails
/// because the runner is an unsatisfied constructor dependency; the assertions
/// are the acceptance criterion for that wiring.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class PackageLifecycleHooksTests(ServerWebAppFactory factory)
{
	private ISharpDatabase Database => factory.Services.GetRequiredService<ISharpDatabase>();
	private IPackageInstallService Installer => factory.Services.GetRequiredService<IPackageInstallService>();
	private PackageManifestService Manifests { get; } = new();

	/// <summary>A unique package id per test so concurrent runs never collide on a shared registry entry.</summary>
	private static string UniquePackageId() => $"lifecycle-pkg-{Guid.NewGuid():N}";

	private static PackageApplySource Source(string pkg, string commit = "commit-1") => new(
		"https://github.com/SharpMUSH/SharpMUSH-Packages", $"{pkg}/", commit, "main");

	// The lifecycle attributes are run as a COMMAND LIST (like @startup) under the package object
	// itself, so they use the & attribute-set command to mark `me`.
	private static string Manifest(string pkg, string version) =>
		$"""
		package: {pkg}
		version: "{version}"
		objects:
		  - ref: marker
		    type: thing
		    name: Lifecycle Marker
		    attributes:
		      AINSTALL: |-
		        &INSTALL_MARKER me=installed
		      AUPDATE: |-
		        &UPDATE_MARKER me=updated
		      FN: |-
		        {version}
		""";

	private PackageManifest Parse(string yaml) => Manifests.ParseManifest(yaml).AsT0.Manifest;

	private async Task<string> ReadAttributeAsync(string objid, string attribute)
	{
		var dbref = PackageInstallService.ParseObjid(objid)!.Value;
		var leaf = await Database.GetAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Value.ToPlainText() ?? "";
	}

	private async Task ClearAttributeAsync(string objid, string attribute)
	{
		var dbref = PackageInstallService.ParseObjid(objid)!.Value;
		await Database.ClearAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None);
	}

	[Test, NotInParallel]
	public async Task FirstInstall_RunsAinstall_NotAupdate()
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();

		var install = await Installer.ApplyAsync(Parse(Manifest(pkg, "1.0")), new PackageApplyRequest(Source(pkg), answers, []));
		await Assert.That(install.IsT0).IsTrue();

		var markerObjid = install.AsT0.CreatedObjects["marker"];

		// AINSTALL ran on first install: it set its own marker attribute.
		await Assert.That(await ReadAttributeAsync(markerObjid, "INSTALL_MARKER")).IsEqualTo("installed");

		// AUPDATE did NOT run on a first install.
		await Assert.That(await ReadAttributeAsync(markerObjid, "UPDATE_MARKER")).IsEqualTo("");

		await Assert.That((await Installer.UninstallAsync(pkg)).IsT0).IsTrue();
	}

	// Mirrors the bundled scene package's AINSTALL (`@teleport %!=#2`): a WIZARD thing
	// that relocates ITSELF into the master room (#2) at first install, so its $-commands
	// become globally matched. This is the exact mechanism the Scene Logger relies on for
	// remote +scene/* capture; the assertion proves AINSTALL actually lands it in #2.
	private static string TeleportManifest(string pkg) =>
		$"""
		package: {pkg}
		version: "1.0"
		objects:
		  - ref: marker
		    type: thing
		    name: Teleport Marker
		    flags: [WIZARD]
		    attributes:
		      AINSTALL: |-
		        @teleport %!=#2
		""";

	[Test, NotInParallel]
	public async Task Ainstall_TeleportSelfToMasterRoom_LandsObjectInRoom2()
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();

		var install = await Installer.ApplyAsync(Parse(TeleportManifest(pkg)), new PackageApplyRequest(Source(pkg), answers, []));
		await Assert.That(install.IsT0).IsTrue();

		var markerObjid = install.AsT0.CreatedObjects["marker"];
		var markerDbref = PackageInstallService.ParseObjid(markerObjid)!.Value;

		// The object must be in #2 purely because AINSTALL's `@teleport %!=#2` ran — no manual move.
		var location = (await Database.GetLocationAsync(markerDbref)).WithoutNone();
		await Assert.That(location.Object().DBRef.Number).IsEqualTo(2)
			.Because("AINSTALL `@teleport %!=#2` must land the package object in the master room (#2)");

		await Assert.That((await Installer.UninstallAsync(pkg)).IsT0).IsTrue();
	}

	[Test, NotInParallel]
	public async Task Upgrade_RunsAupdate_NotAinstall()
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();

		// First install, then prove the upgrade path.
		var install = await Installer.ApplyAsync(Parse(Manifest(pkg, "1.0")), new PackageApplyRequest(Source(pkg), answers, []));
		await Assert.That(install.IsT0).IsTrue();
		var markerObjid = install.AsT0.CreatedObjects["marker"];

		// Wipe the install marker so we can prove AINSTALL does NOT re-run on upgrade.
		await ClearAttributeAsync(markerObjid, "INSTALL_MARKER");
		await Assert.That(await ReadAttributeAsync(markerObjid, "INSTALL_MARKER")).IsEqualTo("");

		// Upgrade to v1.1: this is an Upgrade revision, so AUPDATE runs and AINSTALL does not.
		var upgrade = await Installer.ApplyAsync(Parse(Manifest(pkg, "1.1")), new PackageApplyRequest(Source(pkg, "commit-2"), answers, []));
		await Assert.That(upgrade.IsT0).IsTrue();
		await Assert.That(upgrade.AsT0.CreatedObjects.Count).IsEqualTo(0);

		// AUPDATE ran on the upgrade.
		await Assert.That(await ReadAttributeAsync(markerObjid, "UPDATE_MARKER")).IsEqualTo("updated");

		// AINSTALL did NOT re-run (the cleared marker stays cleared).
		await Assert.That(await ReadAttributeAsync(markerObjid, "INSTALL_MARKER")).IsEqualTo("");

		await Assert.That((await Installer.UninstallAsync(pkg)).IsT0).IsTrue();
	}
}
