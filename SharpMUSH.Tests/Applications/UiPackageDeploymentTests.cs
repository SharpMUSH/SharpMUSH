using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.Applications;

public class UiPackageDeploymentTests
{
	[Test]
	public async Task PlainFilesInstallWithoutAssemblyServeVerifiedBytesAndUninstall()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var files = new Dictionary<string, byte[]>
			{
				["ui.json"] = Encoding.UTF8.GetBytes(UiDefinitionTests.DefinitionJson),
				["index.html"] = Encoding.UTF8.GetBytes("<!doctype html><h1>Graph</h1>")
			};
			var manifest = Manifest(files);
			var installer = new PluginPackageInstaller(Substitute.For<IPluginManager>(), new(true), directories, NullLogger<PluginPackageInstaller>.Instance);
			var request = new PackageApplyRequest(new("repo", "graph-proof", "commit", "main"), new Dictionary<string, string>(), [], 10, AllowPluginCode: true);
			var deployed = (await installer.DeployAsync(manifest, request, new Bytes(files))).Expect<IReadOnlyList<string>>();
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, "graph-proof"))).IsFalse();
			var store = new UiPackageStore(directories);
			await Assert.That((await store.GetAsync("graph-proof"))!.Id).IsEqualTo("graph-proof");
			await Assert.That(await store.ReadAsync("graph-proof", "index.html")).IsEquivalentTo(files["index.html"]);
			await Assert.That(await store.ReadAsync("../graph-proof", "index.html")).IsNull();
			await File.WriteAllTextAsync(Path.Combine(directories.UiPackages, "graph-proof", "index.html"), "tampered");
			await Assert.That(await store.ReadAsync("graph-proof", "index.html")).IsNull();
			await installer.RemoveAsync("graph-proof", deployed);
			await Assert.That(await store.GetAsync("graph-proof")).IsNull();
		}
		finally { PluginPackageFixture.Delete(directories); }
	}

	[Test]
	public async Task AssemblyInPlainUiPayloadIsRejectedBeforeWriting()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var files = new Dictionary<string, byte[]> { ["ui.json"] = Encoding.UTF8.GetBytes(UiDefinitionTests.DefinitionJson), ["index.html"] = [], ["payload.dll"] = [1, 2] };
			var installer = new PluginPackageInstaller(Substitute.For<IPluginManager>(), new(true), directories, NullLogger<PluginPackageInstaller>.Instance);
			var request = new PackageApplyRequest(new("repo", "graph-proof", "commit", "main"), new Dictionary<string, string>(), [], 10, AllowPluginCode: true);
			await Assert.That(await installer.DeployAsync(Manifest(files), request, new Bytes(files)) is Error<string>).IsTrue();
			await Assert.That(Directory.Exists(Path.Combine(directories.UiPackages, "graph-proof"))).IsFalse();
		}
		finally { PluginPackageFixture.Delete(directories); }
	}

	[Test]
	public async Task ExampleGraphPackageHasValidHashesAndLoadsItsDefinition()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "examples", "packages", "builder-graph"))) current = current.Parent;
		if (current is null) throw new DirectoryNotFoundException("builder-graph example is missing.");
		var folder = Path.Combine(current.FullName, "examples", "packages", "builder-graph");
		var manifest = new PackageManifestService().ParseManifest(await File.ReadAllTextAsync(Path.Combine(folder, "package.yaml"))) switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			_ => throw new InvalidOperationException("Example package is invalid.")
		};
		var files = new Dictionary<string, byte[]>();
		foreach (var file in manifest.Binary!.Files) files[file.FileName] = await File.ReadAllBytesAsync(Path.Combine(folder, file.FileName));
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var installer = new PluginPackageInstaller(Substitute.For<IPluginManager>(), new(true), directories, NullLogger<PluginPackageInstaller>.Instance);
			var request = new PackageApplyRequest(new("repo", manifest.Name, "commit", "main"), new Dictionary<string, string>(), [], 10, AllowPluginCode: true);
			await Assert.That(await installer.DeployAsync(manifest, request, new Bytes(files)) is IReadOnlyList<string>).IsTrue();
			var store = new UiPackageStore(directories);
			var definition = await store.GetAsync("builder-graph");
			await Assert.That(definition).IsNotNull();
			await Assert.That(definition!.ValidateConfig(1, System.Text.Json.JsonSerializer.SerializeToElement(new { title = "East", rooms = new[] { 10, 20 } }))).IsNull();
			await Assert.That(definition.ValidateConfig(1, System.Text.Json.JsonSerializer.SerializeToElement(new
			{
				title = "East",
				rooms = new[] { 10, 20 },
				coordinates = new[] { "MAP`X", "MAP`Y", "MAP`Z" },
				warnings = new[] { "room-desc", "exit-oneway" }
			}))).IsNull();
			await Assert.That(definition.ValidateConfig(1, System.Text.Json.JsonSerializer.SerializeToElement(new
			{
				title = "East",
				rooms = new[] { 10 },
				coordinates = new[] { "X", "Y" }
			}))).IsNotNull();
		}
		finally { PluginPackageFixture.Delete(directories); }
	}

	internal static PackageManifest Manifest(Dictionary<string, byte[]> files)
	{
		var yaml = "package: graph-proof\nversion: 1.0.0\nkind: ui\nbinaries:\n  min_server_version: \">=3.0\"\n  files:\n"
			+ string.Concat(files.Select(f => $"    - file: {f.Key}\n      sha256: {PluginPackageFixture.Sha256Of(f.Value)}\n"));
		return new PackageManifestService().ParseManifest(yaml) switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			_ => throw new InvalidOperationException("UI fixture manifest did not parse.")
		};
	}

	private sealed class Bytes(Dictionary<string, byte[]> files) : IPackageFileSource
	{
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(files.GetValueOrDefault(path));
	}
}
