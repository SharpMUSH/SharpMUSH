using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// First-boot bootstrap and the bridges: scene-help and scene-config install at first boot, but only where both of
/// the packages they join are. A game whose administrator turned scene off gets neither, and bootstrap does not try.
/// Here rather than in SharpMUSH.Tests, whose module initialiser turns bootstrap off for the whole process.
/// </summary>
public class BundledBridgeBootstrapTests
{
	private sealed class ScenePlugin : PluginBase
	{
		public override string Id => "scene";
	}

	private sealed class ServerData(DeclinedBundledPackages declined) : IExpandedObjectDataService
	{
		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class => ValueTask.FromResult(declined as T);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();
	}

	[Test]
	public async Task ABridge_WaitsForBothOfThePackagesItJoins()
	{
		string[] installed = ["plus-help", "plus-config", "messages"];
		var registry = Substitute.For<IPackageRegistryService>();
		registry.GetInstalledPackageAsync(Arg.Any<string>()).Returns(call =>
			installed.Contains(call.Arg<string>())
				? new Found<InstalledPackageRecord>(new InstalledPackageRecord(call.Arg<string>(), "99.0.0",
					BundledPackageSource.SourceRepo, null, BundledPackageSource.SourceCommit, null, DateTimeOffset.UnixEpoch, 1))
				: new Found<InstalledPackageRecord>(new NotFound()));

		var planned = new List<string>();
		var installer = Substitute.For<IPackageInstallService>();
		installer.PlanAsync(Arg.Any<PackageManifest>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var manifest = call.Arg<PackageManifest>();
				planned.Add(manifest.Name);
				return new PackageChangeset(manifest.Name, null, "1.0.0", PackageRevisionKind.Install, [], [], [],
					[new PackageDependencyIssue("scene", "", null, null, IsConflict: false)], [], []);
			});

		var options = SharpMUSHOptions.Default();
		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(_ => options with
		{
			Database = options.Database with { HttpHandler = null, EventHandler = null }
		});

		var bootstrap = new DefaultPackagesBootstrapService(new PackageManifestService(), registry, installer, wrapper,
			new ServerData(new DeclinedBundledPackages { PackageIds = ["scene"] }), PluginCatalog.ForPlugins([new ScenePlugin()]),
			NullLogger<DefaultPackagesBootstrapService>.Instance);

		await bootstrap.StartAsync(CancellationToken.None);

		await Assert.That(planned).IsEmpty()
			.Because("scene was turned off, so scene-help and scene-config have nothing to join");
	}
}
