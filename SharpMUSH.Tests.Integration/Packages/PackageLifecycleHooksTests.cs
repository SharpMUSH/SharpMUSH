using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
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

	private PackageManifest Parse(string yaml) => Manifests.ParseManifest(yaml) switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(
			$"The test manifest does not parse: {string.Join("; ", failure.Errors)}"),
	};

	/// <summary>The applied revision; a failed apply fails the test with the installer's reason.</summary>
	private static PackageApplyResult Applied(Result<PackageApplyResult> result) => result switch
	{
		PackageApplyResult applied => applied,
		Error<string> error => throw new InvalidOperationException($"The package did not apply: {error.Value}"),
	};

	private async Task<string> ReadAttributeAsync(string objid, string attribute)
	{
		var dbref = DBRef.Parse(objid);
		var leaf = await Database.GetAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Value.ToPlainText() ?? "";
	}

	private async Task ClearAttributeAsync(string objid, string attribute)
	{
		var dbref = DBRef.Parse(objid);
		await Database.ClearAttributeAsync(dbref, attribute.Split('`'), CancellationToken.None);
	}

	[Test, NotInParallel]
	public async Task FirstInstall_RunsAinstall_NotAupdate()
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();

		var install = Applied(await Installer.ApplyAsync(Parse(Manifest(pkg, "1.0")), new PackageApplyRequest(Source(pkg), answers, [])));

		var markerObjid = install.CreatedObjects["marker"];

		await Assert.That(await ReadAttributeAsync(markerObjid, "INSTALL_MARKER")).IsEqualTo("installed");

		await Assert.That(await ReadAttributeAsync(markerObjid, "UPDATE_MARKER")).IsEqualTo("");

		await Assert.That((await Installer.UninstallAsync(pkg)).Value).IsTypeOf<Success>();
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

		var install = Applied(await Installer.ApplyAsync(Parse(TeleportManifest(pkg)), new PackageApplyRequest(Source(pkg), answers, [])));

		var markerObjid = install.CreatedObjects["marker"];
		var markerDbref = DBRef.Parse(markerObjid);

		// The object must be in #2 purely because AINSTALL's `@teleport %!=#2` ran — no manual move.
		var location = (await Database.GetLocationRefAsync(markerDbref)).Expect<DBRef>();
		await Assert.That(location.Number).IsEqualTo(2)
			.Because("AINSTALL `@teleport %!=#2` must land the package object in the master room (#2)");

		await Assert.That((await Installer.UninstallAsync(pkg)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task Upgrade_RunsAupdate_NotAinstall()
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();

		var install = Applied(await Installer.ApplyAsync(Parse(Manifest(pkg, "1.0")), new PackageApplyRequest(Source(pkg), answers, [])));
		var markerObjid = install.CreatedObjects["marker"];

		// Wipe the install marker so we can prove AINSTALL does NOT re-run on upgrade.
		await ClearAttributeAsync(markerObjid, "INSTALL_MARKER");
		await Assert.That(await ReadAttributeAsync(markerObjid, "INSTALL_MARKER")).IsEqualTo("");

		// Upgrade to v1.1: this is an Upgrade revision, so AUPDATE runs and AINSTALL does not.
		var upgrade = Applied(await Installer.ApplyAsync(Parse(Manifest(pkg, "1.1")), new PackageApplyRequest(Source(pkg, "commit-2"), answers, [])));
		await Assert.That(upgrade.CreatedObjects.Count).IsEqualTo(0);

		await Assert.That(await ReadAttributeAsync(markerObjid, "UPDATE_MARKER")).IsEqualTo("updated");

		await Assert.That(await ReadAttributeAsync(markerObjid, "INSTALL_MARKER")).IsEqualTo("");

		await Assert.That((await Installer.UninstallAsync(pkg)).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task Upgrade_PassesCommittedVersionsToLifecycle(bool deferred)
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();
		string VersionedManifest(string version) => Manifest(pkg, version).Replace(
			"&UPDATE_MARKER me=updated",
			"&UPDATE_MARKER me=updated; &FROM_VERSION me=%<from_version>; &TO_VERSION me=%<to_version>");
		var install = Applied(await Installer.ApplyAsync(Parse(VersionedManifest("2.2.0")),
			new PackageApplyRequest(Source(pkg), answers, [])));
		var objid = install.CreatedObjects["marker"];
		try
		{
			var manifest = Parse(VersionedManifest("2.8.0"));
			var request = new PackageApplyRequest(Source(pkg, "commit-2"), answers, []);
			if (deferred)
			{
				var operations = factory.Services.GetRequiredService<IPackageOperationRunner>();
				var outcome = await operations.RunAsync("apply", token => Installer.ApplyAsync(manifest, request, token));
				Applied(outcome.Expect<PackageOperationRan<Result<PackageApplyResult>>>().Result);
			}
			else Applied(await Installer.ApplyAsync(manifest, request));

			await Assert.That(await ReadAttributeAsync(objid, "FROM_VERSION")).IsEqualTo("2.2.0");
			await Assert.That(await ReadAttributeAsync(objid, "TO_VERSION")).IsEqualTo("2.8.0");

			// A manual invocation must not infer a source version from the installed revision.
			await factory.Services.GetRequiredService<IPackageLifecycleRunner>().RunLifecycleAsync(objid, "AUPDATE");
			await Assert.That(await ReadAttributeAsync(objid, "FROM_VERSION")).IsEqualTo("");
			await Assert.That(await ReadAttributeAsync(objid, "TO_VERSION")).IsEqualTo("");
		}
		finally
		{
			await Installer.UninstallAsync(pkg);
		}
	}

	[Test, NotInParallel]
	public async Task DeferredUpgrades_KeepTheirOwnVersionsAfterAnotherApplyCommits()
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();
		string VersionedManifest(string version) => Manifest(pkg, version).Replace(
			"&UPDATE_MARKER me=updated",
			"&UPDATE_MARKER me=updated; &VERSION_TRACE me=v(VERSION_TRACE)%<from_version>:%<to_version>|");
		var install = Applied(await Installer.ApplyAsync(Parse(VersionedManifest("2.2.0")),
			new PackageApplyRequest(Source(pkg), answers, [])));
		var objid = install.CreatedObjects["marker"];
		try
		{
			var operations = factory.Services.GetRequiredService<IPackageOperationRunner>();
			var outcome = await operations.RunAsync("apply", async token =>
			{
				foreach (var version in new[] { "2.8.0", "2.9.0" })
					Applied(await Installer.ApplyAsync(Parse(VersionedManifest(version)),
						new PackageApplyRequest(Source(pkg, version), answers, []), token));
				return true;
			});
			await Assert.That(outcome.Expect<PackageOperationRan<bool>>().Result).IsTrue();
			await Assert.That(await ReadAttributeAsync(objid, "VERSION_TRACE"))
				.IsEqualTo("2.2.0:2.8.0|2.8.0:2.9.0|");
		}
		finally
		{
			await Installer.UninstallAsync(pkg);
		}
	}


	[Test, NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task Upgrade_CreatedAttributesExcludeAdoptedAndExistingValues(bool deferred)
	{
		var pkg = UniquePackageId();
		var answers = new Dictionary<string, string>();
		string VersionedManifest(string version) => Manifest(pkg, version).Replace(
			"&UPDATE_MARKER me=updated",
			"&UPDATE_MARKER me=updated; &CREATED_ATTRS me=%<created_attributes>");
		var install = Applied(await Installer.ApplyAsync(Parse(VersionedManifest("2.2.0")),
			new PackageApplyRequest(Source(pkg), answers, [])));
		var objid = install.CreatedObjects["marker"];
		try
		{
			var mediator = factory.Services.GetRequiredService<Mediator.IMediator>();
			var god = (await mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(new DBRef(1))))
				.Expect<AnySharpObject>().Expect<SharpPlayer>();
			await Database.SetAttributeAsync(DBRef.Parse(objid), ["ADOPTED"], MarkupText.Plain("same"), god);
			var manifest = Parse(VersionedManifest("2.8.0") + "\n      CREATED: new\n      ADOPTED: same\n"
				+ "  - ref: other\n    type: thing\n    name: Other\n    attributes:\n      FOREIGN: new\n");
			var request = new PackageApplyRequest(Source(pkg, "commit-2"), answers, []);
			if (deferred)
			{
				var outcome = await factory.Services.GetRequiredService<IPackageOperationRunner>()
					.RunAsync("apply", token => Installer.ApplyAsync(manifest, request, token));
				Applied(outcome.Expect<PackageOperationRan<Result<PackageApplyResult>>>().Result);
			}
			else Applied(await Installer.ApplyAsync(manifest, request));

			await Assert.That(await ReadAttributeAsync(objid, "CREATED_ATTRS")).IsEqualTo("marker/CREATED other/FOREIGN");
		}
		finally
		{
			await Installer.UninstallAsync(pkg);
		}
	}

}
