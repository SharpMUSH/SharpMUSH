using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Plugins;

/// <summary>What the portal's plugin upload accepts, what it keeps, and what it refuses before writing anything.</summary>
public class PluginUploadStoreTests
{
	private const string Id = "upload-sample";

	private static PluginUploadStore Store(PluginDirectories directories) =>
		new(directories, new PackageManifestService(), NullLogger<PluginUploadStore>.Instance);

	private static MemoryStream Zip(params (string Name, byte[] Bytes)[] entries)
	{
		var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var (name, bytes) in entries)
			{
				using var entry = zip.CreateEntry(name).Open();
				entry.Write(bytes);
			}
		}

		stream.Position = 0;
		return stream;
	}

	private static (string, byte[]) Yaml(string yaml) => ("package.yaml", Encoding.UTF8.GetBytes(yaml));

	private static (string, byte[])[] PluginPackage() =>
	[
		Yaml(PluginPackageFixture.Yaml(Id)),
		(PluginPackageFixture.DllName, File.ReadAllBytes(PluginPackageFixture.DllPath)),
		(PluginManifest.FileName, PluginPackageFixture.PluginJson(Id)),
		("README.md", "not listed, not kept"u8.ToArray()),
	];

	private static async Task InScratch(Func<PluginDirectories, Task> test)
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			await test(directories);
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public Task PluginPackage_IsStaged_WithOnlyItsListedFiles() => InScratch(async directories =>
	{
		var store = Store(directories);
		var staged = (await store.StageAsync(Zip(PluginPackage()), CancellationToken.None)).Expect<PluginUploadResponse>();

		await Assert.That(staged.Remote).IsEqualTo(PluginUploadStore.RemoteName);
		await Assert.That(staged.PackageId).IsEqualTo(Id);
		await Assert.That(store.ManifestYaml(staged.Path)).IsNotNull();
		var dll = await store.Binaries(staged.Path)!.ReadBinaryAsync(PluginPackageFixture.DllName);
		await Assert.That(PluginPackageFixture.Sha256Of(dll!)).IsEqualTo(PluginPackageFixture.DllSha);
		await Assert.That(await store.Binaries(staged.Path)!.ReadBinaryAsync("README.md")).IsNull()
			.Because("only package.yaml and the files it lists are kept");

		store.Discard(staged.Path);
		await Assert.That(store.ManifestYaml(staged.Path)).IsNull();
	});

	[Test]
	public Task Folders_AreRefused() => InScratch(async directories =>
	{
		var result = await Store(directories).StageAsync(
			Zip([.. PluginPackage(), ("nested/evil.dll", [1, 2, 3])]), CancellationToken.None);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("nested/evil.dll");
		await Assert.That(Directory.Exists(directories.UploadStaging)).IsFalse().Because("a refused upload writes nothing");
	});

	[Test]
	public Task SoftcodePackage_IsRefused() => InScratch(async directories =>
	{
		var result = await Store(directories).StageAsync(Zip(Yaml("""
			package: not-a-plugin
			version: "1.0.0"
			objects:
			  - ref: thing
			    type: thing
			    name: Thing
			""")), CancellationToken.None);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("Only plugin packages");
	});

	[Test]
	public Task MissingListedFile_IsRefused() => InScratch(async directories =>
	{
		var result = await Store(directories).StageAsync(
			Zip(Yaml(PluginPackageFixture.Yaml(Id)), (PluginManifest.FileName, PluginPackageFixture.PluginJson(Id))),
			CancellationToken.None);

		await Assert.That(result.Expect<Error<string>>().Value).Contains(PluginPackageFixture.DllName);
	});

	[Test]
	public Task NotAZip_IsRefused() => InScratch(async directories =>
	{
		var result = await Store(directories).StageAsync(new MemoryStream("plain text"u8.ToArray()), CancellationToken.None);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("not a zip");
	});

	[Test]
	public Task TokensThatAreNotOurs_FindNothing() => InScratch(async directories =>
	{
		var store = Store(directories);
		Directory.CreateDirectory(Path.Combine(directories.Installed, "elsewhere"));

		await Assert.That(store.ManifestYaml("../elsewhere")).IsNull();
		await Assert.That(store.ManifestYaml(new string('a', 32))).IsNull();
		await Assert.That(store.Binaries("..")).IsNull();
	});
}
