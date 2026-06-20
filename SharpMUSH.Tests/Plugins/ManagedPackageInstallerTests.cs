using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using PM = SharpMUSH.Implementation.Services.PluginManager;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Phase 4 (package-manager DLL distribution). Pure loader/installer tests — no
/// DB, no server host. They reuse the already-built <c>CommandOnlyPlugin.dll</c>
/// fixture as the carried managed binary: a managed package.yaml is authored over
/// its real SHA-256, deposited through <see cref="ManagedPackageInstaller"/> into a
/// scratch plugins root, and the deposit + trust gate + hash verification + a
/// loader-discovery check (the proxy for "loads on next boot", since rebooting a
/// second server to prove load is heavy) are asserted directly.
/// </summary>
[NotInParallel]
public class ManagedPackageInstallerTests
{
	private static string CommandOnlyDllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "command-only", "CommandOnlyPlugin.dll");

	private const string PackageId = "managed-sample";

	private static string Sha256Of(string path) =>
		Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

	/// <summary>A binary source backed by a real directory (the package source dir).</summary>
	private sealed class DirectoryBinarySource(string directory) : IManagedPackageBinarySource
	{
		public async Task<byte[]?> ReadBinaryAsync(string fileName, CancellationToken cancellationToken = default)
		{
			var path = Path.Combine(directory, fileName);
			return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
		}
	}

	private static (string SourceDir, string DllSha) StageSource()
	{
		var sourceDir = Path.Combine(Path.GetTempPath(), $"mpi-src-{Guid.NewGuid():N}");
		Directory.CreateDirectory(sourceDir);
		File.Copy(CommandOnlyDllPath, Path.Combine(sourceDir, "CommandOnlyPlugin.dll"));
		return (sourceDir, Sha256Of(CommandOnlyDllPath));
	}

	private static string ManifestYaml(string dllSha) =>
		$"""
		package: {PackageId}
		version: "1.0.0"
		kind: managed
		binaries:
		  min_server_version: ">=1.0"
		  files:
		    - file: CommandOnlyPlugin.dll
		      sha256: {dllSha}
		""";

	private static PackageManifest Parse(string yaml) => new PackageManifestService().ParseManifest(yaml).AsT0.Manifest;

	private static ManagedPackageInstaller NewInstaller(string pluginsRoot, ManagedPackageTrustOptions trust)
	{
		// A real PluginManager with an empty catalog: UnloadAsync returns an error for
		// the (not-loaded) package, which RemoveAsync tolerates while still deleting the dir.
		var manager = new PM(
			PluginCatalog.Empty(), [], [],
			EmptyProvider, NullLogger<PM>.Instance);
		return new ManagedPackageInstaller(manager, trust, NullLogger<ManagedPackageInstaller>.Instance, pluginsRoot);
	}

	private static readonly IServiceProvider EmptyProvider = new EmptyServiceProviderImpl();

	private sealed class EmptyServiceProviderImpl : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	private static PackageApplyRequest Request(bool allow) => new(
		new PackageApplySource("repo", "managed-sample", "commit", "main"),
		new Dictionary<string, string>(), [], 10, AllowManagedCode: allow);

	[Test]
	public async Task FixtureDll_Exists()
	{
		await Assert.That(File.Exists(CommandOnlyDllPath)).IsTrue()
			.Because($"the CommandOnlyPlugin fixture DLL is reused as the carried managed binary at {CommandOnlyDllPath}");
	}

	[Test]
	public async Task Manifest_ManagedKind_ParsesBinaries()
	{
		var (sourceDir, sha) = StageSource();
		try
		{
			var manifest = Parse(ManifestYaml(sha));
			await Assert.That(manifest.Kind).IsEqualTo(PackageKind.Managed);
			await Assert.That(manifest.Binary).IsNotNull();
			await Assert.That(manifest.Binary!.Files.Count).IsEqualTo(1);
			await Assert.That(manifest.Binary.Files[0].FileName).IsEqualTo("CommandOnlyPlugin.dll");
			await Assert.That(manifest.Binary.Files[0].Sha256).IsEqualTo(sha);
		}
		finally
		{
			Directory.Delete(sourceDir, true);
		}
	}

	[Test]
	public async Task Manifest_ManagedKind_RejectsObjectsBlock()
	{
		var result = new PackageManifestService().ParseManifest(
			"""
			package: bad-managed
			version: "1.0.0"
			kind: managed
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
		await Assert.That(result.IsT1).IsTrue().Because("a managed package may not declare softcode objects");
	}

	[Test]
	public async Task Deploy_WithTrust_DepositsVerifiedDll_AndIsLoaderDiscoverable()
	{
		var (sourceDir, sha) = StageSource();
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpi-plugins-{Guid.NewGuid():N}");
		try
		{
			var installer = NewInstaller(pluginsRoot, new ManagedPackageTrustOptions(false, [PackageId]));
			var result = await installer.DeployAsync(
				Parse(ManifestYaml(sha)), Request(allow: true), new DirectoryBinarySource(sourceDir));

			await Assert.That(result.IsT0).IsTrue().Because("trust opt-in + allow-list + matching hash should deploy");
			await Assert.That(result.AsT0).Contains("CommandOnlyPlugin.dll");

			var depositedDll = Path.Combine(pluginsRoot, PackageId, "CommandOnlyPlugin.dll");
			await Assert.That(File.Exists(depositedDll)).IsTrue();
			await Assert.That(Sha256Of(depositedDll)).IsEqualTo(sha)
				.Because("the deposited bytes must match the carried binary's hash");

			// "Loads on next boot" proxy: PluginLoaderService.Discover finds it under the plugins root.
			var discovered = PluginLoaderService.Discover(pluginsRoot, NullLogger.Instance).ToList();
			await Assert.That(discovered.Any(c =>
				Path.GetFileName(c.DllPath) == "CommandOnlyPlugin.dll"
				&& Path.GetFileName(Path.GetDirectoryName(c.DllPath)) == PackageId)).IsTrue()
				.Because("the deposited DLL must sit where the plugin loader scans on boot");
		}
		finally
		{
			Directory.Delete(sourceDir, true);
			if (Directory.Exists(pluginsRoot)) Directory.Delete(pluginsRoot, true);
		}
	}

	[Test]
	public async Task Deploy_WithoutTrustOptIn_IsRefused_AndWritesNothing()
	{
		var (sourceDir, sha) = StageSource();
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpi-plugins-{Guid.NewGuid():N}");
		try
		{
			var installer = NewInstaller(pluginsRoot, new ManagedPackageTrustOptions(true, []));
			var result = await installer.DeployAsync(
				Parse(ManifestYaml(sha)), Request(allow: false), new DirectoryBinarySource(sourceDir));

			await Assert.That(result.IsT1).IsTrue().Because("no per-apply opt-in must refuse a managed install");
			await Assert.That(Directory.Exists(Path.Combine(pluginsRoot, PackageId))).IsFalse()
				.Because("a refused install must not write any binaries");
		}
		finally
		{
			Directory.Delete(sourceDir, true);
			if (Directory.Exists(pluginsRoot)) Directory.Delete(pluginsRoot, true);
		}
	}

	[Test]
	public async Task Deploy_NotOnAllowList_IsRefused()
	{
		var (sourceDir, sha) = StageSource();
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpi-plugins-{Guid.NewGuid():N}");
		try
		{
			var installer = NewInstaller(pluginsRoot, new ManagedPackageTrustOptions(false, ["some-other-package"]));
			var result = await installer.DeployAsync(
				Parse(ManifestYaml(sha)), Request(allow: true), new DirectoryBinarySource(sourceDir));

			await Assert.That(result.IsT1).IsTrue().Because("a package absent from the server allow-list must be refused");
			await Assert.That(Directory.Exists(Path.Combine(pluginsRoot, PackageId))).IsFalse();
		}
		finally
		{
			Directory.Delete(sourceDir, true);
			if (Directory.Exists(pluginsRoot)) Directory.Delete(pluginsRoot, true);
		}
	}

	[Test]
	public async Task Deploy_HashMismatch_IsRejected_AndWritesNothing()
	{
		var (sourceDir, _) = StageSource();
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpi-plugins-{Guid.NewGuid():N}");
		try
		{
			var wrongSha = new string('a', 64);
			var installer = NewInstaller(pluginsRoot, new ManagedPackageTrustOptions(true, []));
			var result = await installer.DeployAsync(
				Parse(ManifestYaml(wrongSha)), Request(allow: true), new DirectoryBinarySource(sourceDir));

			await Assert.That(result.IsT1).IsTrue().Because("a SHA-256 mismatch must reject the deploy");
			await Assert.That(result.AsT1.Value).Contains("SHA-256 mismatch");
			await Assert.That(Directory.Exists(Path.Combine(pluginsRoot, PackageId))).IsFalse();
		}
		finally
		{
			Directory.Delete(sourceDir, true);
			if (Directory.Exists(pluginsRoot)) Directory.Delete(pluginsRoot, true);
		}
	}

	[Test]
	public async Task Deploy_MinServerVersionTooNew_IsRejected()
	{
		var (sourceDir, sha) = StageSource();
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpi-plugins-{Guid.NewGuid():N}");
		try
		{
			var yaml = $"""
				package: {PackageId}
				version: "1.0.0"
				kind: managed
				binaries:
				  min_server_version: ">=99.0"
				  files:
				    - file: CommandOnlyPlugin.dll
				      sha256: {sha}
				""";
			var installer = NewInstaller(pluginsRoot, new ManagedPackageTrustOptions(true, []));
			var result = await installer.DeployAsync(
				Parse(yaml), Request(allow: true), new DirectoryBinarySource(sourceDir));

			await Assert.That(result.IsT1).IsTrue().Because("a future min_server_version must refuse the install");
			await Assert.That(Directory.Exists(Path.Combine(pluginsRoot, PackageId))).IsFalse();
		}
		finally
		{
			Directory.Delete(sourceDir, true);
			if (Directory.Exists(pluginsRoot)) Directory.Delete(pluginsRoot, true);
		}
	}

	[Test]
	public async Task Remove_DeletesDepositedDirectory()
	{
		var (sourceDir, sha) = StageSource();
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpi-plugins-{Guid.NewGuid():N}");
		try
		{
			var installer = NewInstaller(pluginsRoot, new ManagedPackageTrustOptions(true, []));
			var deploy = await installer.DeployAsync(
				Parse(ManifestYaml(sha)), Request(allow: true), new DirectoryBinarySource(sourceDir));
			await Assert.That(deploy.IsT0).IsTrue();
			await Assert.That(Directory.Exists(Path.Combine(pluginsRoot, PackageId))).IsTrue();

			var removed = await installer.RemoveAsync(PackageId, deploy.AsT0);
			await Assert.That(removed.IsT0).IsTrue().Because("uninstall removes the deposited directory");
			await Assert.That(Directory.Exists(Path.Combine(pluginsRoot, PackageId))).IsFalse();
		}
		finally
		{
			Directory.Delete(sourceDir, true);
			if (Directory.Exists(pluginsRoot)) Directory.Delete(pluginsRoot, true);
		}
	}
}
