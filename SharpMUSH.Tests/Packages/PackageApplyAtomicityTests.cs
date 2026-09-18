using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// An apply either lands whole or leaves the world and the registry as it found them (#1180). A
/// failure the apply can foresee is refused before the first write; one it cannot (here, a lock key
/// the lock parser rejects) is undone from the state captured before the first write.
/// </summary>
public class PackageApplyAtomicityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private PackageManifestService Manifests { get; } = new();

	/// <summary>A key the manifest parser accepts and the lock parser refuses at write time.</summary>
	private const string UnbindableLock = "&|&|";

	private static PackageApplyRequest Request(string commit = "commit-1", IReadOnlyDictionary<string, string>? answers = null) =>
		new(new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", "atomicity/", commit, "main"),
			answers ?? new Dictionary<string, string>(), []);

	private PackageManifest Parse(string yaml) => Manifests.ParseManifest(yaml) switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	private async Task<SharpObject[]> ObjectsNamedAsync(string name) =>
		await Database.GetAllObjectsAsync().Where(o => o.Name == name).ToArrayAsync();

	private async Task<SharpObject> LiveAsync(string objid) =>
		(await Database.GetObjectNodeAsync(DBRef.Parse(objid))).Expect<AnySharpObject>().Object();

	private async Task<bool> IsGoingAsync(string objid) =>
		await (await LiveAsync(objid)).Flags.Value.AnyAsync(f => f.Name == "GOING");

	private async Task<string?> AttributeAsync(string objid, string attribute) =>
		(await Database.GetAttributeAsync(DBRef.Parse(objid), [attribute]).LastOrDefaultAsync())?.Value.ToPlainText();

	[Test, NotInParallel]
	public async Task FreshInstall_FailingAfterItsFirstWrite_LeavesNothingBehind()
	{
		var manifest = Parse($$$"""
			package: atom-fresh
			version: "1.0"
			objects:
			  - ref: hall
			    type: room
			    name: Atom Fresh Hall
			  - ref: widget
			    type: thing
			    name: Atom Fresh Widget
			    location: "{{hall}}"
			    flags: [no_command]
			    locks:
			      use: "{{{UnbindableLock}}}"
			    attributes:
			      FN_ONE: |-
			        one
			""");

		var result = await Installer.ApplyAsync(manifest, Request());

		await Assert.That(result.Expect<Error<string>>().Value).Contains("I don't understand that key");
		await Assert.That((await Registry.GetInstalledPackageAsync("atom-fresh")).Value).IsTypeOf<NotFound>();
		await Assert.That(await Registry.GetPackageRevisionsAsync("atom-fresh")).IsEmpty();
		await Assert.That(await Registry.GetPackageObjectsAsync("atom-fresh")).IsEmpty();
		await Assert.That(await Registry.GetManagedAttributesAsync("atom-fresh")).IsEmpty();
		foreach (var name in new[] { "Atom Fresh Hall", "Atom Fresh Widget" })
		{
			var created = await ObjectsNamedAsync(name);
			await Assert.That(created).IsNotEmpty().Because("the lock write fails after both objects exist");
			foreach (var obj in created)
			{
				await Assert.That(await IsGoingAsync(obj.DBRef.ToString())).IsTrue();
			}
		}
	}

	[Test, NotInParallel]
	public async Task Upgrade_FailingAfterItsFirstWrite_RestoresThePreviousRevision()
	{
		var v1 = Parse("""
			package: atom-upgrade
			version: "1.0"
			objects:
			  - ref: widget
			    type: thing
			    name: Atom Upgrade Widget
			    flags: [no_command]
			    locks:
			      use: "#TRUE"
			    attributes:
			      FN_VALUE: |-
			        one
			""");
		var v2 = Parse($$$"""
			package: atom-upgrade
			version: "2.0"
			objects:
			  - ref: widget
			    type: thing
			    name: Atom Upgrade Widget Renamed
			    flags: [no_command, dark]
			    locks:
			      use: "#FALSE"
			      enter: "{{{UnbindableLock}}}"
			    attributes:
			      FN_VALUE: |-
			        two
			      FN_NEW: |-
			        new
			  - ref: extra
			    type: thing
			    name: Atom Upgrade Extra
			""");

		var widget = (await Installer.ApplyAsync(v1, Request())).Expect<PackageApplyResult>().CreatedObjects["widget"];
		var before = await LiveAsync(widget);
		var beforeFlags = await before.Flags.Value.Select(f => f.Name).Order().ToArrayAsync();
		var beforeBaselines = (await Registry.GetManagedAttributesAsync("atom-upgrade"))
			.Select(a => $"{a.Objid}/{a.Attribute}={a.BaselineValue}").Order().ToArray();
		var beforeStructure = (await Registry.GetManagedStructuresAsync("atom-upgrade")).Single().StructureJson;

		var result = await Installer.ApplyAsync(v2, Request("commit-2"));

		await Assert.That(result.Expect<Error<string>>().Value).Contains("I don't understand that key");

		var after = await LiveAsync(widget);
		await Assert.That(after.Name).IsEqualTo("Atom Upgrade Widget");
		await Assert.That(await after.Flags.Value.Select(f => f.Name).Order().ToArrayAsync()).IsEquivalentTo(beforeFlags);
		await Assert.That(after.Locks.Keys.Select(LockNames.Canonical)).IsEquivalentTo(["Use"]);
		await Assert.That(after.Locks.Values.Single().LockString).IsEqualTo(before.Locks.Values.Single().LockString);
		await Assert.That(await AttributeAsync(widget, "FN_VALUE")).IsEqualTo("one");
		await Assert.That(await AttributeAsync(widget, "FN_NEW")).IsNull();

		var installed = (await Registry.GetInstalledPackageAsync("atom-upgrade")).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Version).IsEqualTo("1.0.0");
		await Assert.That(installed.CurrentRevision).IsEqualTo(1);
		await Assert.That((await Registry.GetPackageRevisionsAsync("atom-upgrade")).Count).IsEqualTo(1);
		await Assert.That((await Registry.GetPackageObjectsAsync("atom-upgrade")).Select(o => o.Objid)).IsEquivalentTo([widget]);
		await Assert.That((await Registry.GetManagedAttributesAsync("atom-upgrade"))
			.Select(a => $"{a.Objid}/{a.Attribute}={a.BaselineValue}").Order().ToArray()).IsEquivalentTo(beforeBaselines);
		await Assert.That((await Registry.GetManagedStructuresAsync("atom-upgrade")).Single().StructureJson).IsEqualTo(beforeStructure);

		foreach (var extra in await ObjectsNamedAsync("Atom Upgrade Extra"))
		{
			await Assert.That(await IsGoingAsync(extra.DBRef.ToString())).IsTrue();
		}

		await Assert.That((await Installer.UninstallAsync("atom-upgrade")).Value).IsTypeOf<Success>();
	}

	[Test, NotInParallel]
	public async Task Apply_WithAReferenceItCannotResolve_IsRefusedBeforeCreatingAnything()
	{
		var manifest = Parse("""
			package: atom-unresolved
			version: "1.0"
			configure:
			  keyholder:
			    label: "Who may use the widget"
			objects:
			  - ref: widget
			    type: thing
			    name: Atom Unresolved Widget
			    locks:
			      use: "{{?keyholder}}"
			""");

		var result = await Installer.ApplyAsync(manifest, Request());

		await Assert.That(result.Expect<Error<string>>().Value).Contains("{{?keyholder}} is unresolved");
		await Assert.That(await ObjectsNamedAsync("Atom Unresolved Widget")).IsEmpty();
		await Assert.That((await Registry.GetInstalledPackageAsync("atom-unresolved")).Value).IsTypeOf<NotFound>();
	}

	[Test, NotInParallel]
	public async Task Apply_WithAnExitWhoseDestinationCannotResolve_IsRefusedBeforeCreatingAnything()
	{
		var manifest = Parse("""
			package: atom-exit
			version: "1.0"
			configure:
			  elsewhere:
			    label: "Where the exit leads"
			objects:
			  - ref: hall
			    type: room
			    name: Atom Exit Hall
			  - ref: door
			    type: exit
			    name: Atom Exit Door
			    location: "{{hall}}"
			    destination: "{{?elsewhere}}"
			""");

		var result = await Installer.ApplyAsync(manifest, Request(answers: new Dictionary<string, string> { ["elsewhere"] = "#-5" }));

		await Assert.That(result.Expect<Error<string>>().Value).Contains("destination is not resolvable");
		await Assert.That(await ObjectsNamedAsync("Atom Exit Hall")).IsEmpty();
		await Assert.That(await ObjectsNamedAsync("Atom Exit Door")).IsEmpty();
	}
}
