using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>A package's wiki properties (format 1.17), installed, upgraded and uninstalled against the real world.</summary>
public class PackageWikiPropertyInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => Services.GetRequiredService<IWikiService>();

	private static PackageManifest Manifest(string package, string version, string property, string values)
		=> new PackageManifestService().ParseManifest($"""
			format: "1.17"
			package: {package}
			title: Wiki property test
			version: "{version}"
			objects:
			  - ref: desk
			    type: thing
			    name: Property Desk
			wiki_properties:
			  {property}:
			    type: keyword
			    values: [{values}]
			""").Expect<ParsedPackageManifest>().Manifest;

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, string package, string commit)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{package}/", commit, "main"),
			new Dictionary<string, string>(), []));

	private async Task<PropertyDefinition?> PropertyAsync(string id)
		=> (await Wiki.GetFactSchemaAsync()).Properties.SingleOrDefault(p => p.Id == id);

	/// <summary>
	/// Installing defines the property and a page may state it; an upgrade changes its values; uninstalling removes it
	/// and the page keeps its fact.
	/// </summary>
	[Test]
	public async Task InstallDefinesThePropertyAndUninstallRemovesItLeavingPagesFacts()
	{
		var id = Guid.NewGuid().ToString("N")[..8];
		var package = $"wiki-prop-{id}";
		var property = $"t{id}:size";

		(await ApplyAsync(Manifest(package, "1.0", property, "small, large"), package, "commit-1")).Expect<PackageApplyResult>();
		await Assert.That((await PropertyAsync(property))!.AllowedKeywords.Select(k => k.Value)).IsEquivalentTo(["small", "large"]);
		var page = (await Wiki.CreateAsync($"Sized {id}", "Body.", "#1")).Expect<WikiPage>();
		(await Wiki.SetFactsAsync(page.Id, [new WikiPageFact(property, "large")])).Expect<WikiPage>();

		var rival = await Installer.PlanAsync(Manifest($"wiki-rival-{id}", "1.0", property, "small, large"));
		await Assert.That(rival.IsBlocked).IsTrue();

		(await ApplyAsync(Manifest(package, "2.0", property, "small, medium, large"), package, "commit-2")).Expect<PackageApplyResult>();
		await Assert.That((await PropertyAsync(property))!.AllowedKeywords.Select(k => k.Value)).IsEquivalentTo(["small", "medium", "large"]);

		await Assert.That((await Installer.UninstallAsync(package)).Value).IsTypeOf<Success>();
		await Assert.That(await PropertyAsync(property)).IsNull();
		await Assert.That((await Wiki.GetByIdAsync(page.Id)).Expect<WikiPage>().Facts).IsEquivalentTo([new WikiPageFact(property, "large")]);
	}
}
