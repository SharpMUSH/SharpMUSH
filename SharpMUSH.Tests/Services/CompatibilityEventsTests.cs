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
/// The PennMUSH events raised only while <c>compatibility_events</c> is on: the save cycle and its warnings,
/// <c>DB`DBCK</c>, the inactivity check, the SQL connection events, user signals and <c>LOG`HUH</c>.
/// </summary>
public class CompatibilityEventsTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private static IOptionsWrapper<SharpMUSHOptions> Options(bool on, Func<SharpMUSHOptions, SharpMUSHOptions>? mutate = null)
	{
		var defaults = SharpMUSHOptions.Default();
		var configured = defaults with
		{
			Compatibility = defaults.Compatibility with { CompatibilityEvents = on },
			Dump = defaults.Dump with { DumpInterval = "10m", DbckInterval = "3m" }
		};
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(mutate is null ? configured : mutate(configured));
		return options;
	}

	private sealed record Timers(CompatibilityEventsService Service, IDumpService Dump, IEventService Events, IConnectionService Connections, IMessageBus Bus);

	private Timers CreateTimers(IOptionsWrapper<SharpMUSHOptions> options, params IConnectionService.ConnectionData[] connections)
	{
		var dump = Substitute.For<IDumpService>();
		var events = Substitute.For<IEventService>();
		var connectionService = Substitute.For<IConnectionService>();
		connectionService.GetAll().Returns(_ => connections.ToAsyncEnumerable());
		var bus = Substitute.For<IMessageBus>();
		var service = new CompatibilityEventsService(options, dump, events, connectionService,
			WebAppFactoryArg.Services.GetRequiredService<INotifyService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPermissionService>(),
			Mediator, bus, Substitute.For<TimeProvider>(), NullLogger<CompatibilityEventsService>.Instance);
		return new Timers(service, dump, events, connectionService, bus);
	}

	[Test]
	public async Task TheSaveCycleWarnsAtFiveAndOneMinutesThenSaves()
	{
		var timers = CreateTimers(Options(true));

		await timers.Service.TickAsync(Start);
		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
		await timers.Dump.DidNotReceive().WarnAsync(Arg.Any<int>());

		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(5));
		await timers.Dump.Received(1).WarnAsync(5);

		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(9));
		await timers.Dump.Received(1).WarnAsync(1);
		await timers.Dump.DidNotReceive().DumpAsync(Arg.Any<DBRef?>());

		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(10));
		await timers.Dump.Received(1).DumpAsync(null);

		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(15));
		await timers.Dump.Received(2).WarnAsync(5);
	}

	[Test]
	public async Task ChangingDumpIntervalStartsTheCycleAgain()
	{
		var interval = "10m";
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(_ => Options(true, configured => configured with
		{
			Dump = configured.Dump with { DumpInterval = interval }
		}).CurrentValue);
		var timers = CreateTimers(options);

		await timers.Service.TickAsync(Start);
		interval = "3m";
		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(12));
		await timers.Dump.DidNotReceive().DumpAsync(Arg.Any<DBRef?>());

		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(14));
		await timers.Dump.Received(1).WarnAsync(1);
		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(15));
		await timers.Dump.Received(1).DumpAsync(null);
	}

	[Test]
	public async Task DbckIsRaisedEveryDbckInterval()
	{
		var timers = CreateTimers(Options(true));

		await timers.Service.TickAsync(Start);
		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(3));
		await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(6));

		await timers.Events.Received(2).TriggerEventAsync(SharpEvents.DbDbck, null);
	}

	[Test]
	public async Task NothingRunsWithTheOptionOff()
	{
		var timers = CreateTimers(Options(false));

		foreach (var minutes in new[] { 0, 3, 5, 9, 10, 20 })
		{
			await timers.Service.TickAsync(Start + TimeSpan.FromMinutes(minutes));
		}

		await timers.Dump.DidNotReceive().WarnAsync(Arg.Any<int>());
		await timers.Dump.DidNotReceive().DumpAsync(Arg.Any<DBRef?>());
		await timers.Events.DidNotReceive().TriggerEventAsync(Arg.Any<string>(), Arg.Any<DBRef?>(), Arg.Any<string[]>());
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
		var timers = CreateTimers(Options(true), connection);

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
		var timers = CreateTimers(Options(true), connection);

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
		var timers = CreateTimers(Options(true), connection);

		await timers.Service.InactivityCheckAsync(60);

		await timers.Connections.DidNotReceive().Disconnect(Arg.Any<long>(), Arg.Any<string?>());
		timers.Connections.Received(1).Update(connection.Handle, "Hidden", "1");
		await timers.Events.Received(1).TriggerEventAsync(SharpEvents.PlayerInactivity, null);
	}

	[Test]
	public async Task NoIdleTimeoutDisconnectsNobody()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdleNoLimit");
		var timers = CreateTimers(Options(true), IdleConnection(player));

		await timers.Service.InactivityCheckAsync(0);

		await timers.Connections.DidNotReceive().Disconnect(Arg.Any<long>(), Arg.Any<string?>());
	}

	[Test]
	public async Task ASaveRaisesDumpCompleteWithItsMessage()
	{
		var save = Substitute.For<IWorldSaveService>();
		save.SaveAsync(Arg.Any<CancellationToken>()).Returns(new Result<Success>(new Success()));
		var events = Substitute.For<IEventService>();
		var broadcast = Substitute.For<IGameBroadcastService>();
		var dump = new DumpService(save, events, broadcast, Options(true), NullLogger<DumpService>.Instance);

		await dump.DumpAsync(new DBRef(1));

		await events.Received(1).TriggerEventAsync(SharpEvents.DumpComplete, new DBRef(1), "GAME: Save complete.", "0");
	}

	[Test]
	public async Task AFailedSaveRaisesDumpErrorWithItsReason()
	{
		var save = Substitute.For<IWorldSaveService>();
		save.SaveAsync(Arg.Any<CancellationToken>()).Returns(new Result<Success>(new Error<string>("disk full")));
		var events = Substitute.For<IEventService>();
		var broadcast = Substitute.For<IGameBroadcastService>();
		var dump = new DumpService(save, events, broadcast, Options(true), NullLogger<DumpService>.Instance);

		await dump.DumpAsync(null);

		await events.Received(1).TriggerEventAsync(SharpEvents.DumpError, null, DumpService.SaveFailed, "0", "PERROR disk full");
		await events.DidNotReceive().TriggerEventAsync(SharpEvents.DumpComplete, Arg.Any<DBRef?>(), Arg.Any<string[]>());
	}

	[Test]
	public async Task ASignalTheHandlerHearsRaisesItsEvent()
	{
		var events = Substitute.For<IEventService>();
		events.HasHandlerAsync(SharpEvents.SignalUsr1).Returns(true);
		var restart = Substitute.For<IServerRestart>();
		var service = new SignalEventsService(Options(true), events, Substitute.For<IDumpService>(), restart,
			NullLogger<SignalEventsService>.Instance);

		await service.HandleAsync(SignalEventsService.UserSignal.Usr1);

		await events.Received(1).TriggerEventAsync(SharpEvents.SignalUsr1, null);
		await restart.DidNotReceive().RestartAsync(Arg.Any<string>());
	}

	[Test]
	public async Task ASignalNobodyHearsTakesPennMUSHsDefault()
	{
		var events = Substitute.For<IEventService>();
		var dump = Substitute.For<IDumpService>();
		var restart = Substitute.For<IServerRestart>();
		var service = new SignalEventsService(Options(true), events, dump, restart, NullLogger<SignalEventsService>.Instance);

		await service.HandleAsync(SignalEventsService.UserSignal.Usr1);
		await service.HandleAsync(SignalEventsService.UserSignal.Usr2);

		await restart.Received(1).RestartAsync("SIGUSR1");
		await dump.Received(1).DumpAsync(null);
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
			monitor.CurrentValue.Returns(_ => Options(true, options => options with
			{
				Net = options.Net with { SqlPlatform = "sqlite", SqlDatabase = database, SqlHost = null }
			}).CurrentValue);
			var events = Substitute.For<IEventService>();
			await using var sql = new SqlService(monitor, FixedSecrets.With(SecretDefinition.SqlPassword.Name, ""),
				new Lazy<IEventService>(() => events));

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

		using (TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { CompatibilityEvents = true }
		}))
		{
			await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"zz{marker}"));
		}

		await WebAppFactoryArg.QueueBarrierAsync();
		var heard = (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain($"get(#9/HEARD_{marker})"))).ToPlainText();
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} #9"));

		await Assert.That(heard).StartsWith($"HUH: {player.Name}(#{player.DbRef.Number}) in ");
		await Assert.That(heard).EndsWith($": zz{marker}");
	}
}
