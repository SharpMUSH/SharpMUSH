using System.Text.Json;
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
/// The highlights package: <c>+scene/fav</c> and <c>/unfav</c>, the highlights and a player's favourites, the
/// time window, private scenes on the portal's routes, and the mark on a favourite pose's rule. Not in parallel:
/// each test installs the package, and its store is the game's, one for every test.
/// </summary>
[NotInParallel]
public class HighlightsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task God(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private async Task<string> PackageObjectAsync(string package, string reference) =>
		$"#{DBRef.Parse((await Registry.GetPackageObjectsAsync(package)).First(o => o.Ref == reference).Objid).Number}";

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

	/// <summary>A scene <paramref name="owner"/> creates and focuses, and its id.</summary>
	private async Task<string> SceneAsync(TestIsolationHelpers.TestPlayer owner)
	{
		await As(owner, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("HighlightScene")}");
		return await Eval($"scenefocus(#{owner.DbRef.Number})");
	}

	/// <summary><paramref name="author"/> poses in their focused scene, and the pose's number.</summary>
	private async Task<string> PoseAsync(TestIsolationHelpers.TestPlayer author, string scene)
	{
		await As(author, $"pose {TestIsolationHelpers.GenerateUniqueName("poses")}");
		return await Eval($"last(sceneposes({scene}))");
	}

	private async Task<string> ObjidAsync(TestIsolationHelpers.TestPlayer player) => await Eval($"objid(#{player.DbRef.Number})");

	/// <summary>Installs highlights from the catalogue and returns its store, every favourite in it written by this test.</summary>
	private async Task<string> InstallAsync()
	{
		var controller = new PackagesController(
			Registry,
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "highlights", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("highlights must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
		var store = await PackageObjectAsync("highlights", "store");
		await God($"@teleport {await PackageObjectAsync("scene", "logger")}=#2");
		return store;
	}

	private async Task UninstallAsync()
	{
		await Installer.UninstallAsync("highlights", force: true, CancellationToken.None);
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	[Test]
	public async Task Players_favourite_poses_and_take_them_back()
	{
		var room = await RoomAsync("FavRoom");
		var writer = await PlayerAsync("FavWriter", room);
		var reader = await PlayerAsync("FavReader", room);
		try
		{
			await InstallAsync();
			var scene = await SceneAsync(writer);
			await As(reader, $"+scene/join {scene}");
			var pose = await PoseAsync(writer, scene);

			await Assert.That(await As(reader, "+scene/fav")).Contains("+scene/fav <pose>");
			await Assert.That(await As(reader, "+scene/fav nine")).Contains("No such pose: nine.");
			await Assert.That(await As(reader, "+scene/fav 999999")).Contains("No such pose: 999999.");
			await Assert.That(await As(writer, $"+scene/fav {pose}")).Contains($"Pose {pose} is your own.");
			await Assert.That(await As(reader, $"+scene/fav {pose}"))
				.Contains($"Pose {pose} by {writer.Name} is one of your favourites now.");
			await Assert.That(await As(reader, $"+scene/fav {pose}")).Contains($"Pose {pose} is already one of your favourites.");

			await Assert.That(await As(reader, "+scene/favs")).Contains("Your favourites").And.Contains(pose);
			await Assert.That(await As(reader, $"+scene/recall {scene}=1")).Contains("· favourite")
				.Because("a pose the reader favourited says so on its rule");
			await Assert.That(await As(writer, $"+scene/recall {scene}=1")).DoesNotContain("favourite")
				.Because("the mark is the reader's own, not a count");

			await Assert.That(await As(writer, $"+scene/unfav {pose}")).Contains($"Pose {pose} is not one of your favourites.");
			await Assert.That(await As(reader, $"+scene/unfav {pose}")).Contains($"Pose {pose} is no longer one of your favourites.");
			await Assert.That(await As(reader, "+scene/favs")).Contains("You have no favourite poses yet.");
			await Assert.That(await As(reader, "+scene/highlights")).Contains("No pose from the last 4 weeks is a favourite yet.");
		}
		finally
		{
			await UninstallAsync();
		}

		var logger = await PackageObjectAsync("scene", "logger");
		await Assert.That(await Eval($"hasattr({logger},CMD`FAVOURITE)")).IsEqualTo("0").Because("uninstalling takes the commands off the Scene Logger");
	}

	[Test]
	public async Task Highlights_put_the_most_favourited_first_and_keep_to_the_window()
	{
		var room = await RoomAsync("HighRoom");
		var writer = await PlayerAsync("HighWriter", room);
		var first = await PlayerAsync("HighFirst", room);
		var second = await PlayerAsync("HighSecond", room);
		try
		{
			var store = await InstallAsync();
			var scene = await SceneAsync(writer);
			var older = await PoseAsync(writer, scene);
			var liked = await PoseAsync(writer, scene);
			var newer = await PoseAsync(writer, scene);

			await As(first, $"+scene/fav {older}");
			await As(first, $"+scene/fav {liked}");
			await As(second, $"+scene/fav {liked}");
			await As(second, $"+scene/fav {newer}");

			var logger = await PackageObjectAsync("scene", "logger");
			await Assert.That(await Eval($"u({logger}/FUN`HIGHLIGHT`IDS)")).IsEqualTo($"{liked} {newer} {older}")
				.Because("the pose two players favourited comes first, then the newer of the rest");
			await Assert.That(await Eval($"u({logger}/FUN`HIGHLIGHT`MINE,{await ObjidAsync(second)})")).IsEqualTo($"{newer} {liked}");

			var screen = await As(first, "+scene/highlights");
			await Assert.That(screen).Contains("Highlights").And.Contains("from the last 4 weeks");
			await Assert.That(screen.IndexOf($"pose {liked}", StringComparison.Ordinal))
				.IsLessThan(screen.IndexOf($"pose {older}", StringComparison.Ordinal));

			await God($"&POSE`{older}`AT {store}=sub(secs(),3000000)");
			await Assert.That(await Eval($"u({logger}/FUN`HIGHLIGHT`IDS)")).IsEqualTo($"{liked} {newer}")
				.Because("a pose written before the window leaves the highlights");
			await God($"&DATA`CONFIG`WINDOW {store}=0");
			await Assert.That(await Eval($"u({logger}/FUN`HIGHLIGHT`IDS)")).IsEqualTo($"{liked} {newer} {older}")
				.Because("a window of 0 reaches every favourite");

			await God($"&DATA`CONFIG`PAGE-SIZE {store}=2");
			await Assert.That(await As(first, "+scene/highlights")).Contains("+scene/highlights/2 shows more.");
			await Assert.That(await As(first, "+scene/highlights/2")).Contains("page 2").And.DoesNotContain("shows more");
			await Assert.That(await As(first, "+scene/highlights/3")).Contains("There is no page 3 of highlights.");
			await Assert.That(await As(first, "+scene/highlights/0")).Contains("Which page?");

			await As(first, $"+scene/hide ic");
			await Assert.That(await Eval($"u({logger}/FUN`HIGHLIGHT`PAGE,{await ObjidAsync(first)},{liked} {newer} {older},1)")).IsEmpty()
				.Because("a pose type the reader hides stays out of what they are shown");
			await As(first, $"+scene/show ic");

			await As(writer, $"+scene/delete {newer}");
			await Assert.That(await Eval($"u({logger}/FUN`HIGHLIGHT`PAGE,{await ObjidAsync(first)},{liked} {newer} {older},1)"))
				.IsEqualTo($"{liked} {older}").Because("a deleted pose leaves the highlights");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task A_private_scene_shows_only_to_its_members_on_the_portal()
	{
		var room = await RoomAsync("PrivRoom");
		var writer = await PlayerAsync("PrivWriter", room);
		var member = await PlayerAsync("PrivMember", room);
		var outsider = await PlayerAsync("PrivOutside", room);
		try
		{
			await InstallAsync();
			var logger = await PackageObjectAsync("scene", "logger");
			var scene = await SceneAsync(writer);
			await As(member, $"+scene/join {scene}");
			var pose = await PoseAsync(writer, scene);
			await As(member, $"+scene/fav {pose}");
			await As(writer, "+scene/private");

			async Task<JsonElement> PosesFor(string viewer) =>
				JsonDocument.Parse(await Eval($"u({logger}/WEB`HIGHLIGHT`DATA,{viewer},/apps/highlights)"))
					.RootElement.GetProperty("fields").GetProperty("poses").GetProperty("value");
			await Assert.That((await PosesFor(await ObjidAsync(member))).GetArrayLength()).IsEqualTo(1);
			await Assert.That((await PosesFor(await ObjidAsync(outsider))).GetArrayLength()).IsEqualTo(0)
				.Because("a private scene's poses stay hidden from a character outside it");
			await Assert.That((await PosesFor("")).GetArrayLength()).IsEqualTo(0)
				.Because("a visitor who is not signed in sees only public scenes");
			await Assert.That(await As(outsider, $"+scene/fav {pose}")).Contains($"No such pose: {pose}.");

			await As(writer, "+scene/public");
			var row = (await PosesFor(await ObjidAsync(outsider)))[0];
			await Assert.That(row.GetProperty("author").GetString()).IsEqualTo(writer.Name);
			await Assert.That(row.GetProperty("actions")[0].GetProperty("label").GetString()).IsEqualTo("Favourite");
			var mine = (await PosesFor(await ObjidAsync(member)))[0];
			await Assert.That(mine.GetProperty("actions")[0].GetProperty("label").GetString()).IsEqualTo("Take back");
			await Assert.That(mine.GetProperty("tag").GetString()).IsEqualTo("Your favourite");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>
	/// The portal's Favourite button posts to the act route, and the route answers with the result in its body:
	/// ok and the page's poses again, or the refusal.
	/// </summary>
	[Test]
	public async Task The_favourite_button_answers_in_the_response()
	{
		var room = await RoomAsync("ActRoom");
		var writer = await PlayerAsync("ActWriter", room);
		var reader = await PlayerAsync("ActReader", room);
		try
		{
			var store = await InstallAsync();
			var scene = await SceneAsync(writer);
			var pose = await PoseAsync(writer, scene);
			var readerObjid = await ObjidAsync(reader);
			var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();

			async Task<JsonElement> Act(object body)
			{
				var response = (await dispatcher.DispatchAsync("POST", "/highlights/act", JsonSerializer.Serialize(body), [],
					IHttpHandlerCommandDispatcher.UnknownAddress, DBRef.Parse(readerObjid))).Expect<HttpHandlerResult>();
				await Assert.That(response.Status).IsEqualTo(200).Because(response.Body);
				await WebAppFactoryArg.QueueBarrierAsync();
				await Assert.That(response.Body).IsNotEmpty().Because("the route's body is what the portal reads");
				return JsonDocument.Parse(response.Body).RootElement.Clone();
			}

			var added = await Act(new { pose, favourite = true, at = "/apps/highlights" });
			await Assert.That(added.GetProperty("ok").GetBoolean()).IsTrue().Because(added.ToString());
			await Assert.That(added.GetProperty("message").GetString()).IsEqualTo($"Pose {pose} is one of your favourites now.");
			await Assert.That(await Eval($"get({store}/POSE`{pose}`BY)")).IsEqualTo(readerObjid);

			var again = await Act(new { pose, favourite = true, at = "/apps/highlights" });
			await Assert.That(again.GetProperty("ok").GetBoolean()).IsFalse();
			await Assert.That(again.GetProperty("errors").GetProperty("_global").GetString())
				.IsEqualTo($"Pose {pose} is already one of your favourites.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task Help_and_config_describe_the_highlights()
	{
		var room = await RoomAsync("HelpRoom");
		var reader = await PlayerAsync("HelpReader", room);
		try
		{
			await InstallAsync();
			await Assert.That(await As(reader, "+help scene highlights")).Contains("+scene/fav <pose>").And.Contains("+config highlights");
			await Assert.That(await As(reader, "+config highlights")).Contains("window").And.Contains("page-size").And.Contains("own-poses");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
