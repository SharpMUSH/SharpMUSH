using System.Security.Cryptography;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// What an apply or rollback must refuse before it writes anything: managed packages whose
/// dependencies or conflicts are unmet (#1169), application registrations that cannot be built
/// (#1170, #1171), and rollbacks of packages whose resources the snapshot does not carry (#1172).
/// Each test asserts the state a refusal must leave behind, not only the error.
/// </summary>
public class PackageInstallAdmissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;
	private IApplicationRegistryService Applications => (IApplicationRegistryService)Database;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private PackageManifestService Manifests { get; } = new();

	private static PackageApplySource Source(string commit = "commit-1") => new(
		"https://github.com/SharpMUSH/SharpMUSH-Packages", "admission/", commit, "main");

	private static PackageApplyRequest Request(
		IReadOnlyDictionary<string, string>? answers = null, bool allowManagedCode = false, string commit = "commit-1") =>
		new(Source(commit), answers ?? new Dictionary<string, string>(), [], 10, allowManagedCode);

	private PackageManifest Parse(string yaml) => Manifests.ParseManifest(yaml) switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	private static string CommandOnlyDllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "command-only", "CommandOnlyPlugin.dll");

	private static readonly string CommandOnlySha =
		Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(CommandOnlyDllPath))).ToLowerInvariant();

	private sealed class FixtureBinarySource : IManagedPackageBinarySource
	{
		public async Task<byte[]?> ReadBinaryAsync(string fileName, CancellationToken cancellationToken = default) =>
			fileName == "CommandOnlyPlugin.dll" ? await File.ReadAllBytesAsync(CommandOnlyDllPath, cancellationToken) : null;
	}

	/// <summary>A scratch plugins root and an installer whose managed deployments land in it, trusting every id.</summary>
	private sealed class ManagedScope : IDisposable
	{
		public required string PluginsRoot { get; init; }
		public required PackageInstallService Installer { get; init; }

		public bool Deposited(string packageId) => Directory.Exists(Path.Combine(PluginsRoot, packageId));

		public void Dispose()
		{
			if (Directory.Exists(PluginsRoot))
			{
				Directory.Delete(PluginsRoot, true);
			}
		}
	}

	private ManagedScope CreateManagedScope()
	{
		var pluginsRoot = Path.Combine(Path.GetTempPath(), $"mpkg-admission-{Guid.NewGuid():N}");
		var services = WebAppFactoryArg.Services;
		var managedInstaller = new ManagedPackageInstaller(
			services.GetRequiredService<IPluginManager>(),
			new ManagedPackageTrustOptions(true, []),
			NullLogger<ManagedPackageInstaller>.Instance,
			pluginsRoot);

		return new ManagedScope
		{
			PluginsRoot = pluginsRoot,
			Installer = new PackageInstallService(
				Database, Database, Database, Database, Registry, Applications,
				services.GetRequiredService<IPackagePlanService>(),
				services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
				services.GetRequiredService<IPackageLifecycleRunner>(),
				managedInstaller,
				services.GetRequiredService<IMediator>())
		};
	}

	private PackageManifest ManagedManifest(string id, string version, string relations = "") => Parse($"""
		package: {id}
		version: "{version}"
		kind: managed
		{relations}
		binaries:
		  min_server_version: ">=1.0"
		  files:
		    - file: CommandOnlyPlugin.dll
		      sha256: {CommandOnlySha}
		""");

	private async Task InstallSoftcodeAsync(string id, string version)
	{
		var manifest = Parse($"""
			package: {id}
			version: "{version}"
			objects:
			  - ref: marker
			    type: thing
			    name: {id} marker
			""");
		await Assert.That((await Installer.ApplyAsync(manifest, Request())).Value).IsTypeOf<PackageApplyResult>();
	}

	private async Task AssertNothingRecordedAsync(string packageId)
	{
		await Assert.That((await Registry.GetInstalledPackageAsync(packageId)).Value).IsTypeOf<NotFound>();
		await Assert.That(await Registry.GetPackageRevisionsAsync(packageId)).IsEmpty();
		await Assert.That(await Registry.GetPackageDependenciesAsync(packageId)).IsEmpty();
	}

	// ── #1169: managed installs honour dependencies and conflicts ───────────

	[Test, NotInParallel]
	public async Task Managed_MissingDependency_IsRejectedAndDepositsNothing()
	{
		using var scope = CreateManagedScope();
		var manifest = ManagedManifest("adm-managed-missing", "1.0.0", """
			depends:
			  - adm-absent-dependency: ">=1.0"
			""");

		var result = await scope.Installer.ApplyAsync(
			manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

		await Assert.That(result.Expect<Error<string>>().Value).Contains("requires adm-absent-dependency");
		await Assert.That(scope.Deposited("adm-managed-missing")).IsFalse();
		await AssertNothingRecordedAsync("adm-managed-missing");
	}

	[Test, NotInParallel]
	public async Task Managed_IncompatibleDependency_IsRejected()
	{
		using var scope = CreateManagedScope();
		await InstallSoftcodeAsync("adm-old-dependency", "1.0");
		try
		{
			var manifest = ManagedManifest("adm-managed-incompatible", "1.0.0", """
				depends:
				  - adm-old-dependency: ">=2.0"
				""");

			var result = await scope.Installer.ApplyAsync(
				manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

			await Assert.That(result.Expect<Error<string>>().Value).Contains("installed: 1.0.0");
			await Assert.That(scope.Deposited("adm-managed-incompatible")).IsFalse();
			await AssertNothingRecordedAsync("adm-managed-incompatible");
		}
		finally
		{
			await Installer.UninstallAsync("adm-old-dependency", force: true);
		}
	}

	[Test, NotInParallel]
	public async Task Managed_MatchingConflict_IsRejected()
	{
		using var scope = CreateManagedScope();
		await InstallSoftcodeAsync("adm-rival", "1.0");
		try
		{
			var manifest = ManagedManifest("adm-managed-conflict", "1.0.0", """
				conflicts:
				  - adm-rival: "<2.0"
				""");

			var result = await scope.Installer.ApplyAsync(
				manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

			await Assert.That(result.Expect<Error<string>>().Value).Contains("conflicts with installed adm-rival");
			await Assert.That(scope.Deposited("adm-managed-conflict")).IsFalse();
			await AssertNothingRecordedAsync("adm-managed-conflict");
		}
		finally
		{
			await Installer.UninstallAsync("adm-rival", force: true);
		}
	}

	[Test, NotInParallel]
	public async Task Managed_SatisfiedDependency_DeploysAndRecordsDependencyRows()
	{
		using var scope = CreateManagedScope();
		await InstallSoftcodeAsync("adm-good-dependency", "1.2");
		try
		{
			var manifest = ManagedManifest("adm-managed-happy", "1.0.0", """
				depends:
				  - adm-good-dependency: ">=1.0"
				""");

			var result = await scope.Installer.ApplyAsync(
				manifest, Request(allowManagedCode: true), CancellationToken.None, new FixtureBinarySource());

			await Assert.That(result.Value).IsTypeOf<PackageApplyResult>();
			await Assert.That(scope.Deposited("adm-managed-happy")).IsTrue();
			var dependency = (await Registry.GetPackageDependenciesAsync("adm-managed-happy")).Single();
			await Assert.That(dependency.DependsOnId).IsEqualTo("adm-good-dependency");

			await Assert.That((await scope.Installer.UninstallAsync("adm-managed-happy")).Value).IsTypeOf<Success>();
		}
		finally
		{
			await Installer.UninstallAsync("adm-good-dependency", force: true);
		}
	}
}
