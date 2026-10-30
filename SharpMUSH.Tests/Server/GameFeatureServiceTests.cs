using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <see cref="GameFeatureService"/>: what the portal is told the game has is what the game has installed,
/// and the setup wizard's choice of bundled packages is applied to the package manager, dependencies first,
/// and survives a restart.
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
		public List<string> Installed { get; } = [];
		public List<string> Calls { get; } = [];
		public IPackageRegistryService Registry { get; } = Substitute.For<IPackageRegistryService>();
		public IPackageInstallService Installer { get; } = Substitute.For<IPackageInstallService>();
		public IBundledPackageBootstrap Bundled { get; } = Substitute.For<IBundledPackageBootstrap>();
		public InMemoryServerData ServerData { get; } = new();
		public uint? HttpHandler { get; set; } = 8;
		public uint? EventHandler { get; set; } = 9;

		public Game(params string[] installed)
		{
			Installed.AddRange(installed);
			Registry.GetInstalledPackageAsync(Arg.Any<string>()).Returns(call =>
				Installed.Contains(call.Arg<string>())
					? new Found<InstalledPackageRecord>(Record(call.Arg<string>()))
					: new Found<InstalledPackageRecord>(new NotFound()));
			Installer.UninstallAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				Calls.Add($"-{call.Arg<string>()}");
				Installed.Remove(call.Arg<string>());
				return new Result<Success>(new Success());
			});
			Bundled.InstallBundledAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				var ids = call.Arg<IReadOnlyCollection<string>>().ToList();
				Calls.AddRange(ids.Select(id => $"+{id}"));
				Installed.AddRange(ids);
				return (IReadOnlyList<string>)ids;
			});
		}

		public GameFeatureService Features(bool scenePlugin = true)
		{
			var options = SharpMUSHOptions.Default();
			var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			wrapper.CurrentValue.Returns(_ => options with
			{
				Database = options.Database with { HttpHandler = HttpHandler, EventHandler = EventHandler }
			});

			return new GameFeatureService(Registry, Installer, new PackageManifestService(), Bundled, ServerData, wrapper,
				scenePlugin ? PluginCatalog.ForPlugins([new ScenePlugin()]) : PluginCatalog.Empty(),
				NullLogger<GameFeatureService>.Instance);
		}

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
		var scene = (await features.PackagesAsync()).Single(p => p.Id == "scene");
		await Assert.That(scene.Available).IsFalse();
	}

	/// <summary>The wizard groups packages by the category their manifests give.</summary>
	[Test]
	public async Task EachPackage_CarriesItsManifestsCategory()
	{
		var packages = await new Game().Features().PackagesAsync();

		await Assert.That(packages.Single(p => p.Id == "jobs").Category).IsEqualTo("Staff");
		await Assert.That(packages.Single(p => p.Id == "jobs-app").Category).IsEqualTo("Staff");
	}

	/// <summary>
	/// A bridge the wizard is not asked for while both of its halves stay is turned off, so the halves arriving do not
	/// bring it in; one removed with a half it joins is not, so putting that half back brings it back.
	/// </summary>
	[Test]
	public async Task ABridgeLeftOut_IsTurnedOff_UnlessItsHalfGoesToo()
	{
		var game = new Game("plus-help", "plus-config", "summary", "summary-help", "jobs", "jobs-help");

		await game.Features().ApplyPackagesAsync(["plus-help", "plus-config", "summary"], CancellationToken.None);

		var declined = (await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>())?.PackageIds ?? [];
		await Assert.That(declined).Contains("summary-help");
		await Assert.That(declined).Contains("summary-config");
		await Assert.That(declined).DoesNotContain("jobs-help").Because("jobs went, and jobs-help with it");
		await Assert.That(game.Installed).DoesNotContain("jobs-help");
	}

	/// <summary>
	/// Every bundled package that calls the Scene plugin names it in <c>plugins:</c>, so without the plugin none of
	/// them is offered, and the packages that do not need it still are.
	/// </summary>
	[Test]
	public async Task WithoutTheScenePlugin_NoPackageOverItIsAvailable()
	{
		var packages = await new Game().Features(scenePlugin: false).PackagesAsync();

		// The Discord and World plugins are not loaded here either; their packages are someone else's test.
		await Assert.That(packages.Where(p => !p.Available && !p.Id.StartsWith("discord", StringComparison.Ordinal)
				&& !p.Id.StartsWith("world-grid", StringComparison.Ordinal)).Select(p => p.Id))
			.IsEquivalentTo(["scene", "scene-help", "scene-config", "scene-verse", "scene-safety", "scene-safety-help", "scene-safety-config",
				"radio-scene", "timeline", "timeline-help", "timeline-config", "highlights", "highlights-app", "highlights-config",
				"scene-availability", "scene-here", "relationships-scene"])
			.Because("highlights-app and the scene bridges call no plugin themselves, but the packages they join do");
		await Assert.That(new Game().Features().PackagesOver("scene"))
			.IsEquivalentTo(["scene", "radio-scene", "scene-verse", "scene-safety", "timeline", "highlights", "relationships-scene", "discord-scenes", "scene-availability"]);
	}

	/// <summary>
	/// Asking the wizard for a package over a plugin that is not running reports it by name and installs nothing over
	/// the plugin, not even the package it depends on that needs the plugin too.
	/// </summary>
	[Test]
	public async Task Asking_ForAPackageOverAMissingPlugin_IsReported_NotInstalled()
	{
		var game = new Game();

		var result = await game.Features(scenePlugin: false).ApplyPackagesAsync(["scene-help"], CancellationToken.None);

		await Assert.That(result is Error<string> { Value: var message } && message.Contains("scene-help needs the scene plugin")).IsTrue()
			.Because("a bridge names no plugin itself; it needs the ones of the packages it joins");
		await Assert.That(game.Installed).IsEquivalentTo(["plus-help"])
			.Because("+help needs no plugin; scene, and the bridge joining it to +help, need the Scene plugin");
	}

	/// <summary>The offer is every bundled package, with what it attaches to and needs, from its manifest.</summary>
	[Test]
	public async Task ThePackages_AreEveryBundledOne_WithTheirHandlerAndDependencies()
	{
		var packages = await new Game("http-handler").Features().PackagesAsync();

		await Assert.That(packages.Select(p => p.Id)).IsEquivalentTo(BundledPackages.All.Select(p => p.PackageId));
		var scene = packages.Single(p => p.Id == "scene");
		await Assert.That(scene.DependsOn).IsEmpty()
			.Because("scene-help and scene-config join it to +help and +config");
		await Assert.That(packages.Single(p => p.Id == "scene-help").DependsOn).IsEquivalentTo(["scene", "plus-help"]);
		await Assert.That(scene.Description).IsNotEmpty();
		await Assert.That(packages.Single(p => p.Id == "profile-handler").Requires).IsEqualTo(HandlerKinds.Http);
		await Assert.That(packages.Single(p => p.Id == "room-contents").Requires).IsEqualTo(HandlerKinds.Event);
		await Assert.That(packages.Single(p => p.Id == "http-handler").Installed).IsTrue();
	}

	/// <summary>
	/// What a new game has is recommended — the first-boot packages — unless the administrator turned it off,
	/// so a game that lost them to an import is offered them back, and one that chose against them is not.
	/// </summary>
	[Test]
	public async Task TheFirstBootPackages_AreRecommended_UnlessTurnedOff()
	{
		var game = new Game();
		await game.ServerData.SetExpandedServerDataAsync(new DeclinedBundledPackages { PackageIds = ["scene"] });

		var packages = await game.Features().PackagesAsync();

		await Assert.That(packages.Single(p => p.Id == "profile-handler").Recommended).IsTrue();
		await Assert.That(packages.Single(p => p.Id == "scene").Recommended).IsFalse();
		await Assert.That(packages.Single(p => p.Id == "wiki-reader").Recommended).IsFalse()
			.Because("a new game does not get the wiki reader");
	}

	/// <summary>
	/// The World map is offered ticked to a new game without bootstrap installing it, and a game that removed it is not
	/// offered it again.
	/// </summary>
	[Test]
	public async Task ASuggestedPackage_IsRecommended_UntilRemoved()
	{
		var game = new Game();

		var packages = await game.Features().PackagesAsync();

		await Assert.That(packages.Single(p => p.Id == "world-map").Recommended).IsTrue();
		await Assert.That(BundledPackages.All.Single(p => p.PackageId == "world-map").InstallAtFirstBoot).IsFalse()
			.Because("an upgraded game is not given a map page it never set up");

		await game.Features().SetDeclinedAsync("world-map", declined: true);

		await Assert.That((await game.Features().PackagesAsync()).Single(p => p.Id == "world-map").Recommended).IsFalse();
	}

	[Test]
	public async Task ASuggestedPackage_UntickedBeforeInstall_IsNotOfferedAgain()
	{
		var game = new Game();

		await game.Features().ApplyPackagesAsync([], CancellationToken.None);

		await Assert.That((await game.Features().PackagesAsync()).Single(p => p.Id == "world-map").Recommended).IsFalse()
			.Because("a wizard reloaded after a failed later step must not tick it again");
	}

	[Test]
	public async Task Asking_ForAPackage_InstallsWhatItDependsOnFirst()
	{
		var game = new Game();

		var result = await game.Features().ApplyPackagesAsync(["scene-help"], CancellationToken.None);

		await Assert.That(result is Success).IsTrue();
		await Assert.That(game.Calls).IsEquivalentTo(["+plus-help", "+scene", "+scene-help"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task PackagesNotAskedFor_AreRemoved_DependentsFirst()
	{
		var game = new Game("plus-help", "scene", "wiki-reader");

		await game.Features().ApplyPackagesAsync(["wiki-reader"], CancellationToken.None);

		await Assert.That(game.Calls).IsEquivalentTo(["-scene"])
			.Because("plus-help stays: the wiki reader depends on it");
		await Assert.That(game.Installed).IsEquivalentTo(["plus-help", "wiki-reader"]);
	}

	/// <summary>
	/// A package that attaches to a handler cannot go on while the game has none: it is reported, and nothing
	/// is installed onto an object that is not there.
	/// </summary>
	[Test]
	public async Task APackageOnAMissingHandler_IsReported_NotInstalled()
	{
		var game = new Game { HttpHandler = null };

		var result = await game.Features().ApplyPackagesAsync(["profile-handler"], CancellationToken.None);

		await Assert.That(result is Error<string> { Value: var message } && message.Contains("http handler")).IsTrue();
		await Assert.That(game.Installed).IsEmpty();
	}

	/// <summary>
	/// Bootstrap installs the first-boot packages into any game without them, so removing one has to be
	/// recorded, or the next restart puts it back. Installing it again clears the record.
	/// </summary>
	[Test]
	public async Task RemovingAFirstBootPackage_IsRemembered_UntilItIsInstalledAgain()
	{
		var game = new Game("plus-help", "scene");
		var features = game.Features();

		await features.ApplyPackagesAsync(["plus-help"], CancellationToken.None);
		await Assert.That((await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds)
			.IsEquivalentTo(["scene", "world-map"]);

		await features.ApplyPackagesAsync(["plus-help", "scene", "scene-help"], CancellationToken.None);
		await Assert.That((await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds)
			.IsEquivalentTo(["world-map"]);
	}

	/// <summary>The wiki reader never installs at first boot, so there is nothing to remember about it.</summary>
	[Test]
	public async Task RemovingAPackageBootstrapNeverInstalls_RecordsNothing()
	{
		var game = new Game("plus-help", "wiki-reader");

		await game.Features().ApplyPackagesAsync(["plus-help"], CancellationToken.None);

		await Assert.That((await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds)
			.IsEquivalentTo(["world-map"]);
	}

	[Test]
	public async Task AnUnknownPackage_IsRefused_BeforeAnythingChanges()
	{
		var game = new Game("scene");

		var result = await game.Features().ApplyPackagesAsync(["no-such-package"], CancellationToken.None);

		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That(game.Calls).IsEmpty();
	}

	[Test]
	public async Task AFailedRemoval_IsReported_AndNotRecordedAsOff()
	{
		var game = new Game("scene");
		game.Installer.UninstallAsync("scene", Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(new Result<Success>(new Error<string>("dependents exist")));

		var result = await game.Features().ApplyPackagesAsync([], CancellationToken.None);

		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That((await game.ServerData.GetExpandedServerDataAsync<DeclinedBundledPackages>())!.PackageIds)
			.IsEquivalentTo(["world-map"]);
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
