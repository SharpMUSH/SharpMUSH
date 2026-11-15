using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// The scene package tells the others in a scene's room when it starts, pauses, resumes or ends: who did it, the
/// scene's number and title, and for a public scene how to join it. Not in parallel: one test turns the setting off
/// on the game's one Scene Logger.
/// </summary>
[NotInParallel]
public class SceneAnnouncementIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task God(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return Heard(player, before);
	}

	private string Heard(TestIsolationHelpers.TestPlayer player, int from) =>
		string.Join("\n", Notifications.For(player.DbRef).Skip(from));

	private async Task<DBRef> LoggerAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		await God($"@teleport {logger}=#2");
		return logger;
	}

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

	[Test]
	public async Task The_room_hears_a_scene_start_pause_resume_and_finish()
	{
		await LoggerAsync();
		var room = await RoomAsync("AnnounceRoom");
		var owner = await PlayerAsync("AnnOwner", room);
		var watcher = await PlayerAsync("AnnWatch", room);
		var elsewhere = await PlayerAsync("AnnAway", await RoomAsync("AnnounceAway"));
		var title = TestIsolationHelpers.GenerateUniqueName("AnnScene");
		var watcherBefore = Notifications.CountFor(watcher.DbRef);
		var awayBefore = Notifications.CountFor(elsewhere.DbRef);

		var created = await As(owner, $"+scene/create {title}=A pitch");
		var scene = await Eval($"scenefocus(#{owner.DbRef.Number})");

		await Assert.That(Heard(watcher, watcherBefore))
			.Contains($"[SCENE] {owner.Name} started scene {scene}: {title} · +scene/join {scene} to join");
		await Assert.That(created).DoesNotContain("started scene").Because("the owner has their own reply");
		await Assert.That(Heard(elsewhere, awayBefore)).DoesNotContain(title);

		watcherBefore = Notifications.CountFor(watcher.DbRef);
		await As(owner, "+scene/pause");
		await Assert.That(Heard(watcher, watcherBefore)).Contains($"{owner.Name} paused scene {scene}: {title}")
			.And.DoesNotContain("to join");

		watcherBefore = Notifications.CountFor(watcher.DbRef);
		await As(owner, "+scene/start");
		await Assert.That(Heard(watcher, watcherBefore))
			.Contains($"{owner.Name} resumed scene {scene}: {title} · +scene/join {scene} to join");

		watcherBefore = Notifications.CountFor(watcher.DbRef);
		await As(owner, "+scene/finish");
		await Assert.That(Heard(watcher, watcherBefore)).Contains($"{owner.Name} finished scene {scene}: {title}");
		await Assert.That(Heard(elsewhere, awayBefore)).DoesNotContain(title);
	}

	[Test]
	public async Task A_private_scene_is_announced_to_its_members_only()
	{
		await LoggerAsync();
		var room = await RoomAsync("AnnPrivRoom");
		var owner = await PlayerAsync("AnnPrivOwner", room);
		var member = await PlayerAsync("AnnPrivMember", room);
		var outsider = await PlayerAsync("AnnPrivOut", room);
		var wizard = await PlayerAsync("AnnPrivWiz", room);
		await God($"@role/assign #{wizard.DbRef.Number}=wizard");
		var title = TestIsolationHelpers.GenerateUniqueName("AnnPrivate");
		await As(owner, $"+scene/create {title}");
		var scene = await Eval($"scenefocus(#{owner.DbRef.Number})");
		await As(owner, "+scene/private");
		await As(owner, $"+scene/invite {scene}=#{member.DbRef.Number}");
		await As(member, $"+scene/join {scene}");
		await As(owner, "+scene/pause");
		var memberBefore = Notifications.CountFor(member.DbRef);
		var outsiderBefore = Notifications.CountFor(outsider.DbRef);
		var wizardBefore = Notifications.CountFor(wizard.DbRef);

		await As(owner, "+scene/start");

		await Assert.That(Heard(member, memberBefore)).Contains($"{owner.Name} resumed scene {scene}: {title}")
			.And.DoesNotContain("to join");
		await Assert.That(Heard(outsider, outsiderBefore)).DoesNotContain(title).And.DoesNotContain($"scene {scene}");
		await Assert.That(Heard(wizard, wizardBefore)).Contains($"{owner.Name} resumed scene {scene}: {title}")
			.Because("a wizard reads every scene");
	}

	[Test]
	public async Task The_room_hears_a_pose_in_a_running_scene_edited()
	{
		await LoggerAsync();
		var room = await RoomAsync("AnnEditRoom");
		var owner = await PlayerAsync("AnnEditOwner", room);
		var watcher = await PlayerAsync("AnnEditWatch", room);
		await As(owner, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("AnnEdit")}");
		var scene = await Eval($"scenefocus(#{owner.DbRef.Number})");
		var word = TestIsolationHelpers.GenerateUniqueName("Lantern");
		await As(owner, $"pose sets a {word} on the bar.");
		var pose = await Eval($"last(sceneposes({scene}))");
		var watcherBefore = Notifications.CountFor(watcher.DbRef);

		var edited = await As(owner, $"+scene/edit {pose}={word}^^^candle");

		await Assert.That(edited).Contains($"Pose {pose} edited.").And.DoesNotContain("edited their pose");
		await Assert.That(Heard(watcher, watcherBefore)).Contains($"[SCENE] {owner.Name} edited their pose {pose} in scene {scene}.");

		var member = await PlayerAsync("AnnEditMember", room);
		await As(member, $"+scene/join {scene}");
		await God($"@teleport #{member.DbRef.Number}={await RoomAsync("AnnEditAway")}");
		watcherBefore = Notifications.CountFor(watcher.DbRef);
		var memberBefore = Notifications.CountFor(member.DbRef);
		await As(owner, $"+scene/undo {pose}");
		await Assert.That(Heard(watcher, watcherBefore)).Contains($"{owner.Name} undid an edit to their pose {pose} in scene {scene}.");
		await Assert.That(Heard(member, memberBefore)).Contains($"{owner.Name} undid an edit to their pose {pose} in scene {scene}.")
			.Because("a member hears about their scene wherever they are");

		memberBefore = Notifications.CountFor(member.DbRef);
		await As(owner, $"+scene/redo {pose}");
		await Assert.That(Heard(member, memberBefore)).Contains($"{owner.Name} redid an edit to their pose {pose} in scene {scene}.");

		await As(owner, "+scene/finish");
		watcherBefore = Notifications.CountFor(watcher.DbRef);
		await As(owner, $"+scene/rewrite {pose}=sets a lamp on the bar.");
		await Assert.That(Heard(watcher, watcherBefore)).DoesNotContain($"pose {pose}")
			.Because("a finished scene's room is somewhere else by now");
	}

	[Test]
	public async Task The_setting_turns_announcements_off()
	{
		var logger = await LoggerAsync();
		var room = await RoomAsync("AnnOffRoom");
		var owner = await PlayerAsync("AnnOffOwner", room);
		var watcher = await PlayerAsync("AnnOffWatch", room);
		var title = TestIsolationHelpers.GenerateUniqueName("AnnOff");
		await God($"&DATA`CONFIG`ANNOUNCE {logger}=0");
		try
		{
			var watcherBefore = Notifications.CountFor(watcher.DbRef);
			await As(owner, $"+scene/create {title}");
			await As(owner, "+scene/finish");
			await Assert.That(Heard(watcher, watcherBefore)).DoesNotContain(title);
		}
		finally
		{
			await God($"&DATA`CONFIG`ANNOUNCE {logger}=1");
		}
	}
}
