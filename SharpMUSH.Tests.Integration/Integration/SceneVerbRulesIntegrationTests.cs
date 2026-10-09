using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The scene verbs' rules: times they can read, what each lifecycle verb does to a scene already in that
/// state, pose edits and personas, reading a scene's log from anywhere, and the usage line a player gets
/// instead of <c>Huh?</c> (#1690, #1691, #1692, #1693, #1696).
/// </summary>
[NotInParallel]
public class SceneVerbRulesIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];
	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message.ToPlainText().Trim();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private static string Num(DBRef dbref) => $"#{dbref.Number}";

	private async Task<string> RunAs(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return string.Join("\n", Notifications.For(actor).Skip(before));
	}

	/// <summary>
	/// The room this test's players stand in. DefaultHome holds every player the session made, and each scene
	/// change in a room refreshes it for everyone connected there, on the queue every test waits behind.
	/// </summary>
	private DBRef? _room;

	private async Task<DBRef> RoomAsync() => _room ??= DBRef.Parse(
		(await God1($"@dig {TestIsolationHelpers.GenerateUniqueName("SceneRoom")}")).Message.ToPlainText().Trim());

	private async Task<(DBRef Dbref, long Handle)> CreatePlayerAsync(string name)
	{
		await TestIsolationHelpers.CreateNamedTestPlayerAsync(WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), name, await RoomAsync());
		var dbref = (await God1($"think [pmatch({name})]")).Message.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@role/assign {dbref}=approved");
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed.Value);
		_actors[handle] = parsed.Value;
		return (parsed.Value, handle);
	}

	/// <summary>The Logger's $-commands only match from the master room; other suites move it.</summary>
	private async Task PutLoggerInMasterRoomAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objects = await registry.GetPackageObjectsAsync("scene");
		await God1($"@teleport {DBRef.Parse(objects.Single(o => o.Ref == "logger").Objid)}=#2");
	}

	/// <summary>A running scene owned by <paramref name="handle"/>, and its id.</summary>
	private async Task<string> CreateSceneAsync(long handle, string title)
	{
		await RunAs(handle, $"+scene/create {title}");
		return await Eval($"scenefocus({Num(_actors[handle])})");
	}

	private static string IdIn(string said) => Regex.Match(said, @"Scheduled scene (\d+)").Groups[1].Value;

	private static long NowMillis => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	[Test]
	[Arguments("A", "tomorrow 8pm")]
	[Arguments("B", "garbage")]
	[Arguments("C", "yesterday")]
	[Arguments("D", "")]
	public async Task Schedule_RefusesATimeItCannotRead(string key, string when)
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Sched{key}{Tag}");
		var mine = await RunAs(handle, "think words(scenelist(mine))");

		var said = await RunAs(handle, $"+scene/schedule Unread {Tag}={when}");

		await Assert.That(said).Contains("can't read that as a time").Because($"'{when}' is not a time");
		await Assert.That(said).Contains("2026-12-31 20:00").Because("the refusal shows a time it would take");
		await Assert.That(said).DoesNotContain("Scheduled scene");
		await Assert.That(await RunAs(handle, "think words(scenelist(mine))")).IsEqualTo(mine)
			.Because("a refused schedule makes no scene");
	}

	[Test]
	public async Task Schedule_TakesATimeFromNow_AndRefusesOneGone()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Rel{Tag}");

		var said = await RunAs(handle, $"+scene/schedule Relative {Tag}=+3d");
		var id = IdIn(said);
		await Assert.That(id).IsNotEmpty().Because($"+3d is a time, as +job/due takes it: {said}");
		var at = long.Parse(await Eval($"scene({id},scheduledfor)"));
		var expected = NowMillis + (long)TimeSpan.FromDays(3).TotalMilliseconds;
		await Assert.That(Math.Abs(at - expected)).IsLessThan((long)TimeSpan.FromMinutes(2).TotalMilliseconds);

		var gone = await RunAs(handle, $"+scene/schedule Gone {Tag}=2020-01-01 10:00");
		await Assert.That(gone).Contains("already passed");
		await Assert.That(gone).DoesNotContain("Scheduled scene");

		var usage = await RunAs(handle, $"+scene/schedule No time {Tag}");
		await Assert.That(usage).Contains("+scene/schedule <title>=<when>").Because("no = is a usage line, not Huh?");
	}

	[Test]
	public async Task Reschedule_RefusesAnUnreadableTime_AndAFinishedScene()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Resk{Tag}");
		var id = await CreateSceneAsync(handle, $"Resk Scene {Tag}");

		foreach (var when in new[] { "garbage text", "" })
		{
			var said = await RunAs(handle, $"+scene/reschedule {id}={when}");
			await Assert.That(said).Contains("can't read that as a time");
			await Assert.That(await Eval($"scene({id},status)")).IsEqualTo("active")
				.Because("a refused reschedule must not pause the running scene");
			await Assert.That(await Eval($"scene({id},scheduledfor)")).IsEqualTo(string.Empty);
		}

		await Assert.That(await RunAs(handle, $"+scene/reschedule {id}")).Contains("+scene/reschedule <id>=<when>");

		await RunAs(handle, $"+scene/finish {id}");
		var finished = await RunAs(handle, $"+scene/reschedule {id}=+1d");
		await Assert.That(finished).Contains("is finished");
		await Assert.That(await Eval($"scene({id},scheduledfor)")).IsEqualTo(string.Empty)
			.Because("a finished scene gets no time to come");
	}

	[Test]
	public async Task LifecycleVerbs_SayWhenTheSceneIsAlreadyThere()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Life{Tag}");
		var id = await CreateSceneAsync(handle, $"Life Scene {Tag}");

		await Assert.That(await RunAs(handle, "+scene/start")).Contains($"Scene {id} is already running.");

		await Assert.That(await RunAs(handle, $"+scene/pause {id}")).Contains($"Scene {id} paused.");
		await Assert.That(await RunAs(handle, $"+scene/pause {id}")).Contains($"Scene {id} is already paused.");
		await Assert.That(await RunAs(handle, "+scene/start")).Contains($"Scene {id} is now active.")
			.Because("bare +scene/start names the scene, as +scene/start <id> does");

		await Assert.That(await RunAs(handle, $"+scene/finish {id}")).Contains($"Scene {id} finished.");
		await Assert.That(await RunAs(handle, $"+scene/finish {id}")).Contains($"Scene {id} is already finished.");

		var scheduled = IdIn(await RunAs(handle, $"+scene/schedule Later {Tag}=+2d"));
		await Assert.That(await RunAs(handle, $"+scene/cancel {scheduled}")).Contains($"Cancelled scene {scheduled}.");
		await Assert.That(await RunAs(handle, $"+scene/cancel {scheduled}")).Contains($"Scene {scheduled} is already cancelled.");
		await Assert.That(await RunAs(handle, $"+scene/start {scheduled}")).Contains("was cancelled");
		await Assert.That(await Eval($"scene({scheduled},status)")).IsEqualTo("cancelled")
			.Because("a cancelled scene is as final as a finished one");
	}

	[Test]
	public async Task Edit_SaysWhatHappened()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Edi{Tag}");
		var (wizard, wizardHandle) = await CreatePlayerAsync($"Ewiz{Tag}");
		await God1($"@set {Num(wizard)}=WIZARD");
		var id = await CreateSceneAsync(handle, $"Edit Scene {Tag}");
		await RunAs(handle, $"+scene/pose {id}=waves to the crowd.");
		var pose = await Eval($"last(sceneposes({id}))");

		await Assert.That(await RunAs(handle, $"+scene/edit {pose}=crowd^^^room")).Contains($"Pose {pose} edited.");
		await Assert.That(await Eval($"scenepose({id},{pose},content)")).EndsWith("waves to the room.");

		await Assert.That(await RunAs(handle, $"+scene/edit {pose}=room")).Contains("+scene/edit <pose>=<old>^^^<new>");
		await Assert.That(await RunAs(handle, "+scene/edit")).Contains("+scene/edit <pose>=<old>^^^<new>");
		await Assert.That(await RunAs(handle, $"+scene/edit {pose}=nowhere^^^here")).Contains("has no \"nowhere\" in it");
		await Assert.That(await RunAs(handle, "+scene/edit 987654321=a^^^b")).Contains("No such pose: 987654321");

		var blank = await RunAs(handle, $"+scene/edit {pose}=waves to the room.^^^");
		await Assert.That(blank).Contains("can't be left empty")
			.Because("+scene/rewrite refuses a pose with nothing in it, and an edit to the same result agrees");
		await Assert.That(await Eval($"scenepose({id},{pose},content)")).EndsWith("waves to the room.");

		await Assert.That(await RunAs(wizardHandle, $"+scene/edit {pose}=room^^^hall")).Contains($"Pose {pose} edited.")
			.Because("a wizard may edit anyone's pose, as with undo and redo");
	}

	[Test]
	public async Task As_ReachesThePose_AndClears()
	{
		await PutLoggerInMasterRoomAsync();
		var (who, handle) = await CreatePlayerAsync($"Pers{Tag}");
		var name = await Eval($"name({Num(who)})");
		var id = await CreateSceneAsync(handle, $"Persona Scene {Tag}");

		await RunAs(handle, "+scene/as Masked Stranger");
		await RunAs(handle, $"+scene/pose {id}=bows.");
		await Assert.That(await Eval($"scenepose({id},last(sceneposes({id})),content)")).IsEqualTo("Masked Stranger bows.")
			.Because("the persona is the name the pose goes out under, not only its header");

		await Assert.That(await RunAs(handle, "+scene/as =")).Contains("can't have a comma or an =");
		await Assert.That(await Eval($"scenemember({id},{Num(who)},showas)")).IsEqualTo("Masked Stranger");

		await Assert.That(await RunAs(handle, "+scene/as")).Contains("under your own name");
		await Assert.That(await Eval($"scenemember({id},{Num(who)},showas)")).IsEqualTo(string.Empty);

		await RunAs(handle, "+scene/as Masked Stranger");
		await RunAs(handle, $"+scene/as {name}");
		await Assert.That(await Eval($"scenemember({id},{Num(who)},showas)")).IsEqualTo(string.Empty)
			.Because("taking your own name as a persona is going back to it");
	}

	[Test]
	public async Task AnotherScenesLog_IsReadableWithoutJoiningIt()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, ownerHandle) = await CreatePlayerAsync($"Lowner{Tag}");
		var (reader, readerHandle) = await CreatePlayerAsync($"Lreader{Tag}");
		var id = await CreateSceneAsync(ownerHandle, $"{Tag} Lantern Night");
		await RunAs(ownerHandle, $"+scene/emit {id}=The lantern gutters {Tag}.");
		await RunAs(ownerHandle, $"+scene/ooc {id}=brb {Tag}");
		await RunAs(ownerHandle, $"+scene/finish {id}");

		var recalled = await RunAs(readerHandle, $"+scene/recall {id}=5");
		await Assert.That(recalled).Contains($"The lantern gutters {Tag}.");
		await Assert.That(recalled).Contains($"<OOC · {id}> Lowner{Tag}: brb {Tag}")
			.Because("an out-of-character line keeps its marker, with the scene it belongs to");
		await Assert.That(recalled.Split('\n').Count(line => line.Contains("· pose ", StringComparison.Ordinal))).IsEqualTo(1)
			.Because("the emit is drawn under a rule; the out-of-character line is one tagged line without one");

		var log = await RunAs(readerHandle, $"+scene/log {id}");
		await Assert.That(log).Contains($"Scene {id}: {Tag} Lantern Night");
		await Assert.That(log).Contains($"The lantern gutters {Tag}.");
		await Assert.That(await Eval($"scenefocus({Num(reader)})")).StartsWith("#-1")
			.Because("reading a log does not join the scene");

		await Assert.That(await RunAs(readerHandle, $"+scene {Tag} Lantern Night")).Contains($"Scene {id}:")
			.Because("a scene's title finds it");
		await Assert.That(await RunAs(readerHandle, $"+scene {Tag} Lant")).Contains($"Scene {id}:")
			.Because("so does the start of its title");
	}

	[Test]
	public async Task WebPoses_AreFramedInTheRoom()
	{
		await PutLoggerInMasterRoomAsync();
		var (who, handle) = await CreatePlayerAsync($"Frame{Tag}");
		var room = (await God1($"@dig FrameRoom_{Tag}")).Message.ToPlainText().Trim();
		await God1($"@tel {Num(who)}={room}");
		var id = await CreateSceneAsync(handle, $"Frame Scene {Tag}");

		var said = await RunAs(handle, $"+scene/emit {id}=Thunder rolls {Tag}.");
		var pose = await Eval($"last(sceneposes({id}))");

		await Assert.That(said).Contains($"Thunder rolls {Tag}.");
		await Assert.That(said).Contains($"Frame{Tag} · pose {pose}").Because("the rule names who posed and the pose, as for a captured pose");
		await Assert.That(said).Contains($"< Scene {id} >").Because("the rule carries the scene on its right");
	}

	[Test]
	public async Task AnUnmatchedSwitch_GetsItsUsage_AndAPrefixRuns()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Usage{Tag}");

		await Assert.That(await RunAs(handle, "+scene/join")).Contains("+scene/join <id>");
		await Assert.That(await RunAs(handle, "+scene/rewrite 10")).Contains("+scene/rewrite <pose>=<text>");
		await Assert.That(await RunAs(handle, $"+scene/zz{Tag}")).Contains($"+scene has no /zz{Tag} switch.");
		await Assert.That(await RunAs(handle, "+scene/un")).Contains("could be");

		var listed = await RunAs(handle, "+scene/lis");
		await Assert.That(listed).Contains("Scenes: active").Because("a unique prefix of a switch is that switch");
		await Assert.That(listed).DoesNotContain("Warning").Because("a command that ran gets no usage line as well");

		await Assert.That(await RunAs(handle, "+scene/join 987654321")).Contains("[SCENE] Warning: No such scene: 987654321")
			.Because("a refusal carries the same bracketed badge as a confirmation");
	}
}
