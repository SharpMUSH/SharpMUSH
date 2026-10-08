using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using PM = SharpMUSH.Implementation.Services.PluginManager;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// The plugin-package installer, with no database or host: a package over the CommandOnlyPlugin fixture DLL is
/// deposited into scratch plugin directories, and the gates (the host switch, the per-apply confirmation, the
/// carried plugin.json, hashes, contract version) and a loader-discovery check (the proxy for "loads at the next
/// start") are asserted directly.
/// </summary>
public class PluginPackageInstallerTests
{
	private const string PackageId = "installer-sample";

	private static PackageManifest Parse(string yaml) => new PackageManifestService().ParseManifest(yaml) switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	private static PluginPackageInstaller NewInstaller(PluginDirectories directories, bool allowed = true)
	{
		// A real PluginManager with an empty catalog: UnloadAsync returns an error for
		// the (not-loaded) package, which RemoveAsync tolerates while still deleting the dir.
		var manager = new PM(PluginCatalog.Empty(), [], [], EmptyProvider, NullLogger<PM>.Instance);
		return new PluginPackageInstaller(manager, new PluginInstallOptions(allowed), directories,
			NullLogger<PluginPackageInstaller>.Instance);
	}

	private static readonly IServiceProvider EmptyProvider = new EmptyServiceProviderImpl();

	private sealed class EmptyServiceProviderImpl : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	private static PackageApplyRequest Request(bool allow) => new(
		new PackageApplySource("repo", PackageId, "commit", "main"),
		new Dictionary<string, string>(), [], 10, AllowPluginCode: allow);

	private static Task<Result<IReadOnlyList<string>>> DeployAsync(PluginPackageInstaller installer, string yaml,
		bool allow = true, IPluginPackageBinarySource? source = null) =>
		installer.DeployAsync(Parse(yaml), Request(allow), source ?? new PluginPackageFixture.BinarySource(PackageId));

	[Test]
	public async Task FixtureDll_Exists()
	{
		await Assert.That(File.Exists(PluginPackageFixture.DllPath)).IsTrue()
			.Because($"the CommandOnlyPlugin fixture DLL is reused as the carried binary at {PluginPackageFixture.DllPath}");
	}

	[Test]
	public async Task Manifest_PluginKind_ParsesBinaries()
	{
		var manifest = Parse(PluginPackageFixture.Yaml(PackageId));
		await Assert.That(manifest.Kind).IsEqualTo(PackageKind.Plugin);
		await Assert.That(manifest.Binary).IsNotNull();
		var binary = manifest.Binary!;
		await Assert.That(binary.Files.Select(f => f.FileName)).IsEquivalentTo([PluginPackageFixture.DllName, PluginManifest.FileName]);
		await Assert.That(binary.Files[0].Sha256).IsEqualTo(PluginPackageFixture.DllSha);
	}

	[Test]
	public async Task Manifest_ManagedKind_IsNotAKind()
	{
		var result = new PackageManifestService().ParseManifest(
			"""
			package: old-kind
			version: "1.0.0"
			kind: managed
			""");
		await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>().Because("the plugin kind is spelled plugin");
	}

	[Test]
	public async Task Manifest_PluginKind_RejectsObjectsBlock()
	{
		var result = new PackageManifestService().ParseManifest(
			"""
			package: bad-plugin
			version: "1.0.0"
			kind: plugin
			binaries:
			  min_server_version: ">=1.0"
			  files:
			    - file: x.dll
			      sha256: 0000000000000000000000000000000000000000000000000000000000000000
			objects:
			  - ref: r
			    type: room
			    name: Nope
			""");
		await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>().Because("a plugin package may not declare softcode objects");
	}

	[Test]
	public async Task Deploy_Confirmed_DepositsVerifiedFiles_AndIsLoaderDiscoverable()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var result = await DeployAsync(NewInstaller(directories), PluginPackageFixture.Yaml(PackageId));

			var deployedFiles = result.Expect<IReadOnlyList<string>>("a confirmed install with matching hashes deploys");
			await Assert.That(deployedFiles).Contains(PluginPackageFixture.DllName);

			var depositedDll = Path.Combine(directories.Installed, PackageId, PluginPackageFixture.DllName);
			await Assert.That(PluginPackageFixture.Sha256Of(File.ReadAllBytes(depositedDll))).IsEqualTo(PluginPackageFixture.DllSha)
				.Because("the deposited bytes must match the carried binary's hash");

