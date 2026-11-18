using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// The scene-rooms package: +scene/create/copy and +scene/start/copy run a scene in a copy of the room the runner
/// stands in. The copy takes the room's look and never its map data, belongs to the Scene Logger's owner, and goes
/// when the scene ends. Not in parallel: the package is installed and removed around each test, and its caps count
/// every copy in the game.
/// </summary>
[NotInParallel]
public class SceneRoomsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<string> God(string command)
	{
		var god = DBRef.Parse(await Eval("objid(#1)"));
		var before = Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(god).Skip(before));
	}

	/// <summary>
	/// What <paramref name="player"/> was told while <paramref name="command"/> ran, and the work it queued: a scene
	/// that ends raises its event, which triggers the package's TRIG`SCENE, which triggers TRIG`CLOSE, each behind the
	/// last, so one barrier per step.
	/// </summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		for (var step = 0; step < 4; step++)
			await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private async Task<string> PackageObjectAsync(string package, string reference)
	{
		var registry = WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();
		return (await registry.GetPackageObjectsAsync(package)).Single(o => o.Ref == reference).Objid;
	}

	private async Task LoggerAsync() => await God($"@teleport {await PackageObjectAsync("scene", "logger")}=#2");

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, DBRef home)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		await God($"@role/assign #{player.DbRef.Number}=approved");
		return player;
	}

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName(prefix), god));
	}

	private async Task InstallAsync(string package)
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private Task<string> WhereIs(TestIsolationHelpers.TestPlayer player) => Eval($"num(loc(#{player.DbRef.Number}))");

	[Test]
	public async Task A_copy_takes_the_look_and_parent_and_never_the_map_and_goes_when_the_scene_ends()
	{
		await LoggerAsync();
		await InstallAsync("scene-rooms");
		try
		{
			var source = await RoomAsync("CopySource");
			var parent = await RoomAsync("CopyParent");
			var map = await RoomAsync("CopyMap");
			var runner = await PlayerAsync("CopyRunner", source);
			var watcher = await PlayerAsync("CopyWatcher", source);
			var description = TestIsolationHelpers.GenerateUniqueName("CopyDesc");
			await God($"&MAP`KIND {map}=grid");
			await God($"@chzone {source}={map}");
			await God($"@parent {source}={parent}");
			await God($"&CONFORMAT {parent}=Inherited format");
			await God($"@describe {source}={description}");
			await God($"&CONFORMAT {source}=Here: %0");
			await God($"&MAP`COORDS`X {source}=12");
			await God($"&MAP`PLACE {source}=Harbour");
			await God($"&ENVIRONMENT {source}=coast");
			await God($"&SMELL {source}=Salt");

			var created = await As(runner, $"+scene/create/copy {TestIsolationHelpers.GenerateUniqueName("CopyScene")}");
			await Assert.That(created).Contains("created and focused");
			var scene = await Eval($"scenefocus(#{runner.DbRef.Number})");
			var copy = await WhereIs(runner);
			await Assert.That(copy).IsNotEqualTo($"#{source.Number}").Because("the runner is taken to the copy");
			await Assert.That(await Eval($"num(scene({scene},room))")).IsEqualTo(copy);
			await Assert.That(await Eval($"scene({scene},istemp)")).IsEqualTo("1");

			await Assert.That(await Eval($"name({copy})")).IsEqualTo(await Eval($"name({source})"));
			await Assert.That(await Eval($"[hasflag({copy},SCENE_ROOM)] [hasflag({copy},FLOATING)] [hasflag({copy},MISTRUST)]"))
				.IsEqualTo("1 1 1");
			await Assert.That(await Eval($"owner({copy})")).IsEqualTo(await Eval($"owner({await PackageObjectAsync("scene", "logger")})"));
			await Assert.That(await Eval($"get({copy}/DESCRIBE)")).IsEqualTo(description);
			await Assert.That(await Eval($"get({copy}/CONFORMAT)")).IsEqualTo("Here: %0");
			await Assert.That(await Eval($"num(parent({copy}))")).IsEqualTo($"#{parent.Number}");
			await Assert.That(await Eval($"[lattr({copy}/MAP**)][lattr({copy}/ENVIRONMENT)][lattr({copy}/SMELL)]")).IsEmpty();
			await Assert.That(await Eval($"zone({copy})")).StartsWith("#-1");

			var exit = await Eval($"lexits({copy})");
			await Assert.That(exit.Split(' ')).Count().IsEqualTo(1);
			await Assert.That(await Eval($"fullname({exit})")).IsEqualTo("out;o");

			// Those left behind are told where the scene went.
			await Assert.That(string.Join("\n", Notifications.For(watcher.DbRef))).Contains($"took scene {scene} to a copy of this room");
			await Assert.That(await As(runner, $"+scene/room {scene}")).Contains("Copied from").And.Contains("when the scene ends");

			await As(runner, "out");
			await Assert.That(await WhereIs(runner)).IsEqualTo($"#{source.Number}").Because("the way out leads back");
			await As(runner, $"+scene/go {scene}");
			await Assert.That(await WhereIs(runner)).IsEqualTo(copy);

			var finished = await As(runner, $"+scene/finish {scene}");
			await Assert.That(finished).Contains("its room copy is gone");
			await Assert.That(await WhereIs(runner)).IsEqualTo($"#{source.Number}");
			await Assert.That(await Eval($"isdbref({copy})")).IsEqualTo("0").Because("the copy is destroyed with its scene");
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("scene-rooms");
		}
	}

	[Test]
	public async Task A_parent_that_holds_map_data_is_dropped_and_its_look_copied_instead()
	{
		await LoggerAsync();
		await InstallAsync("scene-rooms");
		try
		{
			var source = await RoomAsync("FlatSource");
			var parent = await RoomAsync("FlatParent");
			var runner = await PlayerAsync("FlatRunner", source);
			await God($"@parent {source}={parent}");
			await God($"&DESCFORMAT {parent}=From the parent: %0");
			await God($"&MAP`POS`X {parent}=4");

			await As(runner, $"+scene/create/copy {TestIsolationHelpers.GenerateUniqueName("FlatScene")}");
			var copy = await WhereIs(runner);

			await Assert.That(copy).IsNotEqualTo($"#{source.Number}");
			await Assert.That(await Eval($"parent({copy})")).IsEqualTo("#-1");
			await Assert.That(await Eval($"get({copy}/DESCFORMAT)")).IsEqualTo("From the parent: %0");
			await Assert.That(await Eval($"lattr({copy}/MAP**)")).IsEmpty();
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("scene-rooms");
		}
	}

	[Test]
	public async Task Start_copy_moves_a_paused_scene_into_a_copy_and_the_cap_refuses_a_second()
	{
		await LoggerAsync();
		await InstallAsync("scene-rooms");
		try
		{
			var source = await RoomAsync("StartSource");
			var runner = await PlayerAsync("StartRunner", source);
			await As(runner, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("StartScene")}");
			var scene = await Eval($"scenefocus(#{runner.DbRef.Number})");
			await As(runner, $"+scene/pause {scene}");
			await Assert.That(await Eval($"scene({scene},status)")).IsEqualTo("paused");

			await As(runner, $"+scene/start/copy {scene}");
			var copy = await WhereIs(runner);
			await Assert.That(copy).IsNotEqualTo($"#{source.Number}");
			await Assert.That(await Eval($"scene({scene},status)")).IsEqualTo("active");
			await Assert.That(await Eval($"num(scene({scene},room))")).IsEqualTo(copy);

			// Again from the source room: the scene goes back to the copy it has rather than making another.
			await As(runner, "out");
			await Assert.That(await As(runner, $"+scene/start/copy {scene}")).Contains("is running in its room copy");
			await Assert.That(await WhereIs(runner)).IsEqualTo(copy);

			// One copy per player by default.
			await As(runner, "out");
			await Assert.That(await As(runner, $"+scene/create/copy {TestIsolationHelpers.GenerateUniqueName("Second")}"))
				.Contains("You have as many room copies as a player may: 1.");

			// A copy can't be copied.
			await As(runner, $"+scene/go {scene}");
			await God($"&DATA`CONFIG`PER-PLAYER {await PackageObjectAsync("scene-rooms", "rooms")}=5");
			await Assert.That(await As(runner, $"+scene/create/copy {TestIsolationHelpers.GenerateUniqueName("Third")}"))
				.Contains("This room is already a copy for a scene.");

			await As(runner, $"+scene/finish {scene}");
			await Assert.That(await Eval($"isdbref({copy})")).IsEqualTo("0");
			await Assert.That(await WhereIs(runner)).IsEqualTo($"#{source.Number}");
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("scene-rooms");
		}
	}

	[Test]
	public async Task A_paused_scene_loses_its_copy_after_the_keep_window_and_start_copy_makes_another()
	{
		await LoggerAsync();
		await InstallAsync("scene-rooms");
		try
		{
			var rooms = await PackageObjectAsync("scene-rooms", "rooms");
			var source = await RoomAsync("PausedSource");
			var runner = await PlayerAsync("PausedRunner", source);
			await As(runner, $"+scene/create/copy {TestIsolationHelpers.GenerateUniqueName("PausedScene")}");
			var scene = await Eval($"scenefocus(#{runner.DbRef.Number})");
			var copy = await WhereIs(runner);
			await As(runner, $"+scene/pause {scene}");
			var record = $"ROOM`{copy.TrimStart('#')}";
			await Assert.That(await Eval($"words(get({rooms}/{record}))")).IsEqualTo("5").Because("a pause is noted on the record");

			// Paused since the epoch, well past the week a paused scene keeps its copy.
			await God($"&{record} {rooms}={await Eval($"extract(get({rooms}/{record}),1,4)")} 0");
			await God($"@trigger {rooms}/TRIG`SWEEP`ONE={record}");
			for (var step = 0; step < 3; step++)
				await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(await Eval($"isdbref({copy})")).IsEqualTo("0");
			await Assert.That(await WhereIs(runner)).IsEqualTo($"#{source.Number}");
			await Assert.That(string.Join("\n", Notifications.For(runner.DbRef))).Contains($"Scene {scene} was paused for a while");
			await Assert.That(await Eval($"[scene({scene},istemp)]|[scene({scene},room)]|[scene({scene},status)]")).IsEqualTo("0||paused");

			await As(runner, $"+scene/start/copy {scene}");
			var second = await WhereIs(runner);
			await Assert.That(second).IsNotEqualTo($"#{source.Number}");
			await Assert.That(await Eval($"num(scene({scene},room))")).IsEqualTo(second);
			await Assert.That(await Eval($"scene({scene},status)")).IsEqualTo("active");
			await As(runner, $"+scene/finish {scene}");
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("scene-rooms");
		}
	}

	[Test]
	public async Task A_copy_older_than_the_day_limit_goes_even_while_its_scene_runs()
	{
		await LoggerAsync();
		await InstallAsync("scene-rooms");
		try
		{
			var rooms = await PackageObjectAsync("scene-rooms", "rooms");
			await Assert.That(await Eval($"get({rooms}/DATA`CONFIG`MAX-DAYS)")).IsEqualTo("30");
			var source = await RoomAsync("AgedSource");
			var runner = await PlayerAsync("AgedRunner", source);
			await As(runner, $"+scene/create/copy {TestIsolationHelpers.GenerateUniqueName("AgedScene")}");
			var scene = await Eval($"scenefocus(#{runner.DbRef.Number})");
			var copy = await WhereIs(runner);
			await Assert.That(await As(runner, $"+scene/room {scene}")).Contains("at the latest");
			var record = $"ROOM`{copy.TrimStart('#')}";

			// Made 29 days ago: still inside the limit.
			var words = (await Eval($"get({rooms}/{record})")).Split(' ');
			var madeAt = long.Parse(await Eval("secs()"));
			await God($"&{record} {rooms}={words[0]} {words[1]} {words[2]} {madeAt - 29 * 86400}");
			await God($"@trigger {rooms}/TRIG`SWEEP`ONE={record}");
			for (var step = 0; step < 3; step++)
				await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await Eval($"isdbref({copy})")).IsEqualTo("1");

			// Made 31 days ago: past it, though the scene still runs.
			await God($"&{record} {rooms}={words[0]} {words[1]} {words[2]} {madeAt - 31 * 86400}");
			await God($"@trigger {rooms}/TRIG`SWEEP`ONE={record}");
			for (var step = 0; step < 3; step++)
				await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(await Eval($"isdbref({copy})")).IsEqualTo("0");
			await Assert.That(await WhereIs(runner)).IsEqualTo($"#{source.Number}");
			await Assert.That(string.Join("\n", Notifications.For(runner.DbRef))).Contains("reached the game's limit of 30 days");
			await Assert.That(await Eval($"[scene({scene},istemp)]|[scene({scene},room)]|[scene({scene},status)]")).IsEqualTo("0||active");
			await As(runner, $"+scene/finish {scene}");
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("scene-rooms");
		}
	}
}
