using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled availability package, installed from the catalogue with the time package it builds on, and the
/// scene-availability bridge on top of it where a test needs scenes: +avail reads a typed &lt;when&gt; into an entry, the entries expand into the moments they cover in their own
/// zone, and the free slots of several players are laid over each other. Entries the moment-dependent tests
/// read are written straight onto the Calendar with fixed dates, and every function is handed its moment, so
/// nothing here depends on the real clock's date. Not in parallel: +avail is global.
/// </summary>
[NotInParallel]
public class SceneAvailabilityIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private string Avail { get; set; } = string.Empty;
	private string Calendar { get; set; } = string.Empty;

	/// <summary>2026-10-15, a Thursday: the day the reading tests take as today.</summary>
	private const int Today = 20741;

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	private Task<string> Think(string expression) => God($"think {expression}");

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>A connected, approved player in a room of its own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		await God($"@role/assign #{player.DbRef.Number}=approved");
		return player;
	}

	private PackagesController Controller() => new(
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

	private async Task ApplyAsync(string package)
	{
		var applied = await Controller().Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private bool WithScenes { get; set; }

	/// <summary>Installs time and availability, and the scene-availability bridge when <paramref name="withScenes"/>.</summary>
	private async Task InstallAsync(bool withScenes = false)
	{
		await ApplyAsync("time");
		await ApplyAsync("availability");
		WithScenes = withScenes;
		if (withScenes) await ApplyAsync("scene-availability");
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>()
			.GetPackageObjectsAsync("availability");
		Avail = objects.First(o => o.Ref == "availability").Objid;
		Calendar = objects.First(o => o.Ref == "calendar").Objid;
	}

	private async Task UninstallAsync()
	{
		if (WithScenes) await Installer.UninstallAsync("scene-availability", force: true, CancellationToken.None);
		await Installer.UninstallAsync("availability", force: true, CancellationToken.None);
		await Installer.UninstallAsync("time", force: true, CancellationToken.None);
	}

	private Task<string> KeyOf(TestIsolationHelpers.TestPlayer player) => Think($"u({Avail}/FUN`KEY,#{player.DbRef.Number})");

	/// <summary>Writes entry <paramref name="number"/> under <paramref name="key"/> leaf by leaf, as +avail does.</summary>
	private async Task EntryAsync(string key, int number, string kind, params (string Leaf, string Value)[] leaves)
	{
		await God($"&PLAYER`{key}`{number} {Calendar}={kind}");
		await God($"&PLAYER`{key}`{number}`KIND {Calendar}={kind}");
		foreach (var (leaf, value) in leaves)
		{
			await God($"&PLAYER`{key}`{number}`{leaf} {Calendar}={value}");
		}
	}

	/// <summary>The registers FUN`READ`WHEN leaves for <paramref name="when"/>, in UTC with today 2026-10-15.</summary>
	private Task<string> Read(string when) => Think(
		$"u({Avail}/FUN`READ`WHEN,{when},UTC,{Today})%q<read_error>|%q<read_repeat>|%q<read_every>|%q<read_days>|" +
		"[if(isint(%q<read_date>),u(" + Avail + "/FUN`DAY`DATE,%q<read_date>))]|[if(isint(%q<read_until>),u(" + Avail +
		"/FUN`DAY`DATE,%q<read_until>))]|%q<read_minute>|%q<read_length>");

	[Test]
	public async Task AWhenIsReadIntoARepeat()
	{
		try
		{
			await InstallAsync();

			// error|repeat|every|days|first date|last date|start minute|length in minutes
			await Assert.That(await Read("weekdays 9:00-17:00")).IsEqualTo("|weekly|1|mon tue wed thu fri|2026-10-15||540|480");
			await Assert.That(await Read("every 2 weeks tue 8pm-10pm until 2026-12-31"))
				.IsEqualTo("|weekly|2|tue|2026-10-15|2026-12-31|1200|120");
			await Assert.That(await Read("every other week on thursdays at 8pm-11pm")).IsEqualTo("|weekly|2|thu|2026-10-15||1200|180");
			await Assert.That(await Read("sat sun 6 pm - 11 pm")).IsEqualTo("|weekly|1|sat sun|2026-10-15||1080|300");
			// A run of days round the end of the week, in the week's order.
			await Assert.That(await Read("fri-mon")).IsEqualTo("|weekly|1|mon fri sat sun|2026-10-15||0|1440");
			await Assert.That(await Read("last fri 19:00-23:00")).IsEqualTo("|monthly|1|-1fri|2026-10-15||1140|240");
			await Assert.That(await Read("2nd and 4th thu 19:00-21:00")).IsEqualTo("|monthly|1|2thu 4thu|2026-10-15||1140|120");
			await Assert.That(await Read("1st 15th")).IsEqualTo("|monthly|1|1 15|2026-10-15||0|1440");
			// 9-5 is the working day; 23:00-02:00 runs past midnight.
			await Assert.That(await Read("monthly 9-5")).IsEqualTo("|monthly|1|15|2026-10-15||540|480");
			await Assert.That(await Read("daily 23:00-02:00")).IsEqualTo("|daily|1||2026-10-15||1380|180");
			await Assert.That(await Read("tomorrow 7pm to 10pm")).IsEqualTo("|once|1||2026-10-16||1140|180");
			// Two dates: whole days; with a time, that time each day.
			await Assert.That(await Read("2026-12-20 to 2026-12-28")).IsEqualTo("|once|1||2026-12-20||0|12960");
			await Assert.That(await Read("2026-10-20 to 2026-10-23 18:00-22:00")).IsEqualTo("|daily|1||2026-10-20|2026-10-23|1080|240");
			await Assert.That(await Read("mon 0:00-24:00")).IsEqualTo("|weekly|1|mon|2026-10-15||0|1440");

			await Assert.That(await Read("blorp 9-5")).StartsWith("'blorp' is not a day, a date or a time I can read.");
			await Assert.That(await Read("19:00-22:00")).StartsWith("Say which day:");
			await Assert.That(await Read("tue 19:00")).StartsWith("Give an end time too");
			await Assert.That(await Read("tue 9")).StartsWith("'9' is not a day");
			await Assert.That(await Read("2026-01-01 19:00-20:00")).StartsWith("That date has passed.");
			await Assert.That(await Read("2026-12-28 to 2026-12-20")).StartsWith("It ends before it starts.");
			await Assert.That(await Read("2026-02-30")).StartsWith("'2026-02-30' is not a day");
			await Assert.That(await Read("last 19:00-20:00")).StartsWith("Name a day after last");
			await Assert.That(await Read("every 99 days")).StartsWith("Every 99 is not a repeat I can keep.");
			await Assert.That(await Read("tue 19:00-19:00")).StartsWith("The time starts and ends at the same moment.");

			await Assert.That(await Think($"iter(2h 90m 1h30m 3 1.5 1h30 h abc,u({Avail}/FUN`READ`LENGTH,%i0),,|)"))
				.IsEqualTo("7200|5400|5400|10800|5400|5400||");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task RepeatsLandOnTheirDaysInTheirZone()
	{
		try
		{
			await InstallAsync();
			const string key = "T_1";

			// Mondays at 19:00 in New York: 23:00 UTC in summer time, midnight UTC once it ends on 1 November.
			await EntryAsync(key, 1, "busy", ("REPEAT", "weekly"), ("DAYS", "mon"), ("DATE", "2026-10-26"),
				("TIME", "19:00"), ("MINUTES", "120"), ("ZONE", "America/New_York"));
			await Assert.That(await Think($"u({Avail}/FUN`ENTRY`SPANS,{key},1,1792972800,1794182400)"))
				.IsEqualTo("1793055600:1793062800 1793664000:1793671200");

			// The 2nd and 4th Thursday and the last Friday of each month: 8 and 22 Oct, 30 Oct, 12 and 26 Nov, 27 Nov.
			await EntryAsync(key, 2, "busy", ("REPEAT", "monthly"), ("DAYS", "2thu 4thu -1fri"), ("DATE", "2026-10-01"),
				("TIME", "19:00"), ("MINUTES", "60"), ("ZONE", "UTC"));
			await Assert.That(await Think($"u({Avail}/FUN`ENTRY`SPANS,{key},2,1790812800,1796083200)"))
				.IsEqualTo("1791486000:1791489600 1792695600:1792699200 1793386800:1793390400 " +
					"1794510000:1794513600 1795719600:1795723200 1795806000:1795809600");

			// Every other Tuesday from 20 October, 17 November left out: 20 Oct and 3 Nov only.
			await EntryAsync(key, 3, "busy", ("REPEAT", "weekly"), ("EVERY", "2"), ("DAYS", "tue"), ("DATE", "2026-10-20"),
				("TIME", "20:00"), ("MINUTES", "120"), ("ZONE", "UTC"), ("SKIP", "2026-11-17"));
			await Assert.That(await Think($"u({Avail}/FUN`ENTRY`SPANS,{key},3,1790812800,1796083200)"))
				.IsEqualTo("1792526400:1792533600 1793736000:1793743200");

			// The 31st passes over November.
			await EntryAsync(key, 4, "busy", ("REPEAT", "monthly"), ("DAYS", "31"), ("DATE", "2026-10-01"),
				("TIME", "12:00"), ("MINUTES", "60"), ("ZONE", "UTC"));
			await Assert.That(await Think($"u({Avail}/FUN`ENTRY`SPANS,{key},4,1790812800,1799000000)"))
				.IsEqualTo("1793448000:1793451600 1798718400:1798722000");

			// Overnight, two nights: the first is caught from before the window opens, at 23:00 the day before.
			await EntryAsync(key, 5, "busy", ("REPEAT", "daily"), ("DATE", "2026-10-20"), ("UNTIL", "2026-10-21"),
				("TIME", "23:00"), ("MINUTES", "180"), ("ZONE", "UTC"));
			await Assert.That(await Think($"u({Avail}/FUN`ENTRY`SPANS,{key},5,1792540000,1796083200)"))
				.IsEqualTo("1792537200:1792548000 1792623600:1792634400");

			await Assert.That(await Think($"iter(1 2 3 4 5,u({Avail}/FUN`ENTRY`DESCRIBE,{key},%i0,UTC),,|)"))
				.IsEqualTo("Every Mon, 19:00-21:00, from Mon 26 Oct · America/New_York|" +
					"The 2nd, 4th Thu, and last Fri of each month, 19:00-20:00, from Thu 1 Oct|" +
					"Every 2 weeks on Tue, 20:00-22:00, from Tue 20 Oct, not Tue 17 Nov|" +
					"The 31st of each month, 12:00-13:00, from Thu 1 Oct|" +
					"Every day, 23:00-02:00 next day, from Tue 20 Oct, until Wed 21 Oct 2026");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task FreeTimesAreLaidOverEachOther()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("AvAnn");
			var bob = await Player("AvBob");
			var cleo = await Player("AvCleo");
			var annKey = await KeyOf(ann);
			var bobKey = await KeyOf(bob);

			// Ann is busy on weekdays from 9 to 5 UTC; Bob is free only at weekends from 10 to 2 in London, which
			// leaves summer time on 25 October.
			await EntryAsync(annKey, 1, "busy", ("REPEAT", "weekly"), ("DAYS", "mon tue wed thu fri"), ("DATE", "2026-10-01"),
				("TIME", "09:00"), ("MINUTES", "480"), ("ZONE", "UTC"));
			await EntryAsync(bobKey, 1, "free", ("REPEAT", "weekly"), ("DAYS", "sat sun"), ("DATE", "2026-10-01"),
				("TIME", "10:00"), ("MINUTES", "240"), ("ZONE", "Europe/London"));

			// Monday 19 October: Ann is busy from 10 to 12 and free from 18 to 20; Bob, with free times kept, is free
			// only inside them; Cleo set nothing.
			await Assert.That(await Think($"u({Avail}/FUN`FREE`AT,#{ann.DbRef.Number},1792404000,1792411200)")).IsEqualTo("0");
			await Assert.That(await Think($"u({Avail}/FUN`FREE`AT,#{ann.DbRef.Number},1792432800,1792440000)")).IsEqualTo("1");
			await Assert.That(await Think($"u({Avail}/FUN`FREE`AT,#{bob.DbRef.Number},1792432800,1792440000)")).IsEqualTo("0");
			await Assert.That(await Think($"u({Avail}/FUN`FREE`AT,#{cleo.DbRef.Number},1792432800,1792440000)")).IsEqualTo(string.Empty);

			// The week from Monday 19 October, in 30-minute slots, at least two hours: Saturday 09:00-13:00 UTC (summer
			// time in London) and Sunday 10:00-14:00 UTC (not).
			await Assert.That(await Think(
					$"u({Avail}/FUN`FIND`RUNS,[objid(#{ann.DbRef.Number})] [objid(#{bob.DbRef.Number})] [objid(#{cleo.DbRef.Number})],1792368000,336,4)"))
				.IsEqualTo("1792832400:1792846800 1792922400:1792936800");
			// Ann alone: Monday until 9, each weekday night from 17 to 9, and Friday 17:00 to the end of the week.
			await Assert.That(await Think($"words(u({Avail}/FUN`FIND`RUNS,objid(#{ann.DbRef.Number}),1792368000,336,4))"))
				.IsEqualTo("6");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task EntriesAreAddedSkippedAndRemoved()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("AvDee");
			var bob = await Player("AvEli");
			var bobName = await Think($"name(#{bob.DbRef.Number})");
			await God($"&TZ #{ann.DbRef.Number}=America/New_York");

			await Assert.That(await As(ann, "+avail")).Contains("You have set no times.");
			await Assert.That(await As(ann, "+avail/busy weekdays 9:00-17:00=Work"))
				.Contains("[AVAIL] Done: Added 1, busy: Weekdays, 09:00-17:00.");
			await Assert.That(await As(ann, "+avail/free sat sun 6pm-11pm")).Contains("Added 2, free: Weekends, 18:00-23:00.");
			await Assert.That(await As(ann, "+avail/busy 2nd and 4th thu 19:00-21:00=Book club,  [and] more"))
				.Contains("Added 3, busy: The 2nd and 4th Thu of each month, 19:00-21:00.");
			// The note is kept as typed, never evaluated.
			var annKey = await KeyOf(ann);
			await Assert.That(await Think($"get({Calendar}/PLAYER`{annKey}`3`NOTE)")).IsEqualTo("Book club,  [and] more");
			await Assert.That(await Think($"get({Calendar}/PLAYER`{annKey}`1`ZONE)")).IsEqualTo("America/New_York");

			var screen = await As(ann, "+avail");
			await Assert.That(screen).Contains("Your availability").And.Contains("Weekdays, 09:00-17:00")
				.And.Contains("Work").And.Contains("The week ahead").And.Contains("Times in America/New_York");

			await Assert.That(await As(ann, "+avail/skip 1=2099-01-05")).Contains("Entry 1 leaves out Mon 5 Jan 2099.");
			await Assert.That(await As(ann, "+avail/skip 1=2099-01-05")).Contains("Entry 1 already leaves out Mon 5 Jan 2099.");
			await Assert.That(await As(ann, "+avail/unskip 1=2099-01-05")).Contains("Entry 1 takes in Mon 5 Jan 2099 again.");
			await Assert.That(await Think($"hasattr({Calendar},PLAYER`{annKey}`1`SKIP)")).IsEqualTo("0");
			await Assert.That(await As(ann, "+avail/skip 1=soon")).Contains("Give the date after =, written 2026-10-20.");
			await Assert.That(await As(ann, "+avail/skip 9=2099-01-05")).Contains("You have no entry 9.");

			// Others see the week without notes.
			var seen = await As(bob, $"+avail {await Think($"name(#{ann.DbRef.Number})")}");
			await Assert.That(seen).Contains("Availability:").And.DoesNotContain("Work");
			await Assert.That(await As(ann, $"+avail {bobName}")).Contains($"{bobName} has set no times, so they count as free at any time.");
			await Assert.That(await As(ann, "+avail Nobody")).Contains("There is no player called 'Nobody'.");

			await Assert.That(await As(ann, "+avail/remove 2")).Contains("Removed 2: Weekends, 18:00-23:00.");
			await Assert.That(await Think($"hasattr({Calendar},PLAYER`{annKey}`2`KIND)")).IsEqualTo("0");
			await Assert.That(await As(ann, "+avail/busy tue 9am")).Contains("Give an end time too");
			await Assert.That(await As(ann, "+avail/busy")).Contains("Say when, such as +avail/busy weekdays 9:00-17:00=Work");
			await Assert.That(await As(ann, "+avail/bogus")).Contains("+avail has no /bogus switch.");
			await Assert.That(await As(ann, "+avail/find Nobody")).Contains("There is no player called 'Nobody'.");
			await Assert.That(await As(ann, $"+avail/find {bobName}=abc")).Contains("Give a length from 15m to 24h after =");
			await Assert.That(await As(ann, $"+avail/find {bobName}=1h")).Contains("Free times").And.Contains($"set no times")
				.And.DoesNotContain("+scene/schedule").Because("without scene-availability, +avail knows nothing of scenes");
			await Assert.That(await As(ann, "+avail/scene 1")).Contains("+avail has no /scene switch.");

			await Assert.That(await As(ann, "+avail/clear")).Contains("Removed all 2 of your entries.");
			await Assert.That(await Think($"lattr({Calendar}/PLAYER`{annKey}**)")).IsEqualTo(string.Empty);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task SettingsAreChangedThroughConfig()
	{
		try
		{
			await InstallAsync();
			var staff = await Player("AvCfg");
			await God($"@role/assign #{staff.DbRef.Number}=wizard");

			await Assert.That(await As(staff, "+config avail")).Contains("find-days").And.Contains("find-length");
			await Assert.That(await As(staff, "+config avail/find-days=14")).Contains("avail/find-days is now 14, was 7.");
			await Assert.That(await Think($"get({Avail}/DATA`CONFIG`FIND-DAYS)")).IsEqualTo("14");
			await Assert.That(await As(staff, "+config avail/find-days=99")).DoesNotContain("is now");
			await Assert.That(await As(staff, "+config avail/find-length=3h")).Contains("avail/find-length is now 3h");
			await Assert.That(await Think($"get({Avail}/DATA`CONFIG`FIND-LENGTH)")).IsEqualTo("10800");
			await Assert.That(await As(staff, "+config avail/slot=45")).DoesNotContain("is now");
			await Assert.That(await As(staff, "+config avail")).DoesNotContain("scene-length");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task WithScenes_AScheduledScenesCardShowsWhoIsFree()
	{
		try
		{
			await InstallAsync(withScenes: true);
			var ann = await Player("AvFay");
			var bob = await Player("AvGus");
			var cleo = await Player("AvHal");
			var annKey = await KeyOf(ann);
			var bobKey = await KeyOf(bob);

			// Monday 5 January 2099 at 18:00 UTC. Ann is busy then, Bob is free, Cleo set nothing.
			await EntryAsync(annKey, 1, "busy", ("REPEAT", "weekly"), ("DAYS", "mon"), ("DATE", "2098-12-29"),
				("TIME", "17:00"), ("MINUTES", "120"), ("ZONE", "UTC"));
			await EntryAsync(bobKey, 1, "free", ("REPEAT", "weekly"), ("DAYS", "mon"), ("DATE", "2098-12-29"),
				("TIME", "17:00"), ("MINUTES", "300"), ("ZONE", "UTC"));

			await As(ann, "+scene/schedule 4071319200/Harbour market=A pitch");
			var scene = await Think($"last(setinter(scenelist(scheduled),scenelist(#{ann.DbRef.Number})))");
			await As(ann, $"+scene/invite {scene}=#{bob.DbRef.Number}");
			var pendingCard = await As(ann, $"+scene {scene}");
			await Assert.That(pendingCard).DoesNotContain(await Think($"name(#{bob.DbRef.Number})"));
			await As(bob, $"+scene/join {scene}");
			await As(cleo, $"+scene/join {scene}");

			var card = await As(ann, $"+scene {scene}");
			await Assert.That(card).Contains("Free at its time");
			var lines = card.Split('\n').Select(l => l.Trim()).ToList();
			var bobName = await Think($"name(#{bob.DbRef.Number})");
			var annName = await Think($"name(#{ann.DbRef.Number})");
			var cleoName = await Think($"name(#{cleo.DbRef.Number})");
			await Assert.That(lines).Contains(l => l.StartsWith("Free:") && l.Contains(bobName));
			await Assert.That(lines).Contains(l => l.StartsWith("Busy:") && l.Contains(annName));
			await Assert.That(lines).Contains(l => l.StartsWith("Not set:") && l.Contains(cleoName));

			await Assert.That(await As(ann, "+avail/scene 999999")).Contains("No such scene: 999999");
			await Assert.That(await As(ann, $"+avail/scene {scene}=1h")).Contains($"Free times for scene {scene}");
			// Ann and Cleo are free now (Ann's entries start in 2098), so the bridge offers to book the first time.
			await Assert.That(await As(ann, $"+avail/find {cleoName}=1h")).Contains("Book the first: +scene/schedule ");
			await Assert.That(await As(ann, "+help avail scene")).Contains("+avail/scene <id>");

			var staff = await Player("AvSceneCfg");
			await God($"@role/assign #{staff.DbRef.Number}=wizard");
			await Assert.That(await As(staff, "+config avail/scene-length=3h")).Contains("avail/scene-length is now 3h");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
