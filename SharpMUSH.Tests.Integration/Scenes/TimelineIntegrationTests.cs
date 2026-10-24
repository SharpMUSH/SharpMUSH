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
/// The Scene plugin's <c>SCENE`STATUS</c> event, and the bundled timeline package built on it: a scene is dated in the
/// time package's IC calendar when it starts, <c>+scene/date</c> dates one by hand, and <c>+timeline</c> lists a
/// character's finished scenes in IC order. Each date a test asserts is one it typed, so nothing here reads the real
/// clock's date. Not in parallel: the details and the event handler's attributes are the game's.
/// </summary>
[NotInParallel]
public class TimelineIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	// Ordinary triggers may enqueue more triggers; command completion includes those descendants.
	private ValueTask SettleCommandsAsync() => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>()
		.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task God(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await SettleCommandsAsync();
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		await SettleCommandsAsync();
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await SettleCommandsAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private async Task<string> ObjectAsync(string package, string reference)
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objid = DBRef.Parse((await registry.GetPackageObjectsAsync(package)).Single(o => o.Ref == reference).Objid);
		return $"#{objid.Number}";
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

	/// <summary>A scene <paramref name="owner"/> creates, which starts at once and is focused, and its id.</summary>
	private async Task<string> SceneAsync(TestIsolationHelpers.TestPlayer owner, string title)
	{
		await As(owner, $"+scene/create {title}");
		return await Eval($"scenefocus(#{owner.DbRef.Number})");
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
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		await SettleCommandsAsync();
	}

	private async Task UninstallAsync()
	{
		await Installer.UninstallAsync("timeline", force: true, CancellationToken.None);
		await Installer.UninstallAsync("time", force: true, CancellationToken.None);
	}

	[Test]
	public async Task A_scene_raises_its_status_when_made_and_each_time_it_changes()
	{
		var room = await RoomAsync("StatusRoom");
		var owner = await PlayerAsync("StatOwner", room);
		var key = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();
		var record = $"#{owner.DbRef.Number}/STATUSLOG";
		try
		{
			// Every status a scene of this owner's goes through, appended to an attribute on the owner.
			await God($"&SCENE`STATUS`{key} #9=@assert strmatch(scene(%<scene>,owner),#{owner.DbRef.Number}); &STATUSLOG #{owner.DbRef.Number}=[get({record})] %<scene>:%<status>:[firstof(%<previous>,-)]:[name(%#)]");
			var scene = await SceneAsync(owner, TestIsolationHelpers.GenerateUniqueName("StatusScene"));
			await As(owner, "+scene/pause");
			await As(owner, "+scene/start");
			await As(owner, "+scene/start");
			await As(owner, "+scene/finish");

			await Assert.That(await Eval($"get({record})")).IsEqualTo(string.Join(" ",
					$"{scene}:new:-:{owner.Name}", $"{scene}:active:new:{owner.Name}", $"{scene}:paused:active:{owner.Name}", $"{scene}:active:paused:{owner.Name}",
					$"{scene}:finished:active:{owner.Name}"))
				.Because("+scene/create makes the scene, new, and starts it; the second +scene/start changes nothing; %# is the player");
		}
		finally
		{
			await God($"&SCENE`STATUS`{key} #9");
		}
	}

	[Test]
	public async Task A_scene_is_dated_when_it_starts_and_by_hand_and_the_timeline_follows_the_ic_order()
	{
		var room = await RoomAsync("TimelineRoom");
		var owner = await PlayerAsync("TlOwner", room);
		var other = await PlayerAsync("TlOther", room);
		try
		{
			await InstallAsync("time");
			await InstallAsync("timeline");
			var clock = await ObjectAsync("time", "clock");
			var timeline = await ObjectAsync("timeline", "timeline");

			await Assert.That(await Eval("scenedetail(icdate,sort)")).IsEqualTo("icsecs");
			await Assert.That(await Eval("scenedetail(icsecs,hidden)")).IsEqualTo("1");

			// Started by +scene/create: dated then, in the IC calendar, words and seconds together.
			var first = await SceneAsync(owner, TestIsolationHelpers.GenerateUniqueName("Storm"));
			var stamped = await Eval($"scene({first},icsecs)");
			await Assert.That(long.TryParse(stamped, out _)).IsTrue().Because("a scene gets the IC moment when it starts");
			await Assert.That(await Eval($"scene({first},icdate)")).IsEqualTo(await Eval($"u({clock}/FUN`IC`WRITE,timefmt($Y $m $d $w $H $M $S,{stamped},UTC))"));
			await Assert.That(await Eval($"scene({first},details)")).IsEqualTo("icdate").Because("icsecs is kept for code, not shown");

			// Dated by hand, as a flashback, with a time and without; the date a pause resumes keeps.
			await Assert.That(await As(owner, "+scene/date 2026-03-14 19:30")).Contains($"[SCENE] Done: Scene {first} takes place on Saturday, 14 March 2026, 19:30.");
			await Assert.That(await Eval($"scene({first},icsecs)")).IsEqualTo(await Eval("convtime(2026-03-14T19:30:00Z)"));
			await As(owner, "+scene/pause");
			await As(owner, "+scene/start");
			await Assert.That(await Eval($"scene({first},icsecs)")).IsEqualTo(await Eval("convtime(2026-03-14T19:30:00Z)"))
				.Because("a scene resumed from a pause keeps the date it had");
			await Assert.That(await As(other, $"+scene/date {first}")).Contains($"Scene {first} takes place on Saturday, 14 March 2026, 19:30.");
			await As(owner, "+scene/finish");

			var second = await SceneAsync(owner, TestIsolationHelpers.GenerateUniqueName("Dawn"));
			await Assert.That(await As(owner, $"+scene/date {second}=2026-01-02")).Contains("Friday, 2 January 2026, 00:00.");
			await As(owner, "+scene/finish");

			// The years the calendar adds are the ones typed: a game set 300 years on writes 2326.
			await God($"&DATA`CONFIG`IC-YEARS {clock}=300");
			var third = await SceneAsync(owner, TestIsolationHelpers.GenerateUniqueName("Later"));
			await Assert.That(await As(owner, "+scene/date 2326-06-01 08:05")).Contains("Monday, 1 June 2326, 08:05.");
			await Assert.That(await Eval($"scene({third},icsecs)")).IsEqualTo(await Eval("convtime(2026-06-01T08:05:00Z)"));
			await As(owner, "+scene/finish");

			// What is not a date, and who may not date a scene.
			await Assert.That(await As(owner, $"+scene/date {third}=2326-02-30")).Contains("'2326-02-30' is not a date in the IC calendar.");
			await Assert.That(await As(owner, $"+scene/date {third}=2326-02-03 7:5")).Contains("Write the IC date year first");
			await Assert.That(await As(owner, $"+scene/date {third}=next tuesday")).Contains("Write the IC date year first");
			await Assert.That(await As(other, $"+scene/date {third}=2326-01-01")).Contains("not yours to change");

			// The timeline: IC order, not the order they were played in.
			var shown = await As(other, $"+timeline {owner.Name}");
			await Assert.That(shown).Contains($"Timeline: {owner.Name}").And.Contains("2 January 2026").And.Contains("14 March 2026");
			await Assert.That(shown.IndexOf(await Eval($"scene({second},title)"), StringComparison.Ordinal))
				.IsLessThan(shown.IndexOf(await Eval($"scene({first},title)"), StringComparison.Ordinal))
				.Because("the scene dated 2 January was played after the one dated 14 March, and comes first");
			await Assert.That(shown.IndexOf(await Eval($"scene({first},title)"), StringComparison.Ordinal))
				.IsLessThan(shown.IndexOf(await Eval($"scene({third},title)"), StringComparison.Ordinal));
			await Assert.That(await Eval($"u({timeline}/FUN`TIMELINE`SCENES,#{owner.DbRef.Number})")).IsEqualTo($"{second} {first} {third}");

			// The words alone can change; the scene keeps its place.
			await As(owner, $"+scene/set {second}/icdate=The night of the storm");
			await Assert.That(await Eval($"u({timeline}/FUN`TIMELINE`SCENES,#{owner.DbRef.Number})")).IsEqualTo($"{second} {first} {third}");

			// Cleared: last, and marked.
			await Assert.That(await As(owner, $"+scene/date/clear {second}")).Contains($"Scene {second} has no IC date now.");
			await Assert.That(await Eval($"u({timeline}/FUN`TIMELINE`SCENES,#{owner.DbRef.Number})")).IsEqualTo($"{first} {third} {second}");
			await Assert.That(await As(owner, "+timeline")).Contains("not dated");
			await Assert.That(await As(owner, "+timeline/9")).Contains("There is one page - you asked for page 9.");
			await Assert.That(await As(owner, "+timeline/x")).Contains("+timeline has no /x switch.");
			await Assert.That(await As(other, "+timeline")).Contains("You have no finished scenes yet.");
			await Assert.That(await As(other, "+timeline NoSuchPlayerAnywhere")).Contains("There is no player called 'NoSuchPlayerAnywhere'.");
			await Assert.That(await As(other, "+help timeline")).Contains("+timeline/2");
			await Assert.That(await As(other, "+help scene date")).Contains("+scene/date/clear");

			// +config timeline/stamp=no: a scene starts undated.
			await God("+config timeline/stamp=no");
			await Assert.That(await Eval($"get({timeline}/DATA`CONFIG`STAMP)")).IsEqualTo("0");
			var fourth = await SceneAsync(owner, TestIsolationHelpers.GenerateUniqueName("Quiet"));
			await Assert.That(await Eval($"scene({fourth},icsecs)")).IsEmpty().Because("stamping is off");
			await As(owner, "+scene/finish");
		}
		finally
		{
			await UninstallAsync();
		}

		await Assert.That(await Eval("scenedetail()")).DoesNotContain("icdate").Because("uninstalling takes the details away");
	}

	[Test]
	public async Task A_private_scene_stays_off_another_players_timeline()
	{
		var room = await RoomAsync("PrivateRoom");
		var owner = await PlayerAsync("PvOwner", room);
		var other = await PlayerAsync("PvOther", room);
		try
		{
			await InstallAsync("time");
			await InstallAsync("timeline");
			var open = await SceneAsync(owner, TestIsolationHelpers.GenerateUniqueName("Open"));
			await As(owner, "+scene/finish");
			var hidden = await SceneAsync(owner, TestIsolationHelpers.GenerateUniqueName("Hidden"));
			await As(owner, "+scene/private");
			await As(owner, "+scene/finish");

			var timeline = await ObjectAsync("timeline", "timeline");
			await Assert.That(await Eval($"u({timeline}/FUN`TIMELINE`SCENES,#{owner.DbRef.Number})")).Contains(hidden)
				.Because("staff see every scene");
			var seen = await As(other, $"+timeline {owner.Name}");
			await Assert.That(seen).Contains(await Eval($"scene({open},title)")).And.DoesNotContain(await Eval($"scene({hidden},title)"));
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
