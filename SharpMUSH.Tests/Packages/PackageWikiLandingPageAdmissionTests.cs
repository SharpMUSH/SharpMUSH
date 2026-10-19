using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Applications;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.Packages;

/// <summary>Programmatic managed manifests cannot bypass entrance validation and deposit executable files.</summary>
public class PackageWikiLandingPageAdmissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private sealed class CountingDeployment(IPluginPackageInstaller inner) : IPluginPackageInstaller
	{
		public int Calls { get; private set; }
		public Task<Result<IReadOnlyList<string>>> DeployAsync(PackageManifest manifest, PackageApplyRequest request,
			IPackageFileSource source, CancellationToken cancellationToken = default)
		{
			Calls++;
			return inner.DeployAsync(manifest, request, source, cancellationToken);
		}

		public Task<Result<Success>> RemoveAsync(string id, IReadOnlyList<string> files, CancellationToken cancellationToken = default)
			=> inner.RemoveAsync(id, files, cancellationToken);
	}

	private sealed class CountingSource(IPackageFileSource inner) : IPackageFileSource
	{
		public int Reads { get; private set; }
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
		{
			Reads++;
			return inner.ReadFileAsync(path, cancellationToken);
		}
	}

	private sealed class Bytes(Dictionary<string, byte[]> files) : IPackageFileSource
	{
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
			=> Task.FromResult(files.GetValueOrDefault(path));
	}

	private PackageInstallService Installer(IPluginPackageInstaller managed)
	{
		var services = WebAppFactoryArg.Services;
		var database = services.GetRequiredService<ISharpDatabase>();
		return new PackageInstallService(database, database, database, database,
			(IPackageRegistryService)database, (IApplicationRegistryService)database,
			services.GetRequiredService<IPackagePlanService>(),
			services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			services.GetRequiredService<IPackageLifecycleRunner>(), managed,
			services.GetRequiredService<IMediator>(), services.GetRequiredService<IPackageOperationGate>(),
			services.GetRequiredService<ILockService>(), services.GetRequiredService<IPackageDeclarationService>(),
			services.GetRequiredService<IPackageSettingService>(), services.GetRequiredService<IPackageWikiService>(),
			services.GetRequiredService<Lazy<IRuntimeRegistrationService>>(), services.GetRequiredService<EngineVersion>(),
			services.GetRequiredService<Lazy<IPluginManager>>(), services.GetRequiredService<Lazy<IPluginAdministration>>());
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task InvalidManagedEntranceIsRefusedBeforeDeploymentOrFileReads(bool ui)
	{
		var id = "landing-admission-" + Guid.NewGuid().ToString("N")[..12];
		var directories = PluginPackageFixture.ScratchDirectories();
		PackageInstallService? cleanup = null;
		try
		{
			PackageManifest manifest;
			IPackageFileSource payload;
			if (ui)
			{
				var files = new Dictionary<string, byte[]>
				{
					["ui.json"] = Encoding.UTF8.GetBytes(UiDefinitionTests.DefinitionJson.Replace("graph-proof", id)),
					["index.html"] = Encoding.UTF8.GetBytes("<!doctype html><h1>Instructions</h1>")
				};
				manifest = UiPackageDeploymentTests.Manifest(files) with { Name = id };
				payload = new Bytes(files);
			}
			else
			{
				manifest = new PackageManifestService().ParseManifest(PluginPackageFixture.Yaml(id)).Expect<ParsedPackageManifest>().Manifest;
				payload = new PluginPackageFixture.BinarySource(id);
			}

			var request = new PackageApplyRequest(
				new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", id + "/", "commit-1", "main"),
				new Dictionary<string, string>(), [], AllowPluginCode: true);
			var realDeployment = new PluginPackageInstaller(WebAppFactoryArg.Services.GetRequiredService<IPluginManager>(),
				PluginPackageFixture.Allowed, directories, NullLogger<PluginPackageInstaller>.Instance);
			// Establish that this exact payload and trust configuration actually deploy. A different
			// validation error must not stand in for the entrance gate below.
			var deposited = (await realDeployment.DeployAsync(manifest, request, payload)).Expect<IReadOnlyList<string>>();
			(await realDeployment.RemoveAsync(id, deposited)).Expect<Success>();
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, id))).IsFalse();
			await Assert.That(Directory.Exists(Path.Combine(directories.UiPackages, id))).IsFalse();

			var deployment = new CountingDeployment(realDeployment);
			var source = new CountingSource(payload);
			var invalid = manifest with
			{
				Format = new PackageFormatVersion(1, 14),
				WikiLandingPage = new PackageWikiLandingPageSpec(WikiNamespace.Main, id + "_start")
			};
			cleanup = Installer(deployment);
			var result = await cleanup.ApplyAsync(invalid, request, fileSource: source);
			await Assert.That(result.Expect<Error<string>>().Value).Contains("wiki_landing_page");
			await Assert.That(deployment.Calls).IsEqualTo(0);
			await Assert.That(source.Reads).IsEqualTo(0);
			await Assert.That(Directory.Exists(Path.Combine(directories.Installed, id))).IsFalse();
			await Assert.That(Directory.Exists(Path.Combine(directories.UiPackages, id))).IsFalse();
			var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
			await Assert.That(await registry.GetInstalledPackageAsync(id) is NotFound).IsTrue();
			await Assert.That(await registry.GetPackageRevisionsAsync(id)).IsEmpty();
			await Assert.That(await registry.GetPackageWikiPagesAsync(id)).IsEmpty();
		}
		finally
		{
			try
			{
				if (cleanup is not null && await ((IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>())
					.GetInstalledPackageAsync(id) is InstalledPackageRecord)
					(await cleanup.UninstallAsync(id)).Expect<Success>();
			}
			finally { PluginPackageFixture.Delete(directories); }
		}
	}
}
