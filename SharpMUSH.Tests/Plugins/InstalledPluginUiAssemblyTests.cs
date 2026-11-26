using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// The server's UI-assembly provider reads what the server's plugin-package installer wrote: a package
/// carrying a browser component, deployed through the host's installer, is served by the host's provider (#2874).
/// </summary>
public class InstalledPluginUiAssemblyTests
{
	private const string UiAssembly = "Sample.Ui.dll";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private sealed class Source(string id, byte[] ui) : IPackageFileSource
	{
		private readonly PluginPackageFixture.BinarySource _plugin = new(id);

		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
			path == UiAssembly ? Task.FromResult<byte[]?>(ui) : _plugin.ReadFileAsync(path, cancellationToken);
	}

	[Test]
	public async Task InstalledPackageUiAssemblyIsServed()
	{
		var services = WebAppFactoryArg.Services;
		var installer = services.GetRequiredService<IPluginPackageInstaller>();
		var provider = services.GetRequiredService<IPluginUiAssemblyProvider>();
		var id = $"ui-served-{Guid.NewGuid():N}"[..24];
		var ui = Encoding.UTF8.GetBytes($"UI-ASSEMBLY-{id}");
		var yaml = PluginPackageFixture.Yaml(id) + $"""

		    - file: {UiAssembly}
		      sha256: {PluginPackageFixture.Sha256Of(ui)}
		""";
		var manifest = new PackageManifestService().ParseManifest(yaml) switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};
		var request = new PackageApplyRequest(new PackageApplySource("repo", id, "commit", "main"),
			new Dictionary<string, string>(), [], 10, AllowPluginCode: true);

		// The shared host's world has no installed plugins, so the folder is made here and removed after.
		var installed = services.GetRequiredService<PluginDirectories>().Installed;
		var hadInstalled = Directory.Exists(installed);
		var deployed = (await installer.DeployAsync(manifest, request, new Source(id, ui)))
			.Expect<IReadOnlyList<string>>("the package deploys");
		try
		{
			await Assert.That(deployed).Contains(UiAssembly);
			await Assert.That(File.Exists(Path.Combine(installed, id, UiAssembly))).IsTrue();

			var served = (await provider.GetVerifiedAssemblyAsync(id, UiAssembly)).Expect<byte[]>("the installed assembly is served");
			await Assert.That(served).IsEquivalentTo(ui);
		}
		finally
		{
			await installer.RemoveAsync(id, deployed);
			if (!hadInstalled && Directory.Exists(installed) && !Directory.EnumerateFileSystemEntries(installed).Any())
			{
				Directory.Delete(installed);
			}
		}
	}
}
