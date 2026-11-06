using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
using SharpMUSH.Server.Mudlet;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// The interface gmcp-mudlet offers Mudlet when the game names none: a package Mudlet can install, served to
/// anyone, whose version is the one gmcp-mudlet sends.
/// </summary>
public class MudletInterfaceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task ThePackageHoldsItsConfigAndOneScriptNamedForIt()
	{
		using var zip = new ZipArchive(new MemoryStream(MudletInterface.Package.ToArray()), ZipArchiveMode.Read);
		await Assert.That(zip.Entries.Select(e => e.FullName)).IsEquivalentTo(["config.lua", "sharpmush-ui.xml"]);

		using var config = new StreamReader(zip.GetEntry("config.lua")!.Open());
		await Assert.That(await config.ReadToEndAsync()).Contains($"mpackage = [[{MudletInterface.PackageName}]]")
			.Because("Mudlet names the installed package after the URL's file name, and finds it again by that name");
		await Assert.That(MudletInterface.Path).EndsWith($"/{MudletInterface.PackageName}.mpackage");

		using var xml = zip.GetEntry("sharpmush-ui.xml")!.Open();
		var document = XDocument.Load(xml);
		await Assert.That(document.Root!.Name.LocalName).IsEqualTo("MudletPackage");
		var scripts = document.Descendants("Script").ToList();
		await Assert.That(scripts).Count().IsEqualTo(1);
		await Assert.That(scripts[0].Element("script")!.Value).IsEqualTo(MudletInterface.Lua);
	}

	[Test]
	public async Task ItsVersionIsTheOneGmcpMudletSends()
	{
		await Assert.That(MudletInterface.Version).Matches(@"^\d+\.\d+\.\d+$");
		var manifest = BundledPackages.ManifestYaml("gmcp-mudlet");
		await Assert.That(manifest).Contains($"FUN`GUI`SHIPPED`VERSION: |-\n        {MudletInterface.Version}\n")
			.Because("Mudlet installs the interface again only when the version it is sent changes");
	}

	[Test]
	public async Task TheServerServesItToAnyone()
	{
		using var client = Factory.CreateHttpClient();
		using var response = await client.GetAsync(MudletInterface.Path);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/zip");
		await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(MudletInterface.Package.ToArray());
	}
}
