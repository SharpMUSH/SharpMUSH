using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <see cref="GameFeatureService"/>: what the portal is told the game has is what the game has installed,
/// and the setup wizard's choice is applied to the package manager and survives a restart.
/// </summary>
public class GameFeatureServiceTests
{
	private sealed class ScenePlugin : PluginBase
	{
		public override string Id => "scene";
	}

	/// <summary>Expanded server data in memory, by type name, as the store keys it.</summary>
	private sealed class InMemoryServerData : IExpandedObjectDataService
	{
		private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);

		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
			=> ValueTask.FromResult(_data.TryGetValue(typeof(T).Name, out var value) ? (T?)value : null);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
		{
			_data[typeof(T).Name] = data;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class Game
	{
		public HashSet<string> Installed { get; } = new(StringComparer.OrdinalIgnoreCase);
		public IPackageRegistryService Registry { get; } = Substitute.For<IPackageRegistryService>();
		public IPackageInstallService Installer { get; } = Substitute.For<IPackageInstallService>();
		public IBundledPackageBootstrap Bundled { get; } = Substitute.For<IBundledPackageBootstrap>();
		public InMemoryServerData ServerData { get; } = new();

		public Game(params string[] installed)
		{
			Installed.UnionWith(installed);
			Registry.GetInstalledPackageAsync(Arg.Any<string>()).Returns(call =>
				Installed.Contains(call.Arg<string>())
					? new Found<InstalledPackageRecord>(Record(call.Arg<string>()))
					: new Found<InstalledPackageRecord>(new NotFound()));
			Installer.UninstallAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				Installed.Remove(call.Arg<string>());
				return new Result<Success>(new Success());
			});
			Bundled.InstallBundledAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				Installed.UnionWith(call.Arg<IReadOnlyCollection<string>>());
				return (IReadOnlyList<string>)call.Arg<IReadOnlyCollection<string>>().ToList();
			});
		}

		public GameFeatureService Features(bool scenePlugin = true) => new(Registry, Installer, Bundled, ServerData,
			scenePlugin ? PluginCatalog.ForPlugins([new ScenePlugin()]) : PluginCatalog.Empty(),
			NullLogger<GameFeatureService>.Instance);

		private static InstalledPackageRecord Record(string id) =>
			new(id, "1.0.0", BundledPackageSource.SourceRepo, null, BundledPackageSource.SourceCommit, null,
				DateTimeOffset.UnixEpoch, 1);
	}

	[Test]
	public async Task AnApplicationIsOn_ExactlyWhenItsPackageIsInstalled()
	{
		var game = new Game("scene");

		await Assert.That(await game.Features().EnabledAsync()).IsEquivalentTo([GameFeatures.Scenes]);
	}

	/// <summary>
	/// The scene package without the plugin it calls into gives the portal pages with nothing behind them,
	/// so the application is neither on nor offered.
	/// </summary>
	[Test]
	public async Task WithoutItsPlugin_AnApplicationIsNeitherOnNorAvailable()
	{
		var game = new Game("scene");
		var features = game.Features(scenePlugin: false);

		await Assert.That(await features.EnabledAsync()).IsEmpty();
		var scenes = (await features.ApplicationsAsync()).Single(a => a.Id == GameFeatures.Scenes);
		await Assert.That(scenes.Available).IsFalse();
	}

	[Test]
	public async Task Apply_InstallsWhatWasChosen_AndRemovesTheRest()
	{
		var game = new Game("scene");
		var features = game.Features();

		var result = await features.ApplyAsync([GameFeatures.WikiReader], CancellationToken.None);

		await Assert.That(result is Success).IsTrue();
		await Assert.That(game.Installed).IsEquivalentTo(["wiki-reader"]);
		await Assert.That(await features.EnabledAsync()).IsEquivalentTo([GameFeatures.WikiReader]);
	}

	/// <summary>
	/// Bootstrap installs the first-boot packages into any game without them, so removing the Scene System
	/// has to be recorded, or the next restart puts it back. Turning it on again clears the record.
	/// </summary>
	[Test]
	public async Task TurningOffAFirstBootApplication_IsRemembered_UntilItIsTurnedBackOn()
	{
		var game = new Game("scene");
		var features = game.Features();

		await features.ApplyAsync([], CancellationToken.None);
		await Assert.That((await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds)
			.IsEquivalentTo(["scene"]);

		await features.ApplyAsync([GameFeatures.Scenes], CancellationToken.None);
		await Assert.That((await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds)
			.IsEmpty();
	}

	/// <summary>The wiki reader never installs at first boot, so there is nothing to remember about it.</summary>
	[Test]
	public async Task TurningOffAnApplicationBootstrapNeverInstalls_RecordsNothing()
	{
		var game = new Game("wiki-reader");

		await game.Features().ApplyAsync([GameFeatures.Scenes], CancellationToken.None);

		await Assert.That(await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>()).IsNull();
	}

	[Test]
	public async Task AnUnknownApplication_IsRefused_BeforeAnythingChanges()
	{
		var game = new Game("scene");

		var result = await game.Features().ApplyAsync(["chargen"], CancellationToken.None);

		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That(game.Installed).IsEquivalentTo(["scene"]);
	}

	[Test]
	public async Task AFailedRemoval_IsReported_AndNotRecordedAsOff()
	{
		var game = new Game("scene");
		game.Installer.UninstallAsync("scene", Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(new Result<Success>(new Error<string>("dependents exist")));

		var result = await game.Features().ApplyAsync([], CancellationToken.None);

		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That(await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>()).IsNull();
	}

	[Test]
	public async Task TheWizard_IsPendingOnlyOnceSomethingSaysSo()
	{
		var features = new Game().Features();

		await Assert.That(await features.WizardPendingAsync()).IsFalse()
			.Because("a game claimed before the wizard existed has nothing pending");
		await features.SetWizardPendingAsync(true);
		await Assert.That(await features.WizardPendingAsync()).IsTrue();
		await features.SetWizardPendingAsync(false);
		await Assert.That(await features.WizardPendingAsync()).IsFalse();
	}
}
