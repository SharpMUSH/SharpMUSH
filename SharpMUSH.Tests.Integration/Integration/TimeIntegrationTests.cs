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
/// The bundled time package, installed from the catalogue and driven with typed commands: +timezone keeps a
/// player's zone in TZ, +time converts a typed time between zones, and the Clock's functions write the IC date.
/// Every assertion that depends on a moment names that moment, so nothing here reads the real clock's date.
/// Each test installs the package and removes it again. Not in parallel: +time is global.
/// </summary>
[NotInParallel]
public class TimeIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private string Time { get; set; } = string.Empty;
	private string Clock { get; set; } = string.Empty;

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

	/// <summary>A connected player in a room of its own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	private async Task InstallAsync()
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "time", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("time must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("time");
		Time = objects.First(o => o.Ref == "time").Objid;
		Clock = objects.First(o => o.Ref == "clock").Objid;
	}

	private async Task UninstallAsync() => await Installer.UninstallAsync("time", force: true, CancellationToken.None);

	[Test]
	public async Task TimezoneIsSetFoundAndCleared()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("TzAnn");

			await Assert.That(await As(ann, "+timezone")).Contains("You have not set a time zone.");

			// A short name from the ALIAS` table, a zone in any case, and one the server does not know.
			await Assert.That(await As(ann, "+timezone eastern")).Contains("[TIME] Done: Your time zone is America/New_York (UTC-0");
			await Assert.That(await God($"think get(#{ann.DbRef.Number}/TZ)")).IsEqualTo("America/New_York");
			await Assert.That(await As(ann, "+timezone asia/kolkata")).Contains("Your time zone is Asia/Kolkata (UTC+05:30)");
			await Assert.That(await As(ann, "+timezone Nowhere/Land")).Contains("There is no time zone called 'Nowhere/Land'.");
			await Assert.That(await God($"think get(#{ann.DbRef.Number}/TZ)")).IsEqualTo("Asia/Kolkata");

			// TZ is visual, so code on the Clock reads it, and time() reads it as PennMUSH's does.
			await Assert.That(await Think($"u({Clock}/FUN`ZONE`OF,#{ann.DbRef.Number})")).IsEqualTo("Asia/Kolkata");
			await Assert.That(await As(ann, "+timezone")).Contains("Your time zone is Asia/Kolkata (UTC+05:30).");

			await Assert.That(await As(ann, "+timezone/clear")).Contains("Your time zone is cleared.");
			await Assert.That(await As(ann, "+timezone/clear")).Contains("You have not set a time zone.");
			await Assert.That(await As(ann, "+timezone/bogus")).Contains("+timezone has no /bogus switch.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ATypedTimeIsConvertedBetweenZones()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("CvAnn");
			await As(ann, "+timezone America/New_York");

			// No zone given: the time is read in the reader's own, in summer time on a July date.
			var summer = await As(ann, "+time 2026-07-15 8pm");
			await Assert.That(summer).Contains("20:00 on Wed 15 Jul")
				.And.Contains("America/New_York").And.Contains("Wed 15 Jul 2026, 20:00 · UTC-04:00")
				.And.Contains("Game time")
				.And.Contains("Thu 16 Jul 2026, 00:00 · UTC")
				.And.Contains("<t:1784160000:F>");

			// A zone after the time, and a January date, in standard time.
			await Assert.That(await As(ann, "+time 2026-01-15 20:00 America/New_York")).Contains("<t:1768525200:F>")
				.And.Contains("· UTC-05:00");
			// am or pm on its own, and a short name: read in Los Angeles, shown in the reader's New York too.
			var pacific = await As(ann, "+time 2026-07-15 8:30 pm pacific");
			await Assert.That(pacific).Contains("<t:1784172600:F>").And.Contains("Your time")
				.And.Contains("Wed 15 Jul 2026, 23:30 · America/New_York");

			await Assert.That(await As(ann, "+time 25:00")).Contains("'25:00' is not a time.");
			await Assert.That(await As(ann, "+time 13pm")).Contains("'13pm' is not a time.");
			await Assert.That(await As(ann, "+time 8pm Nowhere/Land")).Contains("There is no time zone called 'Nowhere/Land'.");
			await Assert.That(await As(ann, "+time 8pm two zones")).Contains("Give one time zone after the time");
			await Assert.That(await As(ann, "+time 2026-02-30 8pm")).Contains("'2026-02-30' is not a date.");
			// 02:30 on the day New York's clocks go forward does not happen there.
			await Assert.That(await As(ann, "+time 2026-03-08 02:30")).Contains("There is no 02:30 on 2026-03-08 in America/New_York");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AnotherPlayersTimeAndHowFarAheadItIs()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("PtAnn");
			var bo = await Player("PtBo");

			await Assert.That(await As(ann, $"+time {bo.Name}")).Contains($"{bo.Name} has not set a time zone.");
			await Assert.That(await As(ann, "+time nobody-by-that-name")).Contains("There is no player called 'nobody-by-that-name'.");

			// A name that starts with a digit is still a name.
			var digit = await Player("7Pt");
			await As(digit, "+timezone UTC");
			await Assert.That(await As(ann, $"+time {digit.Name}")).Contains($"Time for {digit.Name}");

			// Two zones without summer time, so the difference is the same on every date.
			await As(bo, "+timezone Asia/Tokyo");
			await Assert.That(await As(ann, $"+time {bo.Name}")).Contains($"Time for {bo.Name}").And.Contains("Asia/Tokyo (UTC+09:00)")
				.And.Contains("+timezone <zone> sets it");
			await As(ann, "+timezone Asia/Kolkata");
			await Assert.That(await As(ann, $"+time {bo.Name}")).Contains($"{bo.Name} is 3.5 hours ahead of you");
			await Assert.That(await As(bo, $"+time {ann.Name}")).Contains($"{ann.Name} is 3.5 hours behind you");

			// A hidden zone is kept from +time and from the Clock, and stays hidden when it changes.
			await Assert.That(await As(bo, "+timezone/hide")).Contains("Only you and staff see your time zone now.");
			await Assert.That(await As(ann, $"+time {bo.Name}")).Contains($"{bo.Name} keeps their time zone to themselves.");
			await Assert.That(await Think($"u({Clock}/FUN`ZONE`OF,#{bo.DbRef.Number})")).IsEqualTo(string.Empty);
			await As(bo, "+timezone Asia/Kolkata");
			await Assert.That(await As(bo, "+timezone")).Contains("Only you and staff see it.");
			await Assert.That(await As(bo, "+timezone/hide")).Contains("Your time zone is already hidden.");
			await Assert.That(await As(bo, "+timezone/unhide")).Contains("Everyone sees your time zone again.");
			await Assert.That(await As(ann, $"+time {bo.Name}")).Contains($"{bo.Name} keeps the same time as you");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task TheScreenShowsGameYourAndICTime()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("ScAnn");

			var screen = await As(ann, "+time");
			await Assert.That(screen).Contains("Game time").And.Contains("· UTC").And.Contains("Your time").And.Contains("not set")
				.And.Contains("IC date");

			// Another package adds to the screen with a filter.
			await God($"&FILTER`TIME`SCREEN`WEATHER {Time}=%0%r[fields(,Weather,Rain)]");
			await Assert.That(await As(ann, "+time")).Contains("Weather: Rain");

			await God($"&DATA`IC`SHOW {Clock}=0");
			await Assert.That(await As(ann, "+time")).DoesNotContain("IC date");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task TheICClockFollowsItsSettings()
	{
		try
		{
			await InstallAsync();

			// At the anchor, with the shipped settings, the IC clock reads the real one.
			await Assert.That(await Think($"u({Clock}/FUN`IC`DATE,1767225600)")).IsEqualTo("Thursday, 1 January 2026, 00:00");

			// Twice as fast, 300 years on, with names of the game's own: a real day after the anchor is two IC days.
			await God($"&DATA`IC`RATIO {Clock}=2");
			await God($"&DATA`IC`YEARS {Clock}=300");
			await God($"&DATA`IC`MONTHS {Clock}=Deepwinter|Thaw|Seed|Rain|Bloom|Sun|High|Harvest|Fall|Mist|Frost|Dark");
			await God($"&DATA`IC`WEEKDAYS {Clock}=Sunsday|Moonsday|Tirsday|Wodensday|Thorsday|Freyday|Starday");
			await Assert.That(await Think($"u({Clock}/FUN`IC`DATE,{1767225600 + 86400 + 3600})")).IsEqualTo("Starday, 3 Deepwinter 2326, 02:00");

			// DATA`IC`DAYS moves the date, and FORMAT`IC writes it the game's way.
			await God($"&DATA`IC`DAYS {Clock}=-3");
			await God($"&FORMAT`IC {Clock}=Year %<year>, month %<month>, day %<day>");
			await Assert.That(await Think($"u({Clock}/FUN`IC`DATE,{1767225600 + 86400})")).IsEqualTo("Year 2325, month 12, day 31");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
