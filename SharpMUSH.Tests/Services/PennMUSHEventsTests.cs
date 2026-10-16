using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH's own events: the dump events around a backup and the schedule's warnings, the inactivity check, the SQL
/// connection events, user signals and <c>LOG`HUH</c>.
/// </summary>
public class PennMUSHEventsTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private static IOptionsWrapper<SharpMUSHOptions> Options(Func<SharpMUSHOptions, SharpMUSHOptions>? mutate = null)
	{
		var defaults = SharpMUSHOptions.Default();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(mutate is null ? defaults : mutate(defaults));
		return options;
	}

	private sealed record Inactivity(InactivityCheckService Service, IEventService Events, IConnectionService Connections, IMessageBus Bus);

	private Inactivity CreateInactivity(params IConnectionService.ConnectionData[] connections)
	{
		var events = Substitute.For<IEventService>();
		var connectionService = Substitute.For<IConnectionService>();
		connectionService.GetAll().Returns(_ => connections.ToAsyncEnumerable());
		var bus = Substitute.For<IMessageBus>();
		var service = new InactivityCheckService(Options(), events, connectionService,
			WebAppFactoryArg.Services.GetRequiredService<INotifyService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPermissionService>(),
			Mediator, bus, Substitute.For<TimeProvider>(), NullLogger<InactivityCheckService>.Instance);
		return new Inactivity(service, events, connectionService, bus);
	}

	private static WorldBackupScheduleService CreateSchedule(IBackupRunner runner)
		=> new(Substitute.For<IWorldBackupService>(), runner, Substitute.For<TimeProvider>(),
			NullLogger<WorldBackupScheduleService>.Instance);

	private static readonly WorldBackup Written = new("20260101T000000Z", "/backups/20260101T000000Z", Start, 4096);

	[Test]
	public async Task TheBackupScheduleWarnsAtFiveAndOneMinutesThenBacksUp()
	{
		var runner = Substitute.For<IBackupRunner>();
		runner.RunAsync(Arg.Any<DBRef?>(), Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>()).Returns(new Result<WorldBackup>(Written));
		var schedule = CreateSchedule(runner);
		var interval = TimeSpan.FromMinutes(10);

		await schedule.TickAsync(Start, interval);
		await schedule.TickAsync(Start + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59), interval);
		await runner.DidNotReceive().WarnAsync(Arg.Any<int>());

		await schedule.TickAsync(Start + TimeSpan.FromMinutes(5), interval);
		await runner.Received(1).WarnAsync(5);

		await schedule.TickAsync(Start + TimeSpan.FromMinutes(9), interval);
		await runner.Received(1).WarnAsync(1);
		await runner.DidNotReceive().RunAsync(Arg.Any<DBRef?>(), Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>());

		await schedule.TickAsync(Start + TimeSpan.FromMinutes(10), interval);
		await runner.Received(1).RunAsync(null, Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>());

		await schedule.TickAsync(Start + TimeSpan.FromMinutes(15), interval);
		await runner.Received(2).WarnAsync(5);
	}

	[Test]
	public async Task AScheduleShorterThanFiveMinutesWarnsOnlyAtOne()
	{
		var runner = Substitute.For<IBackupRunner>();
		runner.RunAsync(Arg.Any<DBRef?>(), Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>()).Returns(new Result<WorldBackup>(Written));
		var schedule = CreateSchedule(runner);
		var interval = TimeSpan.FromMinutes(3);

		foreach (var minutes in new[] { 0, 1, 2, 3 })
		{
			await schedule.TickAsync(Start + TimeSpan.FromMinutes(minutes), interval);
		}

		await runner.DidNotReceive().WarnAsync(5);
		await runner.Received(1).WarnAsync(1);
		await runner.Received(1).RunAsync(null, Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task AWarningRaisesItsDumpEventThenTellsEveryone()
	{
		var events = Substitute.For<IEventService>();
		var broadcast = Substitute.For<IGameBroadcastService>();
		var runner = new BackupRunner(Substitute.For<IWorldBackupService>(), events, broadcast, Options(),
			NullLogger<BackupRunner>.Instance);

		await runner.WarnAsync(5);
		await runner.WarnAsync(1);

		Received.InOrder(() =>
		{
			events.TriggerEventAsync(SharpEvents.Dump5Min, null, "GAME: Database save in 5 minutes.", "0");
			broadcast.BroadcastAsync("GAME: Database save in 5 minutes.");
			events.TriggerEventAsync(SharpEvents.Dump1Min, null, "GAME: Database save in 1 minute.", "0");
			broadcast.BroadcastAsync("GAME: Database save in 1 minute.");
		});
	}

	[Test]
	public async Task AWrittenBackupRaisesBackupAndDumpComplete()
	{
		var backups = Substitute.For<IWorldBackupService>();
		backups.CreateAsync(Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>()).Returns(new Result<WorldBackup>(Written));
		var events = Substitute.For<IEventService>();
		var broadcast = Substitute.For<IGameBroadcastService>();
		var runner = new BackupRunner(backups, events, broadcast, Options(), NullLogger<BackupRunner>.Instance);

		var result = await runner.RunAsync(new DBRef(1));

		await Assert.That(result.Expect<WorldBackup>()).IsEqualTo(Written);
		Received.InOrder(() =>
		{
			broadcast.BroadcastAsync("GAME: Saving database. Game may freeze for a few moments.");
			events.TriggerEventAsync(SharpEvents.BackupComplete, new DBRef(1), Written.Name, "4096");
			events.TriggerEventAsync(SharpEvents.DumpComplete, new DBRef(1), "GAME: Save complete.", "0");
			broadcast.BroadcastAsync("GAME: Save complete.");
		});
	}

	[Test]
	public async Task AFailedBackupRaisesDumpErrorAndTellsWizards()
	{
		var backups = Substitute.For<IWorldBackupService>();
		backups.CreateAsync(Arg.Any<BackupProvenance>(), Arg.Any<CancellationToken>()).Returns(new Result<WorldBackup>(new Error<string>("disk full")));
		var events = Substitute.For<IEventService>();
		var broadcast = Substitute.For<IGameBroadcastService>();
		var runner = new BackupRunner(backups, events, broadcast, Options(), NullLogger<BackupRunner>.Instance);

		await runner.RunAsync(null);

		await events.Received(1).TriggerEventAsync(SharpEvents.BackupError, null, "disk full");
		await events.Received(1).TriggerEventAsync(SharpEvents.DumpError, null, BackupRunner.SaveFailed, "0", "PERROR disk full");
		await events.DidNotReceive().TriggerEventAsync(SharpEvents.DumpComplete, Arg.Any<DBRef?>(), Arg.Any<string[]>());
		await broadcast.Received(1).BroadcastToFlagAsync(Arg.Is<IReadOnlyCollection<string>>(flags => flags.SequenceEqual(new[] { "WIZARD", "ROYALTY" })), "", BackupRunner.SaveFailed);
		await broadcast.DidNotReceive().BroadcastAsync("GAME: Save complete.");
	}

	/// <summary>A play connection whose last input was at the Unix epoch: idle past any limit.</summary>
	private static IConnectionService.ConnectionData IdleConnection(DBRef player, string presence = PresenceClasses.Play)
		=> new(TestIsolationHelpers.GenerateUniqueHandle(), player, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>
			{
				["LastConnectionSignal"] = "0",
				["PresenceClass"] = presence
			});

	[Test]
	public async Task AnIdleMortalIsDisconnectedAndInactivityIsRaised()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdleMortal");
		var connection = IdleConnection(player);
		var timers = CreateInactivity(connection);

		await timers.Service.InactivityCheckAsync(60);

		await timers.Connections.Received(1).Disconnect(connection.Handle);
		await timers.Bus.Received(1).Publish(Arg.Is<DisconnectConnectionMessage>(message => message.Handle == connection.Handle && message.Reason == "idle"));
		await timers.Events.Received(1).TriggerEventAsync(SharpEvents.PlayerInactivity, null);
	}

	[Test]
	public async Task AnIdlePortalTabIsLeftAlone()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdlePortal");
		var connection = IdleConnection(player, PresenceClasses.Portal);
		var timers = CreateInactivity(connection);

		await timers.Service.InactivityCheckAsync(60);

		await timers.Connections.DidNotReceive().Disconnect(Arg.Any<long>(), Arg.Any<string?>());
		await timers.Events.DidNotReceive().TriggerEventAsync(SharpEvents.PlayerInactivity, null);
	}

	[Test]
	public async Task AnIdleUnfindableWizardIsHiddenInstead()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdleWizard");
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player}=WIZARD UNFINDABLE"));
		var connection = IdleConnection(player);
		var timers = CreateInactivity(connection);

		await timers.Service.InactivityCheckAsync(60);

		await timers.Connections.DidNotReceive().Disconnect(Arg.Any<long>(), Arg.Any<string?>());
		timers.Connections.Received(1).Update(connection.Handle, "Hidden", "1");
		await timers.Events.Received(1).TriggerEventAsync(SharpEvents.PlayerInactivity, null);
	}

	[Test]
	public async Task NoIdleTimeoutDisconnectsNobody()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdleNoLimit");
		var timers = CreateInactivity(IdleConnection(player));

		await timers.Service.InactivityCheckAsync(0);

		await timers.Connections.DidNotReceive().Disconnect(Arg.Any<long>(), Arg.Any<string?>());
	}

	[Test]
	public async Task ASignalTheHandlerHearsRaisesItsEvent()
	{
		var events = Substitute.For<IEventService>();
		events.HasHandlerAsync(SharpEvents.SignalUsr1).Returns(true);
		var restart = Substitute.For<IServerRestart>();
		var service = new SignalEventsService(events, Substitute.For<IBackupRunner>(), restart,
			NullLogger<SignalEventsService>.Instance);

		await service.HandleAsync(SignalEventsService.UserSignal.Usr1);

		await events.Received(1).TriggerEventAsync(SharpEvents.SignalUsr1, null);
		await restart.DidNotReceive().RestartAsync(Arg.Any<string>());
	}

	[Test]
	public async Task ASignalNobodyHearsTakesPennMUSHsDefault()
	{
		var events = Substitute.For<IEventService>();
		var backups = Substitute.For<IBackupRunner>();
		var restart = Substitute.For<IServerRestart>();
		var service = new SignalEventsService(events, backups, restart, NullLogger<SignalEventsService>.Instance);

		await service.HandleAsync(SignalEventsService.UserSignal.Usr1);
		await service.HandleAsync(SignalEventsService.UserSignal.Usr2);

		await restart.Received(1).RestartAsync("SIGUSR1");
		await backups.Received(1).RunAsync(null, Arg.Any<CancellationToken>());
		await events.DidNotReceive().TriggerEventAsync(Arg.Any<string>(), Arg.Any<DBRef?>(), Arg.Any<string[]>());
	}

	[Test]
	public async Task SqlReportsConnectFailThenConnectThenDisconnect()
	{
		var directory = Directory.CreateTempSubdirectory("sharpmush-sql-events");
		try
		{
			var good = Path.Join(directory.FullName, "game.db");
			var bad = Path.Join(directory.FullName, "missing", "game.db");
			var database = bad;
			var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
			monitor.CurrentValue.Returns(_ => Options(options => options with
			{
				Net = options.Net with { SqlPlatform = "sqlite", SqlDatabase = database, SqlHost = null }
			}).CurrentValue);
			var events = Substitute.For<IEventService>();
			await using var sql = new SqlService(monitor, FixedSecrets.With(SecretDefinition.SqlPassword.Name, ""), new Lazy<IEventService>(() => events));

			await Assert.That(async () => await sql.ExecuteQueryAsync("SELECT 1")).Throws<SqliteException>();
			await events.Received(1).TriggerEventAsync(SharpEvents.SqlConnectFail, null, "sqlite3", Arg.Any<string>());

			database = good;
			await sql.ExecuteQueryAsync("SELECT 1");
			await sql.ExecuteQueryAsync("SELECT 1");
			await events.Received(1).TriggerEventAsync(SharpEvents.SqlConnect, null, "sqlite3");

			database = bad;
			await Assert.That(async () => await sql.ExecuteQueryAsync("SELECT 1")).Throws<SqliteException>();
			await events.Received(1).TriggerEventAsync(SharpEvents.SqlDisconnect, null, "sqlite3", Arg.Any<string>());
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			directory.Delete(recursive: true);
		}
	}

	[Test]
	public async Task AnUnknownCommandInSomeoneElsesRoomRaisesLogHuh()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("huh").ToLowerInvariant();
		var attribute = $"LOG`HUH`{marker.ToUpperInvariant()}";
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Huh");
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{attribute} #9=think [if(strmatch(%0,*zz{marker}),set(#9,HEARD_{marker}:%0))]"));

		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"zz{marker}"));

		await WebAppFactoryArg.QueueBarrierAsync();
		var heard = (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain($"get(#9/HEARD_{marker})"))).ToPlainText();
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} #9"));

		await Assert.That(heard).StartsWith($"HUH: {player.Name}(#{player.DbRef.Number}) in ");
		await Assert.That(heard).EndsWith($": zz{marker}");
	}
}
