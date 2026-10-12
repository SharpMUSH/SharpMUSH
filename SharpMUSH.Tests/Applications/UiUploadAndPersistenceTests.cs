using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.Applications;

public class UiUploadAndPersistenceTests
{
	[Test]
	public async Task ExistingUploadWorkflowStagesPlainUiWithoutDll()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var files = new Dictionary<string, byte[]> { ["ui.json"] = Encoding.UTF8.GetBytes(UiDefinitionTests.DefinitionJson), ["index.html"] = Encoding.UTF8.GetBytes("<h1>Graph</h1>") };
			files["package.yaml"] = Encoding.UTF8.GetBytes(PackageManifestWriter.Write(UiPackageDeploymentTests.Manifest(files)));
			using var stream = new MemoryStream();
			using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
				foreach (var file in files)
				{
					using var entry = archive.CreateEntry(file.Key).Open(); entry.Write(file.Value);
				}
			stream.Position = 0;
			var uploads = new PluginUploadStore(directories, new PackageManifestService(), NullLogger<PluginUploadStore>.Instance);
			var staged = (await uploads.StageAsync(stream, default)).Expect<PluginUploadResponse>();
			await Assert.That(staged.PackageId).IsEqualTo("graph-proof");
			await Assert.That(uploads.ManifestYaml(staged.Path)).Contains("kind: ui");
		}
		finally { PluginPackageFixture.Delete(directories); }
	}

	[Test]
	public async Task LightningCodecPreservesIndependentConfigsAndDefaultsOldRecords()
	{
		var east = new ApplicationRecord { Slug = "east", DefinitionId = "graph-proof", ConfigVersion = 1, Config = JsonSerializer.SerializeToElement(new { rooms = new[] { 10 } }) };
		var west = east with { Slug = "west", Config = JsonSerializer.SerializeToElement(new { rooms = new[] { 20 } }) };
		var savedEast = Codec.Deserialize<ApplicationRecord>(Codec.Serialize(east));
		var savedWest = Codec.Deserialize<ApplicationRecord>(Codec.Serialize(west));
		await Assert.That(savedEast.Config!.Value.GetProperty("rooms")[0].GetInt32()).IsEqualTo(10);
		await Assert.That(savedWest.Config!.Value.GetProperty("rooms")[0].GetInt32()).IsEqualTo(20);
		await Assert.That(savedEast.DefinitionId).IsEqualTo("graph-proof");
		var old = Codec.Deserialize<ApplicationRecord>(Encoding.UTF8.GetBytes("{\"Slug\":\"old\"}"));
		await Assert.That(old.ConfigVersion).IsEqualTo(1);
		await Assert.That(old.Config).IsNull();
	}
}
