using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>Public collection labels survive real package and provider writes without changing identity.</summary>
public class PackageWikiCollectionNameInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static PackageManifest Manifest(string id, string version, string? label)
		=> new PackageManifestService().ParseManifest($$"""
			format: {{(label is null ? "1.4" : "1.13")}}
			package: {{id}}
			version: {{version}}
			{{(label is null ? string.Empty : "wiki_collection_name: " + JsonSerializer.Serialize(label))}}
			wiki:
			  - page: main:{{id}}
			    title: Collection label fixture
			    markdown: Stable content.
			""").Expect<ParsedPackageManifest>().Manifest;

	private static string? Label<T>(T value) =>
		JsonSerializer.SerializeToElement(value).TryGetProperty("WikiCollectionName", out var label) ? label.GetString() : null;

	private async Task AssertInstalled(string id, string? expected, string version)
	{
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Id).IsEqualTo(id);
		await Assert.That(installed.Version).IsEqualTo(version);
		await Assert.That(Label(installed)).IsEqualTo(expected);
		await Assert.That(Label((await Registry.GetInstalledPackagesAsync()).Single(package => package.Id == id))).IsEqualTo(expected);
		var revision = (await Registry.GetPackageRevisionAsync(id, installed.CurrentRevision)).Expect<PackageRevisionRecord>();
		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
			?? throw new InvalidOperationException("The installed revision has no snapshot.");
		await Assert.That(Label(snapshot)).IsEqualTo(expected);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task InstallUpgradeRollbackAndMetadataRemovalPersistTheRevisionLabel(bool legacyFirst)
	{
		var id = "label-" + Guid.NewGuid().ToString("N")[..12];
		var first = legacyFirst ? null : "Independent Rules";
		async Task Apply(string version, string? label)
		{
			(await Installer.ApplyAsync(Manifest(id, version, label), new PackageApplyRequest(
				new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", id + "/", "commit-" + version, "main"),
				new Dictionary<string, string>(), []))).Expect<PackageApplyResult>();
		}

		try
		{
			await Apply("1.0.0", first);
			await AssertInstalled(id, first, "1.0.0");
			await Apply("1.1.0", "Independent Rules Second Edition");
			await AssertInstalled(id, "Independent Rules Second Edition", "1.1.0");
			(await Installer.RollbackAsync(id, 1)).Expect<PackageRollbackResult>();
			await AssertInstalled(id, first, "1.0.0");
			await Apply("1.2.0", null);
			await AssertInstalled(id, null, "1.2.0");
			(await Installer.RollbackAsync(id, 2)).Expect<PackageRollbackResult>();
			await AssertInstalled(id, "Independent Rules Second Edition", "1.1.0");
		}
		finally { (await Installer.UninstallAsync(id)).Expect<Success>(); }
	}
}
