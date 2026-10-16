using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
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

			var staff = await Player("ScStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
			await Assert.That(await As(staff, "+config time/ic-show=no")).Contains("time/ic-show is now no, was yes.");
			await Assert.That(await As(ann, "+time")).DoesNotContain("IC date");

			// Month names come all twelve or none, split by |.
			await Assert.That(await As(staff, "+config time/ic-months=Thaw|Seed")).Contains("Give all 12 names, or none for the real ones.");
			await Assert.That(await As(staff, "+config time/zone=Mars/Olympus")).Contains("Give a time zone, such as Europe/London or UTC.");
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
			await God($"&DATA`CONFIG`IC-RATIO {Clock}=2");
			await God($"&DATA`CONFIG`IC-YEARS {Clock}=300");
			await God($"&DATA`CONFIG`IC-MONTHS {Clock}=Deepwinter|Thaw|Seed|Rain|Bloom|Sun|High|Harvest|Fall|Mist|Frost|Dark");
			await God($"&DATA`CONFIG`IC-WEEKDAYS {Clock}=Sunsday|Moonsday|Tirsday|Wodensday|Thorsday|Freyday|Starday");
			await Assert.That(await Think($"u({Clock}/FUN`IC`DATE,{1767225600 + 86400 + 3600})")).IsEqualTo("Starday, 3 Deepwinter 2326, 02:00");

			// IC-DAYS moves the date, and FORMAT`IC writes it the game's way.
			await God($"&DATA`CONFIG`IC-DAYS {Clock}=-3");
			await God($"&FORMAT`IC {Clock}=Year %<year>, month %<month>, day %<day>");
			await Assert.That(await Think($"u({Clock}/FUN`IC`DATE,{1767225600 + 86400})")).IsEqualTo("Year 2325, month 12, day 31");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>What <paramref name="player"/> was told while God ran <paramref name="command"/> and the queue drained.</summary>
	private async Task<string> Told(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await God(command);
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>The event handler's player`connect, as the engine runs it when the player logs in.</summary>
	private Task<string> LogsIn(TestIsolationHelpers.TestPlayer player) =>
		Told(player, $"@trigger/args #9/PLAYER`CONNECT`TIME=objid,#{player.DbRef.Number},connections,1,descriptor,{player.Handle}");

	[Test]
	public async Task AZoneTheClientReportedFillsAnEmptyOne()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("CzAnn");
			var number = ann.DbRef.Number;

			// No client zone: nothing happens, and +timezone points at /match.
			await Assert.That(await LogsIn(ann)).DoesNotContain("[TIME]");
			await Assert.That(await As(ann, "+timezone")).Contains("+timezone/match <your time> finds yours");
			await Assert.That(await As(ann, "+timezone/detect")).Contains("Your client has not reported a time zone.");

			// The client reported one: filled in, hidden, and the player is told.
			ConnectionService.Update(ann.Handle, ConnectionTimeZone.Key, "Asia/Tokyo");
			await Assert.That(await Think($"clientzone(#{number})")).IsEqualTo("Asia/Tokyo");
			await Assert.That(await LogsIn(ann)).Contains("Your time zone is now Asia/Tokyo (UTC+09:00), from your client. Only you and staff see it");
			await Assert.That(await God($"think get(#{number}/TZ)")).IsEqualTo("Asia/Tokyo");
			await Assert.That(await Think($"hasflag(#{number}/TZ,visual)")).IsEqualTo("0");

			// A zone the player set is never replaced.
			await As(ann, "+timezone UTC");
			ConnectionService.Update(ann.Handle, ConnectionTimeZone.Key, "Europe/Paris");
			await Assert.That(await LogsIn(ann)).DoesNotContain("[TIME]");
			await Assert.That(await Told(ann, $"@trigger/args #9/SOCKET`TIMEZONE`TIME=descriptor,{ann.Handle},zone,Europe/Paris,player,#{number}"))
				.DoesNotContain("[TIME]");
			await Assert.That(await God($"think get(#{number}/TZ)")).IsEqualTo("UTC");

			// +timezone/detect takes it on request.
			await Assert.That(await As(ann, "+timezone/detect")).Contains("Your time zone is Europe/Paris (UTC+0");
			await Assert.That(await As(ann, "+timezone/detect")).Contains("Your time zone is already Europe/Paris.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task OfferAndOffLeaveTheZoneToThePlayer()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("OfAnn");
			var number = ann.DbRef.Number;
			ConnectionService.Update(ann.Handle, ConnectionTimeZone.Key, "Asia/Tokyo");
			var staff = await Player("OfStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");

			await Assert.That(await As(staff, "+config time/detect=offer")).Contains("time/detect is now offer, was set.");
			await Assert.That(await LogsIn(ann)).Contains("Your client says your time zone is Asia/Tokyo. +timezone/detect sets it.");
			// After login a report is not offered again.
			await Assert.That(await Told(ann, $"@trigger/args #9/SOCKET`TIMEZONE`TIME=descriptor,{ann.Handle},zone,Asia/Tokyo,player,#{number}"))
				.DoesNotContain("[TIME]");
			await Assert.That(await God($"think hasattr(#{number},TZ)")).IsEqualTo("0");
			await Assert.That(await As(ann, "+timezone")).Contains("Your client says yours is Asia/Tokyo: +timezone/detect sets it.");

			await As(staff, "+config time/detect=off");
			await Assert.That(await LogsIn(ann)).DoesNotContain("[TIME]");
			await Assert.That(await God($"think hasattr(#{number},TZ)")).IsEqualTo("0");

			await Assert.That(await As(staff, "+config time/match-zones=Asia/Tokyo Nowhere/Land")).Contains("Nowhere/Land");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>
	/// What the Time object does with a zone a lookup found for <paramref name="player"/> at login: INC`ZONE`SETTLE, as
	/// the zone service's reply hands it over.
	/// </summary>
	private Task<string> SettlesFromAddress(TestIsolationHelpers.TestPlayer player, string zone) =>
		Told(player, $"@trigger/args {Time}/INC`ZONE`SETTLE=player,#{player.DbRef.Number},zone,{zone},from,ip,login,1");

	/// <summary>
	/// The zone service's reply, <paramref name="body"/> with <paramref name="status"/>, for a login lookup: run as the
	/// LOOKUP`&lt;number&gt; attribute INC`ZONE`LOOKUP writes on the Time object runs, by an attribute of the same shape.
	/// </summary>
	private async Task<string> Replies(TestIsolationHelpers.TestPlayer player, int status, string body)
	{
		await God($"&REPLY`TEST {Time}=@include/args {Time}/INC`ZONE`REPLY=body,%0,status,{status},player,#{player.DbRef.Number},mode,login");
		return await Told(player, $"@trigger {Time}/REPLY`TEST={{{{{body}}}}}");
	}

	private sealed class DeferredZoneResponse : HttpMessageHandler
	{
		public TaskCompletionSource<Uri> Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requested.TrySetResult(request.RequestUri!);
			return await Response.Task.WaitAsync(cancellationToken);
		}
	}

	[Test]
	public async Task GeneratedHttpCallbackPreservesTheDeferredResponseBody()
	{
		using var response = new DeferredZoneResponse();
		using var client = new HttpClient(response);
		try
		{
			await InstallAsync();
			var player = await Player("DeferredZone");
			ConnectionService.Update(player.Handle, "InternetProtocolAddress", "81.2.69.142");
			await God($"&DATA`CONFIG`DETECT-IP {Clock}=set");
			await God($"&DATA`CONFIG`IP-FIELD {Clock}=timezone.id");
			var clients = Substitute.For<IHttpClientFactory>();
			clients.CreateClient(Arg.Any<string>()).Returns(client);
			var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, clients);
			var parser = WebAppFactoryArg.CommandParserWith(
				((ILibraryProvider<SharpMUSH.Library.Definitions.CommandDefinition>)commands).Get(), DBRef.Parse(Time), 1);
			var before = Notifications.CountFor(player.DbRef);

			// Run the installed producer and its generated LOOKUP attribute through the real HTTP admission.
			// Only the transport is held back: the JSON does not exist when the callback text is written.
			await parser.CommandListParse(MarkupText.Plain($"@include/args {Time}/INC`ZONE`LOOKUP=player,{player.DbRef},mode,login"));
			await Assert.That((await response.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10))).AbsoluteUri)
				.IsEqualTo("https://ipinfo.io/81.2.69.142/json");
			await Assert.That(await Think($"hasattr({player.DbRef},TZ)")).IsEqualTo("0");
			using var reply = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent("{\"timezone\":{\"id\":\"Asia/Tokyo\"}}")
			};
			response.Response.TrySetResult(reply);
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await Think($"get({player.DbRef}/TZ)")).IsEqualTo("Asia/Tokyo");
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef).Skip(before)))
				.Contains("guessed from where you connect.");
			await Assert.That(await Think($"lattr({Time}/LOOKUP`*)")).IsEmpty();
		}
		finally
		{
			using var cleanupReply = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
			response.Response.TrySetResult(cleanupReply);
			await WebAppFactoryArg.QueueBarrierAsync();
			await UninstallAsync();
		}
	}

	[Test]
	public async Task TheAddressIsUsedOnlyWhenTheGameTurnsItOn()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("IpAnn");
			var number = ann.DbRef.Number;
			var staff = await Player("IpStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");

			// Off by default: a zone found from the address does nothing.
			await Assert.That(await SettlesFromAddress(ann, "Europe/Paris")).DoesNotContain("[TIME]");
			await Assert.That(await God($"think hasattr(#{number},TZ)")).IsEqualTo("0");

			await Assert.That(await As(staff, "+config time/detect-ip=offer")).Contains("time/detect-ip is now offer, was off.");
			await Assert.That(await SettlesFromAddress(ann, "Europe/Paris"))
				.Contains("Where you connect from suggests your time zone is Europe/Paris. +timezone/detect sets it.");
			await Assert.That(await God($"think hasattr(#{number},TZ)")).IsEqualTo("0");

			await As(staff, "+config time/detect-ip=set");
			await Assert.That(await SettlesFromAddress(ann, "Europe/Paris"))
				.Contains("Your time zone is now Europe/Paris (UTC+0");
			await Assert.That(await God($"think get(#{number}/TZ)")).IsEqualTo("Europe/Paris");
			await Assert.That(await Think($"hasflag(#{number}/TZ,visual)")).IsEqualTo("0");

			// Once a zone is set, a later one changes nothing.
			await Assert.That(await SettlesFromAddress(ann, "Asia/Tokyo")).DoesNotContain("[TIME]");
			await Assert.That(await God($"think get(#{number}/TZ)")).IsEqualTo("Europe/Paris");

			// The service's reply: the zone at ip-field, only from a 200 and only a zone the server knows.
			var bo = await Player("IpBo");
			await Assert.That(await Replies(bo, 429, "\"timezone\":\"Asia/Tokyo\"")).DoesNotContain("[TIME]");
			await Assert.That(await Replies(bo, 200, "\"timezone\":\"Nowhere/Land\"")).DoesNotContain("[TIME]");
			await Assert.That(await God($"think hasattr(#{bo.DbRef.Number},TZ)")).IsEqualTo("0");
			await As(staff, "+config time/ip-field=timezone.id");
			await Assert.That(await Replies(bo, 200, "\"timezone\":{\"id\":\"Asia/Tokyo\"}"))
				.Contains("Your time zone is now Asia/Tokyo (UTC+09:00), guessed from where you connect.");

			// Only an address on the internet is ever sent, and only to an https service.
			foreach (var (address, sent) in new[]
			{
				("81.2.69.142", "1"), ("2a00:1450:4001::200e", "1"), ("172.32.0.1", "1"),
				("127.0.0.1", "0"), ("10.1.2.3", "0"), ("192.168.0.9", "0"), ("172.20.0.1", "0"), ("169.254.1.1", "0"),
				("::1", "0"), ("fd12:3456::1", "0"), ("fe80::1", "0"), ("localhost", "0"), ("UNKNOWN", "0"), ("#-1 NOT CONNECTED", "0"),
			})
			{
				await Assert.That(await Think($"u({Time}/FUN`ADDRESS`PUBLIC,{address})")).IsEqualTo(sent).Because(address);
			}

			await Assert.That(await As(staff, "+config time/ip-service=http://ipinfo.io/<ip>/json")).Contains("Give an https address with <ip>");
			await Assert.That(await As(staff, "+config time/ip-service=https://ipwho.is/<ip>")).Contains("time/ip-service is now https://ipwho.is/<ip>");

			// A test connection's address is not one, so a real login sends nothing even with the lookup on.
			var cy = await Player("IpCy");
			await Assert.That(await LogsIn(cy)).DoesNotContain("[TIME]");
			await Assert.That(await Think($"lattr({Time}/LOOKUP`*)")).IsEqualTo(string.Empty);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task MatchListsTheZonesAtATypedTime()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("MtAnn");

			await Assert.That(await As(ann, "+timezone/match")).Contains("Give the time where you are");
			await Assert.That(await As(ann, "+timezone/match soon")).Contains("'soon' is not a time.");
			await Assert.That(await As(ann, "+timezone/match 8pm pacific")).Contains("'8pm pacific' is not a time.");

			// The time in Tokyo now, typed as a player there would: Tokyo and Seoul share it, Darwin is half an hour off.
			var tokyo = await Think("timefmt($H:$M,secs(),Asia/Tokyo)");
			var listed = await As(ann, $"+timezone/match {tokyo}");
			await Assert.That(listed).Contains("Choose yours:").And.Contains("+timezone Asia/Tokyo · UTC+09:00")
				.And.Contains("+timezone Asia/Seoul").And.DoesNotContain("Australia/Darwin");

			// Only zones on the list are offered.
			await God($"&DATA`CONFIG`MATCH-ZONES {Clock}=UTC");
			await Assert.That(await As(ann, $"+timezone/match {tokyo}")).Contains("No zone on the game's list is at");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}