			// "Loads at the next start" proxy: the loader finds it, by its plugin.json id, under the installed root.
			var discovered = PluginLoaderService.Discover(directories.Installed, NullLogger.Instance).ToList();
			var candidate = discovered.Single();
			await Assert.That(candidate.Id).IsEqualTo(PackageId);
			await Assert.That(candidate.Problem).IsNull();
			await Assert.That(Path.GetFileName(candidate.DllPath)).IsEqualTo(PluginPackageFixture.DllName);
			await Assert.That(Directory.EnumerateDirectories(directories.Installed).Select(path => Path.GetFileName(path)!))
				.IsEquivalentTo([PackageId]).Because("the staging folder is gone once the install is in place");
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Deploy_HostSwitchedOff_IsRefused_AndWritesNothing()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var result = await DeployAsync(NewInstaller(directories, allowed: false), PluginPackageFixture.Yaml(PackageId));

			var error = result.Expect<Error<string>>("SHARPMUSH_PLUGIN_INSTALL=false refuses every plugin package");
			await Assert.That(error.Value).Contains(PluginDirectories.InstallVariable);
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, PackageId))).IsFalse();
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Deploy_NotConfirmed_IsRefused_AndWritesNothing()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var result = await DeployAsync(NewInstaller(directories), PluginPackageFixture.Yaml(PackageId), allow: false);

			await Assert.That(result.Value).IsTypeOf<Error<string>>().Because("an apply without allow_plugin_code is refused");
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, PackageId))).IsFalse()
				.Because("a refused install must not write any binaries");
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Deploy_PluginIdDiffersFromPackage_IsRefused()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			// The package is installer-sample; its plugin.json says another-id.
			var yaml = PluginPackageFixture.Yaml(PackageId).Replace(
				PluginPackageFixture.Sha256Of(PluginPackageFixture.PluginJson(PackageId)),
				PluginPackageFixture.Sha256Of(PluginPackageFixture.PluginJson("another-id")));
			var result = await DeployAsync(NewInstaller(directories), yaml, source: new PluginPackageFixture.BinarySource("another-id"));

			var error = result.Expect<Error<string>>("a plugin package's plugin id is its package id");
			await Assert.That(error.Value).Contains("another-id");
			await Assert.That(Directory.Exists(directories.Installed) && Directory.EnumerateFileSystemEntries(directories.Installed).Any()).IsFalse();
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Deploy_WithoutPluginJson_IsRefused()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var yaml = $"""
				package: {PackageId}
				version: "1.0.0"
				kind: plugin
				binaries:
				  min_server_version: ">=1.0"
				  files:
				    - file: {PluginPackageFixture.DllName}
				      sha256: {PluginPackageFixture.DllSha}
				""";
			var error = (await DeployAsync(NewInstaller(directories), yaml)).Expect<Error<string>>();
			await Assert.That(error.Value).Contains(PluginManifest.FileName);
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Deploy_TakingAShippedPluginsId_IsRefused()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			Directory.CreateDirectory(Path.Combine(directories.BuiltIn, PackageId));
			var error = (await DeployAsync(NewInstaller(directories), PluginPackageFixture.Yaml(PackageId))).Expect<Error<string>>();
			await Assert.That(error.Value).Contains("ships with this server");
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Deploy_HashMismatch_IsRejected_AndWritesNothing()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var result = await DeployAsync(NewInstaller(directories), PluginPackageFixture.Yaml(PackageId, dllSha: new string('a', 64)));

			var error = result.Expect<Error<string>>("a SHA-256 mismatch must reject the deploy");
			await Assert.That(error.Value).Contains("SHA-256 mismatch");
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, PackageId))).IsFalse();
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Deploy_MinServerVersionTooNew_IsRejected()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var result = await DeployAsync(NewInstaller(directories), PluginPackageFixture.Yaml(PackageId, minServerVersion: ">=99.0"));

			await Assert.That(result.Value).IsTypeOf<Error<string>>().Because("a future min_server_version must refuse the install");
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, PackageId))).IsFalse();
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}

	[Test]
	public async Task Remove_DeletesDepositedDirectory()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var installer = NewInstaller(directories);
			var deployedFiles = (await DeployAsync(installer, PluginPackageFixture.Yaml(PackageId))).Expect<IReadOnlyList<string>>();
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, PackageId))).IsTrue();

			var removed = await installer.RemoveAsync(PackageId, deployedFiles);
			await Assert.That(removed.Value).IsTypeOf<Success>().Because("uninstall removes the deposited directory");
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, PackageId))).IsFalse();
		}
		finally
		{
			PluginPackageFixture.Delete(directories);
		}
	}
}
