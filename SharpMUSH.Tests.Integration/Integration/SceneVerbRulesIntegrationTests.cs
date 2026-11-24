using System.Collections.Concurrent;
using System.Globalization;
using SharpMUSH.Tests.Infrastructure;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

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

	/// <summary>
	/// A running scene owned by <paramref name="handle"/>, and its id. Waits for the queued room announcement, so it
	/// is not heard during the next command someone in the room runs.
	/// </summary>
	private async Task<string> CreateSceneAsync(long handle, string title)
	{
		await RunAs(handle, $"+scene/create {title}");
		await WebAppFactoryArg.QueueBarrierAsync();
		return await Eval($"scenefocus({Num(_actors[handle])})");
	}

	private static string IdIn(string said) => Regex.Match(said, @"Scheduled scene (\d+)").Groups[1].Value;

	private static long NowMillis => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public static IEnumerable<Func<DateExpressionExample>> DateCases() => DateExpressionCorpus.Cases();

	[Test]
	[MethodDataSource(nameof(DateCases))]
	public async Task ScheduleHelperUsesTheSharedFixedReferenceCorpus(DateExpressionExample example)
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		var actual = await Eval($"u({logger}/FUN`SCHEDULE`WHEN,{example.Expression},{DateExpressionCorpus.Zone},{DateExpressionCorpus.Reference})");
		await Assert.That(actual).IsEqualTo(DateTimeOffset.Parse(example.ExpectedUtc, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
	}

	[Test]
	[Arguments("tomorrow 7pm America/Chicagoo")]
	[Arguments("tomorrow 7pm trailing junk")]
	[Arguments("03/04")]
	[Arguments("tomorrow evening")]
	[Arguments("next Tuesday")]
	public async Task ScheduleNaturalRefusalDoesNotWrite(string when)
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync(TestIsolationHelpers.GenerateUniqueName("NatDeny"));
		var before = await RunAs(handle, "think words(scenelist(mine))");
		var said = await RunAs(handle, $"+scene/schedule {when}/Title / extra=Pitch / extra");
		await Assert.That(said).Contains("can't read that as a time");
		await Assert.That(said).DoesNotContain("Scheduled scene");
		await Assert.That(await RunAs(handle, "think words(scenelist(mine))")).IsEqualTo(before);
	}

	[Test]
	public async Task RescheduleAndTimedPauseUseNaturalDatesAndRefusePastBeforeMutation()
	{
		await PutLoggerInMasterRoomAsync();
		var (actor, handle) = await CreatePlayerAsync($"NaturalLife{Tag}");
		await God1($"&TZ {actor}=America/Chicago");
		await Assert.That(await RunAs(handle, "think dateparse(tomorrow noon,,1792152000)")).Contains("1792256400");
		var id = IdIn(await RunAs(handle, $"+scene/schedule tomorrow noon/Calendar {Tag}=A required pitch"));
		await Assert.That(id).IsNotEmpty();
		var expected = long.Parse(await Eval("dateparse(tomorrow noon,America/Chicago,,ms)"), CultureInfo.InvariantCulture);
		await Assert.That(long.Parse(await Eval($"scene({id},scheduledfor)"))).IsEqualTo(expected);
		await Assert.That(await RunAs(handle, $"+scene/reschedule {id}=next Tuesday 18:30")).Contains("Rescheduled scene");
		var previous = await Eval($"scene({id},scheduledfor)");
		await Assert.That(await RunAs(handle, $"+scene/reschedule {id}=yesterday noon")).Contains("already passed");
		await Assert.That(await Eval($"scene({id},scheduledfor)")).IsEqualTo(previous);
		await RunAs(handle, $"+scene/start {id}");
		await Assert.That(await RunAs(handle, $"+scene/pause {id}=tomorrow evening")).Contains("can't read that as a time");
		await Assert.That(await Eval($"scene({id},status)")).IsEqualTo("active");
		await Assert.That(await RunAs(handle, $"+scene/pause {id}=in a fortnight at 19:00")).Contains("rescheduled for");
		await Assert.That(await Eval($"scene({id},status)")).IsEqualTo("paused");
		await Assert.That(long.Parse(await Eval($"scene({id},scheduledfor)"))).IsGreaterThan(NowMillis);
	}

	[Test]
	public async Task ClockOnlyPastIsRefusedInsteadOfMovingToTomorrow()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"ClockPast{Tag}");
		var before = await RunAs(handle, "think words(scenelist(mine))");
		var said = await RunAs(handle, "+scene/schedule midnight/Past clock=A pitch");
		await Assert.That(said).Contains("already passed");
		await Assert.That(await RunAs(handle, "think words(scenelist(mine))")).IsEqualTo(before);
	}

	[Test]
	public async Task EpochZeroIsAnExplicitSceneHelperReference()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		await Assert.That(await Eval($"u({logger}/FUN`SCHEDULE`WHEN,in 1 hour,UTC,0)")).IsEqualTo("3600000");
	}

	[Test]
	public async Task ExplicitFoldOffsetsAreConfirmedInTheSchedulingReply()
	{
		await PutLoggerInMasterRoomAsync();
		var (actor, handle) = await CreatePlayerAsync($"Fold{Tag}");
		await God1($"&TZ {actor}=America/New_York");
		var first = await RunAs(handle, $"+scene/schedule 2099-11-01T01:30:00-04:00/First clock {Tag}=A pitch");
		var second = await RunAs(handle, $"+scene/schedule 2099-11-01T01:30:00-05:00/Second clock {Tag}=A pitch");
		await Assert.That(first).Contains("01:30:00 -04:00 (-04:00)");
		await Assert.That(second).Contains("01:30:00 -05:00 (-05:00)");
		var firstAt = long.Parse(await Eval($"scene({IdIn(first)},scheduledfor)"));
		var secondAt = long.Parse(await Eval($"scene({IdIn(second)},scheduledfor)"));
		await Assert.That(secondAt - firstAt).IsEqualTo(3600000L);
	}

	[Test]
	[Arguments("+1d", "2027-03-14T17:00:00Z")]
	[Arguments("in 1 day", "2027-03-14T16:00:00Z")]
	public async Task LegacyDayOffsetRetainsElapsedTimeWhileNewCalendarPhrasesRespectDst(string expression, string expected)
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		var reference = DateTimeOffset.Parse("2027-03-13T17:00:00Z", CultureInfo.InvariantCulture).ToUnixTimeSeconds();
		await Assert.That(await Eval($"u({logger}/FUN`SCHEDULE`WHEN,{expression},America/New_York,{reference})"))
			.IsEqualTo(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
	}

	[Test]
	public async Task ScheduleNaturalDatePreservesZoneTitleAndPitchSlashes()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Natural{Tag}");
		var said = await RunAs(handle, $"+scene/schedule tomorrow 7pm America/Chicago/Title / Part {Tag}=Pitch / part = intact");
		var id = IdIn(said);
		await Assert.That(id).IsNotEmpty();
		await Assert.That(await Eval($"scene({id},title)")).IsEqualTo($"Title / Part {Tag}");
		await Assert.That(await Eval($"scene({id},summary)")).IsEqualTo("Pitch / part = intact");
		await Assert.That(long.Parse(await Eval($"scene({id},scheduledfor)"))).IsGreaterThan(NowMillis);
		await Assert.That(said).Contains("19:00:00");
		await Assert.That(said).Contains("(America/Chicago)");
		var rescheduled = await RunAs(handle, $"+scene/reschedule {id}=2099-10-23T19:00:00-05:00 America/Chicago");
		await Assert.That(rescheduled).Contains("2099-10-23 19:00:00 -05:00 (America/Chicago)");
		await RunAs(handle, $"+scene/start {id}");
		var paused = await RunAs(handle, $"+scene/pause {id}=2099-10-24T19:00:00-05:00 America/Chicago");
		await Assert.That(paused).Contains("2099-10-24 19:00:00 -05:00 (America/Chicago)");
	}

	[Test]
	[Arguments("A", "tomorrow")]
	[Arguments("B", "garbage")]
	[Arguments("C", "yesterday")]
	[Arguments("D", "")]
	public async Task Schedule_RefusesATimeItCannotRead(string key, string when)
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Sched{key}{Tag}");
		var mine = await RunAs(handle, "think words(scenelist(mine))");

		var said = await RunAs(handle, $"+scene/schedule {when}/Unread {Tag}=A pitch");

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

		var said = await RunAs(handle, $"+scene/schedule +3d/Relative {Tag}=A pitch");
		var id = IdIn(said);
		await Assert.That(id).IsNotEmpty().Because($"+3d is a time, as +job/due takes it: {said}");
		var at = long.Parse(await Eval($"scene({id},scheduledfor)"));
		var expected = NowMillis + (long)TimeSpan.FromDays(3).TotalMilliseconds;
		await Assert.That(Math.Abs(at - expected)).IsLessThan((long)TimeSpan.FromMinutes(2).TotalMilliseconds);

		var gone = await RunAs(handle, $"+scene/schedule 2020-01-01 10:00/Gone {Tag}=A pitch");
		await Assert.That(gone).Contains("already passed");
		await Assert.That(gone).DoesNotContain("Scheduled scene");

		var usage = await RunAs(handle, $"+scene/schedule No time {Tag}");
		await Assert.That(usage).Contains("+scene/schedule <when>/<title>=<pitch>").Because("no = is a usage line, not Huh?");
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

		var scheduled = IdIn(await RunAs(handle, $"+scene/schedule +2d/Later {Tag}=A pitch"));
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
		await Assert.That(said.IndexOf($"< Scene {id} >", StringComparison.Ordinal)).IsGreaterThan(said.IndexOf($"Thunder rolls {Tag}.", StringComparison.Ordinal))
			.Because("the footer under the pose carries the scene");
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
	[Test]
	public async Task CreationAndSchedulingKeepThePitchAndRefuseMissingPitchWithoutCreating()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Pitch{Tag}");
		var id = await CreateSceneAsync(handle, $"A pitch {Tag}=Bring a lantern = and courage");
		await Assert.That(await Eval($"scene({id},summary)")).IsEqualTo("Bring a lantern = and courage");
		var before = await RunAs(handle, "think words(scenelist(mine))");
		var refused = await RunAs(handle, $"+scene/schedule +3d/No pitch {Tag}=   ");
		await Assert.That(refused).Contains("<pitch>");
		await Assert.That(await RunAs(handle, "think words(scenelist(mine))")).IsEqualTo(before);
		var scheduled = IdIn(await RunAs(handle, $"+scene/schedule +3d/A later pitch {Tag}=A journey = together"));
		await Assert.That(scheduled).IsNotEmpty();
		await Assert.That(await Eval($"scene({scheduled},summary)")).IsEqualTo("A journey = together");
	}

	[Test]
	public async Task JoiningPolicyAndCapApplyToJoinActivateAndWebPoses()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"CapOwner{Tag}");
		var (guest, guestHandle) = await CreatePlayerAsync($"CapGuest{Tag}");
		var (other, otherHandle) = await CreatePlayerAsync($"CapOther{Tag}");
		var id = await CreateSceneAsync(owner, $"Only two {Tag}");
		await RunAs(owner, "+scene/limit 2");
		await RunAs(owner, "+scene/policy invite-only");
		await Assert.That(await RunAs(guestHandle, $"+scene/join {id}")).Contains("invite-only");
		await RunAs(owner, $"+scene/invite {id}={Num(guest)}");
		await Assert.That(await RunAs(guestHandle, $"+scene/join {id}")).Contains("Joined");
		await RunAs(owner, "+scene/policy limited");
		await Assert.That(await RunAs(otherHandle, $"+scene/activate {id}")).Contains("player cap");
		await Assert.That(await RunAs(otherHandle, $"+scene/pose {id}=Uninvited words")).Contains("player cap");
		await Assert.That(await Eval($"scenemember({id},{Num(other)},role)")).StartsWith("#-1");
		await Assert.That(await RunAs(owner, "+scene/limit 1")).Contains("cannot be below");
		await Assert.That(await Eval($"scene({id},playercap)")).IsEqualTo("2");
		await Assert.That(await RunAs(owner, $"+scene {id}")).Contains("limited").And.Contains("cap 2");
	}

	[Test]
	[Arguments("policy")]
	[Arguments("limit")]
	[Arguments("invite")]
	public async Task BareParticipationCommandsShowTheirUsage(string verb)
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Bare{verb}{Tag}");
		await Assert.That(await RunAs(handle, $"+scene/{verb}")).Contains($"+scene/{verb} ");
	}

	[Test]
	public async Task InvitationAcceptanceStillRequiresASeat()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"InviteOwner{Tag}");
		var (guest, guestHandle) = await CreatePlayerAsync($"InviteGuest{Tag}");
		var id = await CreateSceneAsync(owner, $"Private invitation {Tag}");
		await RunAs(owner, "+scene/policy invite-only");
		await RunAs(owner, "+scene/limit 1");
		await RunAs(owner, $"+scene/invite {id}={Num(guest)}");
		await Assert.That(await Eval($"member(scenemembers({id}),{Num(guest)})")).IsEqualTo("0");
		await Assert.That(await RunAs(guestHandle, $"think member(scenelist(mine),{id})")).IsEqualTo("0");
		await RunAs(owner, "+scene/private");
		await Assert.That(await RunAs(guestHandle, $"think scene({id},status)")).IsEqualTo("active")
			.Because("a private scene that has not ended reads like any other");
		await Assert.That(await RunAs(guestHandle, $"+scene/join {id}")).Contains("player cap");
		await RunAs(owner, "+scene/limit 2");
		await Assert.That(await RunAs(guestHandle, $"+scene/join {id}")).Contains("Joined");
		await Assert.That(await RunAs(guestHandle, $"think scene({id},status)")).IsEqualTo("active");
		await RunAs(guestHandle, "+scene/leave");
		await Assert.That(await RunAs(guestHandle, $"+scene/join {id}")).DoesNotContain("Joined");
	}

	/// <summary>
	/// +scene/invite takes several players at once and answers for each: one name that is wrong does not stop
	/// the rest, and each player invited is told. +scene/uninvite withdraws them the same way.
	/// </summary>
	[Test]
	public async Task InvitingSeveralPlayersAnswersForEachAndTellsThem()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"ManyOwner{Tag}");
		var (alice, _) = await CreatePlayerAsync($"ManyAlice{Tag}");
		var (bob, _) = await CreatePlayerAsync($"ManyBob{Tag}");
		var id = await CreateSceneAsync(owner, $"Many invited {Tag}");
		var aliceHeard = Notifications.CountFor(alice);

		var said = await RunAs(owner, $"+scene/invite {id}=ManyAlice{Tag}, Nobody{Tag},ManyBob{Tag}");

		await Assert.That(said).Contains($"Invited ManyAlice{Tag} to scene {id}.")
			.And.Contains($"No such player: Nobody{Tag}")
			.And.Contains($"Invited ManyBob{Tag} to scene {id}.");
		await Assert.That(await Eval($"scene({id},invited)")).IsEqualTo(string.Join(" ", new[] { alice, bob }.OrderBy(d => d.Number).Select(Num)));
		await Assert.That(string.Join("\n", Notifications.For(alice).Skip(aliceHeard)))
			.Contains($"ManyOwner{Tag} invited you to scene {id}: Many invited {Tag}").And.Contains($"+scene/join {id}");
		await Assert.That(await RunAs(owner, $"+scene {id}")).Contains("Invited").And.Contains($"ManyBob{Tag}");

		said = await RunAs(owner, $"+scene/uninvite {id}=ManyBob{Tag},ManyBob{Tag}");
		await Assert.That(said).Contains($"Withdrew ManyBob{Tag}'s invitation to scene {id}.")
			.And.Contains($"ManyBob{Tag} has no invitation to scene {id}.");
		await Assert.That(await Eval($"scene({id},invited)")).IsEqualTo(Num(alice));
	}

	/// <summary>
	/// A private invite-only scene is kept to its list while it runs: an outsider can't read it, find it, join it
	/// or RSVP to it, and an invited player can do all of those. A public invite-only scene is readable by anyone,
	/// but only an invited player may RSVP.
	/// </summary>
	[Test]
	public async Task APrivateInviteOnlySceneIsSeenOnlyByItsList()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"HideOwner{Tag}");
		var (insider, insiderHandle) = await CreatePlayerAsync($"HideIn{Tag}");
		var (_, outsiderHandle) = await CreatePlayerAsync($"HideOut{Tag}");
		var id = await CreateSceneAsync(owner, $"Behind doors {Tag}");
		await RunAs(owner, $"+scene/invite {id}={Num(insider)}");
		await RunAs(owner, $"+scene/policy {id}=invite-only");

		await Assert.That(await RunAs(outsiderHandle, $"+scene/rsvp {id}")).Contains("invite-only")
			.Because("a public invite-only scene can be read, but only its invited players may RSVP");
		await Assert.That(await RunAs(outsiderHandle, $"+scene/join {id}"))
			.Contains("This scene is invite-only. Ask its owner for an invitation.").And.DoesNotContain("#-1");
		await Assert.That(await RunAs(outsiderHandle, $"think scene({id},status)")).IsEqualTo("active");

		await Assert.That(await RunAs(owner, $"+scene/private {id}")).Contains("only its owner, its members and the players invited to it can see it");
		await Assert.That(await RunAs(outsiderHandle, $"think scene({id},status)")).IsEqualTo("#-1 PERMISSION");
		await Assert.That(await RunAs(outsiderHandle, $"think member(scenelist(active),{id})")).IsEqualTo("0");
		await Assert.That(await RunAs(outsiderHandle, $"+scene {id}")).Contains($"No such scene: {id}");
		await Assert.That(await RunAs(outsiderHandle, $"+scene/join {id}")).Contains($"No such scene: {id}")
			.And.DoesNotContain("invite-only");
		await Assert.That(await RunAs(outsiderHandle, $"+scene/rsvp {id}")).Contains($"No such scene: {id}");

		await Assert.That(await RunAs(insiderHandle, $"think scene({id},status)")).IsEqualTo("active");
		await Assert.That(await RunAs(insiderHandle, $"think member(scenelist(active),{id})")).IsEqualTo("1");
		await Assert.That(await RunAs(insiderHandle, $"+scene {id}")).Contains($"Behind doors {Tag}").And.Contains("Members and invited players");
		await Assert.That(await RunAs(insiderHandle, $"+scene/join {id}")).Contains("Joined");
	}

	/// <summary>
	/// +scene/schedule/private/invite-only makes the scene private and invite-only in the command that creates it,
	/// so no one else can read or join it before those apply.
	/// </summary>
	[Test]
	public async Task ScheduleSwitchesHideTheSceneFromTheStart()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"SchHide{Tag}");
		var (_, outsiderHandle) = await CreatePlayerAsync($"SchOut{Tag}");

		var said = await RunAs(owner, $"+scene/schedule/private/invite-only +2d/Quiet plans {Tag}=Not for everyone.");
		var id = IdIn(said);
		await Assert.That(said).Contains("It is private and invite-only.");
		await Assert.That(await Eval($"scene({id},public)")).IsEqualTo("0");
		await Assert.That(await Eval($"scene({id},joinpolicy)")).IsEqualTo("invite-only");
		await Assert.That(await RunAs(outsiderHandle, $"think scene({id},status)")).IsEqualTo("#-1 PERMISSION");

		said = await RunAs(owner, $"+scene/schedule/invite-only +2d/Open doors {Tag}=Anyone may read it.");
		var open = IdIn(said);
		await Assert.That(said).Contains("It is invite-only.");
		await Assert.That(await Eval($"scene({open},public)")).IsEqualTo("1");
		await Assert.That(await RunAs(outsiderHandle, $"think scene({open},status)")).IsEqualTo("scheduled");

		await Assert.That(await Eval($"scenecreate(,me,Capped {Tag},A pitch,,0,limited)"))
			.IsEqualTo("#-1 A new scene's join policy is open or invite-only.");
	}

	/// <summary>A name given to +scene/invite is used as a name, never run again as the package's code.</summary>
	[Test]
	public async Task InvitedNamesAreTakenLiterally()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"LitOwner{Tag}");
		var id = await CreateSceneAsync(owner, $"Literal names {Tag}");

		// The typed line is evaluated as the player, as PennMUSH does, so the escapes leave the code as text; the
		// package must then use each name as a value and never evaluate it again as itself.
		var said = await RunAs(owner, $@"+scene/invite {id}=\[ucstr(ab)\]{Tag}, Bo\(b{Tag}, \%#{Tag}");

		await Assert.That(said).Contains($"No such player: [ucstr(ab)]{Tag}")
			.And.Contains($"No such player: Bo(b{Tag}")
			.And.Contains($"No such player: %#{Tag}");
	}

	/// <summary>
	/// Staff holding scene.manage may change any scene, so they see a private invite-only scene too and can manage its
	/// list; a player without it still can't. Taking an invitation back also takes back that player's RSVP.
	/// </summary>
	[Test]
	public async Task SceneManagersSeeAHiddenSceneAndUninvitingTakesBackTheRsvp()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"MgrOwner{Tag}");
		var (manager, managerHandle) = await CreatePlayerAsync($"MgrStaff{Tag}");
		var (guest, guestHandle) = await CreatePlayerAsync($"MgrGuest{Tag}");
		var (_, outsiderHandle) = await CreatePlayerAsync($"MgrOut{Tag}");
		var id = await CreateSceneAsync(owner, $"Staff eyes {Tag}");
		await RunAs(owner, $"+scene/policy {id}=invite-only");
		await RunAs(owner, $"+scene/private {id}");
		await God1($"@permission/allow {Num(manager)}=scene.manage");

		await Assert.That(await RunAs(outsiderHandle, $"think scene({id},status)")).IsEqualTo("#-1 PERMISSION");
		await Assert.That(await RunAs(managerHandle, $"think scene({id},status)")).IsEqualTo("active");
		await Assert.That(await RunAs(managerHandle, $"+scene/invite {id}={Num(guest)}"))
			.Contains($"Invited MgrGuest{Tag} to scene {id}.");

		await Assert.That(await RunAs(guestHandle, $"+scene/rsvp {id}")).DoesNotContain("invite-only").And.DoesNotContain("No such scene");
		await Assert.That(await Eval($"member(scenemembers({id}),{Num(guest)})")).IsNotEqualTo("0");
		await RunAs(managerHandle, $"+scene/uninvite {id}={Num(guest)}");
		await Assert.That(await Eval($"member(scenemembers({id}),{Num(guest)})")).IsEqualTo("0")
			.Because("an RSVP to an invite-only scene goes with the invitation");
		await Assert.That(await RunAs(guestHandle, $"think scene({id},status)")).IsEqualTo("#-1 PERMISSION");
	}

	/// <summary>
	/// Leaving makes a player inactive: unfocused, still on the cast while the scene has a pose of theirs. A
	/// player with none, or only deleted ones, comes off the cast; the owner never does.
	/// </summary>
	[Test]
	public async Task LeavingMarksPlayersInactiveAndDropsOnlyThoseWithNoPoses()
	{
		await PutLoggerInMasterRoomAsync();
		var (owner, ownerHandle) = await CreatePlayerAsync($"LeaveOwner{Tag}");
		var (poser, poserHandle) = await CreatePlayerAsync($"LeavePoser{Tag}");
		var (undone, undoneHandle) = await CreatePlayerAsync($"LeaveUndone{Tag}");
		var (silent, silentHandle) = await CreatePlayerAsync($"LeaveSilent{Tag}");
		var id = await CreateSceneAsync(ownerHandle, $"Leaving {Tag}");
		foreach (var handle in new[] { poserHandle, undoneHandle, silentHandle })
			await Assert.That(await RunAs(handle, $"+scene/join {id}")).Contains("Joined");
		await RunAs(poserHandle, $"+scene/pose {id}=Kept words {Tag}");
		await RunAs(undoneHandle, $"+scene/pose {id}=Taken back {Tag}");
		var undonePose = await Eval($"last(sceneposes({id},{Num(undone)}))");
		await Assert.That(await RunAs(undoneHandle, $"+scene/delete {undonePose}")).Contains("deleted");

		await Assert.That(await RunAs(ownerHandle, "+scene/leave")).Contains("You own scene");
		await Assert.That(await Eval($"scenemember({id},{Num(owner)},role)")).IsEqualTo("owner");
		await Assert.That(await Eval($"scenefocus({Num(owner)})")).StartsWith("#-1");

		await Assert.That(await RunAs(poserHandle, "+scene/leave")).Contains("stay on its cast as inactive");
		await Assert.That(await Eval($"scenemember({id},{Num(poser)},role)")).IsEqualTo("participant");
		await Assert.That(await Eval($"scenefocus({Num(poser)})")).StartsWith("#-1");

		foreach (var (gone, handle) in new[] { (undone, undoneHandle), (silent, silentHandle) })
		{
			await Assert.That(await RunAs(handle, "+scene/leave")).Contains($"Left scene {id}.");
			await Assert.That(await Eval($"scenemember({id},{Num(gone)},role)")).StartsWith("#-1");
		}
	}

	[Test]
	public async Task ParticipationVerbsUseUniquePrefixesAndAmbiguitiesDoNotMutate()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, owner) = await CreatePlayerAsync($"PrefixOwner{Tag}");
		var (guest, guestHandle) = await CreatePlayerAsync($"PrefixGuest{Tag}");
		var id = await CreateSceneAsync(owner, $"Prefix scene {Tag}");
		await Assert.That(await RunAs(owner, "+scene/lim 2")).Contains("Player cap set to 2");
		await Assert.That(await RunAs(owner, "+scene/pol invite-only")).Contains("Joining policy set to invite-only");
		await Assert.That(await RunAs(owner, $"+scene/inv {id}={Num(guest)}")).Contains("Invited");
		await Assert.That(await RunAs(owner, $"+scene/inf {id}")).Contains($"Scene {id}:");
		await Assert.That(await RunAs(owner, "+scene/lis")).Contains("Scenes: active");
		var poses = await Eval($"words(sceneposes({id}))");
		foreach (var abbreviated in new[] { $"po {id}=Never posed", "li 999", $"i {id}", $"in {id}" })
			await Assert.That(await RunAs(owner, $"+scene/{abbreviated}")).Contains("could be");
		await Assert.That(await Eval($"words(sceneposes({id}))")).IsEqualTo(poses);
		await Assert.That(await Eval($"scene({id},playercap)")).IsEqualTo("2");
		await Assert.That(await RunAs(guestHandle, $"+scene/join {id}")).Contains("Joined");
		await Assert.That(await RunAs(guestHandle, $"+scene/pos {id}=A real pose")).DoesNotContain("could be");
		await Assert.That(await Eval($"words(sceneposes({id}))")).IsEqualTo((int.Parse(poses) + 1).ToString());
	}

	/// <summary>
	/// +scene/go takes a player to the room of a running scene and nowhere else: a paused or finished scene is
	/// refused, as is going where they already stand.
	/// </summary>
	[Test]
	public async Task GoingTravelsOnlyToARunningScene()
	{
		await PutLoggerInMasterRoomAsync();
		var (owner, ownerHandle) = await CreatePlayerAsync($"GoOwner{Tag}");
		var (traveler, travelerHandle) = await CreatePlayerAsync($"GoTraveler{Tag}");
		var sceneRoom = await Eval($"loc({Num(owner)})");
		var id = await CreateSceneAsync(ownerHandle, $"Going {Tag}");
		var elsewhere = (await God1($"@dig {TestIsolationHelpers.GenerateUniqueName("GoElsewhere")}")).Message.ToPlainText().Trim().Split(':')[0];
		await God1($"@tel {Num(traveler)}={elsewhere}");

		await Assert.That(await RunAs(travelerHandle, $"+scene/go {id}")).Contains($"+scene/join {id} joins it");
		await Assert.That(await Eval($"loc({Num(traveler)})")).IsEqualTo(sceneRoom);
		await Assert.That(await RunAs(travelerHandle, $"+scene/g {id}")).Contains("already where");

		await God1($"@tel {Num(traveler)}={elsewhere}");
		await Assert.That(await RunAs(ownerHandle, "+scene/pause")).Contains("paused");
		await Assert.That(await RunAs(travelerHandle, $"+scene/go {id}")).Contains("is paused");
		await Assert.That(await RunAs(ownerHandle, "+scene/start")).Contains("now active");
		await Assert.That(await RunAs(ownerHandle, "+scene/finish")).Contains("finished");
		await Assert.That(await RunAs(travelerHandle, $"+scene/go {id}")).Contains("is finished");
		await Assert.That(await Eval($"loc({Num(traveler)})")).IsEqualTo(elsewhere);
	}

	/// <summary>The Where cell of a running scene in +scene is a link to +scene/go; a finished scene's is text.</summary>
	[Test]
	public async Task ListLinksARunningScenesRoomToGo()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, ownerHandle) = await CreatePlayerAsync($"GoLister{Tag}");
		var id = await CreateSceneAsync(ownerHandle, $"Listed {Tag}");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);

		var where = await FunctionParser.EvaluateAsync(MarkupText.Plain($"u({logger}/FUN`LIST`WHERE,{id})"));
		await Assert.That(where.ToPlainText()).IsEqualTo(await Eval($"scene({id},roomname)"));
		await Assert.That(where.Render(MarkupFormat.Pueblo)).Contains($"XCH_CMD=\"+scene/go {id}\"");

		await Assert.That(await RunAs(ownerHandle, "+scene/finish")).Contains("finished");
		var finished = await FunctionParser.EvaluateAsync(MarkupText.Plain($"u({logger}/FUN`LIST`WHERE,{id})"));
		await Assert.That(finished.Render(MarkupFormat.Pueblo)).DoesNotContain("XCH_CMD");
	}
}
