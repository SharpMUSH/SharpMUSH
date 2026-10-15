using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// An upgrade over a game's own edits, against the real world: an attribute both sides changed on different lines
/// is written merged, and the baseline moves to the package's value so the game's lines stay a local edit.
/// </summary>
public class PackageLocalEditInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static PackageManifest Manifest(string id, string version, string text)
		=> new PackageManifestService().ParseManifest($$"""
			package: {{id}}
			version: "{{version}}"
			objects:
			  - ref: notes
			    type: thing
			    name: Notes {{id}}
			    attributes:
			      DATA`TEXT: "{{text}}"
			""").Expect<ParsedPackageManifest>().Manifest;

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://example.com/notes.git", null, "commit-" + manifest.Version, null),
			new Dictionary<string, string>(), []));

	private async Task<string> ReadAsync(DBRef dbref)
		=> (await Database.GetAttributeAsync(dbref, ["DATA", "TEXT"], CancellationToken.None).LastOrDefaultAsync())
			?.Value.ToPlainText() ?? "";

	[Test]
	public async Task LinesOnlyOneSideChanged_AreMergedOnUpgrade()
	{
		var id = "edits-" + Guid.NewGuid().ToString("N")[..8];
		(await ApplyAsync(Manifest(id, "1.0.0", "one\\ntwo\\nthree"))).Expect<PackageApplyResult>();
		var objid = (await Registry.GetPackageObjectsAsync(id)).Single().Objid;
		var dbref = DBRef.Parse(objid);

		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		await Database.SetAttributeAsync(dbref, ["DATA", "TEXT"], MarkupText.Plain("ONE\ntwo\nthree"), pm);

		var next = Manifest(id, "1.1.0", "one\\ntwo\\nTHREE");
		var plan = await Installer.PlanAsync(next);
		var change = plan.Attributes.Single(a => a.Attribute == "DATA`TEXT");
		await Assert.That(change.Action).IsEqualTo(PackageAttributeAction.Merge);
		await Assert.That(plan.HasConflicts).IsFalse();

		(await ApplyAsync(next)).Expect<PackageApplyResult>();
		await Assert.That(await ReadAsync(dbref)).IsEqualTo("ONE\ntwo\nTHREE");
		var baseline = (await Registry.GetManagedAttributesAsync(id)).Single(b => b.Attribute == "DATA`TEXT");
		await Assert.That(baseline.BaselineValue).IsEqualTo("one\ntwo\nTHREE");

		// The game's line is still its own: the same version again keeps it.
		var again = await Installer.PlanAsync(next);
		await Assert.That(again.Attributes.Single(a => a.Attribute == "DATA`TEXT").Action).IsEqualTo(PackageAttributeAction.KeepLocal);

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}
}
