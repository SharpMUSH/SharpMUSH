using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// The bundled "scene" package is delivered by the package manager (create mode):
/// an owned WIZARD "Scene Logger" thing carries the pose/say/semipose capture hooks
/// and the +scene/* player verbs. Its AINSTALL (once) and STARTUP (every boot)
/// (re-)establish OVERRIDE hooks on POSE/SAY/SEMIPOSE pointing at this object. A
/// second, deliberately unflagged "Scene Help" thing carries the package's +help
/// topics, which the librarian could not evaluate on a privileged object.
///
/// These assertions are read-only against the singleton package, so they run safely
/// alongside the other tests.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ScenePackageTests(ServerWebAppFactory factory)
{
	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	private IHookService Hooks => factory.Services.GetRequiredService<IHookService>();

	[Test]
	public async Task ScenePackage_IsInstalled_WithWizardLoggerObjectAndAttributes()
	{
		var package = await Registry.GetInstalledPackageAsync("scene");
		await Assert.That(package.IsT0).IsTrue();
		await Assert.That(package.AsT0.Version).IsEqualTo("1.14.0");

		var objects = await Registry.GetPackageObjectsAsync("scene");
		// Two created objects: the WIZARD Logger that runs the verbs and the @hook overrides, and
		// a plain Scene Help thing carrying the +help topics. They are separate because u() on a
		// privileged object's attribute is refused unless the evaluator is privileged too, so the
		// +help librarian could not render a topic that lived on the Logger.
		await Assert.That(objects.Select(o => o.Ref).Order()).IsEquivalentTo(new[] { "help", "logger" });

		var attrs = (await Registry.GetManagedAttributesAsync("scene"))
			.Select(m => m.Attribute).ToList();
		await Assert.That(attrs).Contains("CMD`CAPTURE`POSE");
		await Assert.That(attrs).Contains("CMD`CAPTURE`SAY");
		await Assert.That(attrs).Contains("CMD`CAPTURE`SEMI");
		await Assert.That(attrs).Contains("CMD`CREATE");
		await Assert.That(attrs).Contains("CMD`INFO");
		await Assert.That(attrs).Contains("FUN`OWNS");
		await Assert.That(attrs).Contains("FUN`IS`APPROVED");
		await Assert.That(attrs).Contains("DATA`DENY_APPROVAL");
		await Assert.That(attrs).Contains("AINSTALL");
		await Assert.That(attrs).Contains("STARTUP");

		// The Scene Logger object is WIZARD (required: @hook target + wizard @scene/@hook).
		var loggerObjid = objects.Single(o => o.Ref == "logger").Objid;
		var loggerDbref = PackageInstallService.ParseObjid(loggerObjid)!.Value;
		var node = await factory.Services.GetRequiredService<ISharpDatabase>()
			.GetObjectNodeAsync(loggerDbref);
		var flags = new List<string>();
		await foreach (var flag in node.Known.Object().Flags.Value)
		{
			flags.Add(flag.Name);
		}

		await Assert.That(flags).Contains("WIZARD");
	}

	[Test]
	public async Task ScenePackage_OverrideHooks_ResolveToTheSceneLoggerObject()
	{
		var objects = await Registry.GetPackageObjectsAsync("scene");
		var loggerDbref = PackageInstallService.ParseObjid(objects.Single(o => o.Ref == "logger").Objid)!.Value;

		// STARTUP sets @hook/override on POSE, SAY, SEMIPOSE, and @EMIT, all targeting
		// the Scene Logger with its CMD`CAPTURE`* capture attributes.
		await AssertOverrideHook("POSE", loggerDbref.Number, "CMD`CAPTURE`POSE");
		await AssertOverrideHook("SAY", loggerDbref.Number, "CMD`CAPTURE`SAY");
		await AssertOverrideHook("SEMIPOSE", loggerDbref.Number, "CMD`CAPTURE`SEMI");
		await AssertOverrideHook("@EMIT", loggerDbref.Number, "CMD`CAPTURE`EMIT");
	}

	private async Task AssertOverrideHook(string command, int expectedTargetNumber, string expectedAttribute)
	{
		var hook = await Hooks.GetHookAsync(command, "OVERRIDE");
		await Assert.That(hook.IsT0).IsTrue();
		await Assert.That(hook.AsT0.TargetObject.Number).IsEqualTo(expectedTargetNumber);
		await Assert.That(hook.AsT0.AttributeName.ToUpperInvariant()).IsEqualTo(expectedAttribute);
	}
}
