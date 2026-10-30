using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration.World;

/// <summary>
/// The bundled world-grid package over the World plugin: +map/exits opens the ten direction exits on a world map,
/// a player walks between the rooms its places are lent and is refused by walls with their reasons, the cell parent
/// describes a lent room from the legend, and +map draws the places around the player. Not in parallel: the
/// package's commands are global.
/// </summary>
[NotInParallel]
public class WorldGridIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task<string> God(string command)
	{
		var god = ConnectionService.Get(1)!.Ref!.Value;
		await WebAppFactoryArg.QueueBarrierAsync();
		var before = Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(god).Skip(before));
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	/// <summary>Whether this test installed world-map, which world-grid needs, and so removes it again.</summary>
	private bool _installedWorldMap;

	private async Task InstallAsync()
	{
		var registry = WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();
		var controller = new PackagesController(
			registry,
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<IBundledPackageBootstrap>());
		if (await registry.GetInstalledPackageAsync("world-map") is not InstalledPackageRecord)
		{
			var map = await controller.Apply(new ApplyRequest(BundledPackages.RemoteName, "world-map", null, null, null), CancellationToken.None);
			await Assert.That(map.Result).IsTypeOf<OkObjectResult>().Because("world-map must install from the catalogue");
			_installedWorldMap = true;
		}
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "world-grid", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("world-grid must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	[Test]
	public async Task APlayerWalksAWorldMapThroughTheRoomsItsPlacesAreLent()
	{
		try
		{
			await InstallAsync();
			var map = TestIsolationHelpers.GenerateUniqueName("Wilds");
			await God($"@dig {map}");
			var mapRef = await Eval($"num(search(name={map}))");
			await God($"@world/create {mapRef}");
			await God($"@world/paint {mapRef}/0,0:2,2=forest");
			await God($"&MAP`LEGEND`FOREST`LABEL {mapRef}=Old forest");
			await God($"&MAP`LEGEND`FOREST`DESC {mapRef}=Oaks crowd the path.");
			await God($"@world/wall {mapRef}/1,1/n=wall,A fallen oak blocks the path.");
			await Assert.That(await God($"+map/exits {mapRef}")).Contains($"Opened 10 direction exits on {map}.");
			await Assert.That(await God($"+map/exits {mapRef}")).Contains($"{map} has every direction exit.");

			var walker = await Player("Walker");
			var start = await Eval($"worldroom({mapRef},1,1)");
			await God($"@tel {walker.DbRef}={start}");

			await Assert.That(await As(walker, "north")).Contains("A fallen oak blocks the path.");
			await Assert.That(await Eval($"num(loc({walker.DbRef}))")).IsEqualTo(start);

			var walked = await As(walker, "east");
			var east = await Eval($"worldcell({mapRef},2,1,,room)");
			await Assert.That(east).StartsWith("#");
			await Assert.That(await Eval($"num(loc({walker.DbRef}))")).IsEqualTo(east);
			await Assert.That(walked).Contains("Old forest (2,1,0)").And.Contains("Oaks crowd the path.");

			await Assert.That(await As(walker, "e")).Contains("You can't go that way.");
			await Assert.That(await As(walker, "+map")).Contains("   OO@");
			await Assert.That(await As(walker, "+map/keep")).Contains($"You do not control {map}.");
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("world-grid");
			if (_installedWorldMap) await WebAppFactoryArg.Services.UninstallWithBridgesAsync("world-map");
		}
	}
}
