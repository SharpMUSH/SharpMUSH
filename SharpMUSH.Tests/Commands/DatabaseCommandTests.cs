using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Requests;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NSubstitute;
using NSubstitute.Core;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class DatabaseCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory SqlWebAppFactoryArg { get; init; }

	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	private INotifyService NotifyService => SqlWebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => SqlWebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => SqlWebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();
	private IMediator Mediator => SqlWebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Creates a unique test player and grants them the WIZARD flag so they can execute
	/// privileged commands like <c>@sql</c> and <c>@mapsql</c>. Using a per-test wizard player
	/// means <see cref="INotifyService"/> mock assertions against that player's DBRef are
	/// isolated from all other tests in the session.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> CreateWizardTestPlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=WIZARD"));
		return player;
	}

	[Before(Test)]
	public async Task InitializeAsync()
	{
		// Use unique table names for command tests to avoid interference with function tests
		var connectionString = MySqlTestServer.Instance.GetConnectionString();

		await using var connection = new MySqlConnection(connectionString);
		await connection.OpenAsync();

		await using (var cmd = new MySqlCommand("""
		                                        			CREATE TABLE IF NOT EXISTS test_sql_data_cmd (
		                                        				id INT PRIMARY KEY AUTO_INCREMENT,
		                                        				name VARCHAR(255),
		                                        				value INT
		                                        			)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("""
		                                        			CREATE TABLE IF NOT EXISTS test_mapsql_data_cmd (
		                                        				id INT PRIMARY KEY AUTO_INCREMENT,
		                                        				col1 VARCHAR(50),
		                                        				col2 VARCHAR(50),
		                                        				col3 INT
		                                        			)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("TRUNCATE TABLE test_sql_data_cmd", connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("TRUNCATE TABLE test_mapsql_data_cmd", connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("""
		                                        			INSERT INTO test_sql_data_cmd (name, value) VALUES 
		                                        			('test_sql_row1', 100),
		                                        			('test_sql_row2', 200),
		                                        			('test_sql_row3', 300)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("""
		                                        			INSERT INTO test_mapsql_data_cmd (col1, col2, col3) VALUES 
		                                        			('data1_col1', 'data1_col2', 10),
		                                        			('data2_col1', 'data2_col2', 20),
		                                        			('data3_col1', 'data3_col2', 30)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MapSqlDoesNotReportRejectedRowsOrSkipARejectedHeader(bool columnNames)
	{
		// Exercise the ordinary owner ceiling; Wizards have an additional database-sized allowance.
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlCapacity");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await testParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPCAPACITY me=think callback"));
		var scheduler = SqlWebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
		var options = SqlWebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		var pids = new List<long>();
		try
		{
			for (var i = 0; i < options.CurrentValue.Limit.PlayerQueueLimit; i++)
			{
				var admitted = await scheduler.AdmitCommandList(MarkupText.Plain("think reserved"), ParserState.RootFor(player.DbRef), TimeSpan.FromHours(1));
				await Assert.That(admitted.Accepted).IsTrue();
				pids.Add(admitted.Pid!.Value);
			}
			await testParser.CommandParse(player.Handle, ConnectionService,
				MarkupText.Plain($"@mapsql{(columnNames ? "/colnames" : "")} me/MAPCAPACITY=SELECT col1 FROM test_mapsql_data_cmd"));
			await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(player.DbRef),
				TestHelpers.MatchingMessage("0 rows queued for execution."), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
		}
		finally { foreach (var pid in pids) await scheduler.HaltByPid(pid); }
	}

	[Test]
	[Arguments(QueueRejectionReason.OwnerLimit)]
	[Arguments(QueueRejectionReason.GlobalLimit)]
	[Arguments(QueueRejectionReason.ShuttingDown)]
	public async Task MapSqlCompletionReservationReportsOneRealSchedulerRejection(QueueRejectionReason reason)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlSingleRejection");
		var realParser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await realParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPSINGLE me=think row"));
		var state = ParserState.RootFor(player.DbRef) with
		{
			Switches = ["NOTIFY"],
			Arguments = new() { ["0"] = new CallState(player.DbRef + "/MAPSINGLE"), ["1"] = new CallState("SELECT 1") }
		};
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		var baseline = SqlWebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with
		{
			Limit = baseline.Limit with
			{
				PlayerQueueLimit = reason == QueueRejectionReason.OwnerLimit ? 0u : 100u,
				GlobalQueueLimit = reason == QueueRejectionReason.GlobalLimit ? 0u : 100u
			}
		});
		var scheduler = new SharpMUSH.Library.Services.TaskScheduler(Parser, ConnectionService,
			Substitute.For<Quartz.ISchedulerFactory>(), SqlWebAppFactoryArg.Services.GetRequiredService<IAttributeService>(),
			Mediator, Microsoft.Extensions.Logging.Abstractions.NullLogger<SharpMUSH.Library.Services.TaskScheduler>.Instance,
			options, NotifyService);
		var stopped = reason == QueueRejectionReason.ShuttingDown;
		try
		{
			if (stopped) await scheduler.DisposeAsync();
			var admission = Substitute.For<IMediator>();
			admission.Send(Arg.Any<ReserveCommandListRequest>(), Arg.Any<CancellationToken>())
				.Returns(call => scheduler.ReserveCommandList(call.Arg<ReserveCommandListRequest>().Command, call.Arg<ReserveCommandListRequest>().State));
			var sql = Substitute.For<ISqlService>();
			sql.IsAvailable.Returns(true);
			var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(SqlWebAppFactoryArg.Services, admission, sql);
			await commands.MapSql(parser, new SharpCommandAttribute { Name = "@MAPSQL" });
			await NotifyService.Received(1).NotifyLocalized(player.Handle, "QueueRejected", Arg.Any<object[]>());
			await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(player.DbRef),
				TestHelpers.MatchingMessage(new QueueAdmissionResult(null, reason).Error), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
			sql.DidNotReceive().ExecuteStreamQueryAsync(Arg.Any<string>());
			await Assert.That(scheduler.GetQueueUsage().Total).IsEqualTo(0);
		}
		finally { if (!stopped) await scheduler.DisposeAsync(); }
	}

	[Test]
	[Arguments(QueueRejectionReason.InvalidTarget)]
	[Arguments(QueueRejectionReason.AlreadyReleased)]
	public async Task MapSqlNotifyDoesNotBypassNonCapacityAdmissionFailures(QueueRejectionReason reason)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlRejectedCompletion");
		var realParser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await realParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPREJECTION me=think row"));
		var state = ParserState.RootFor(player.DbRef) with
		{
			Switches = ["NOTIFY"],
			Arguments = new() { ["0"] = new CallState(player.DbRef + "/MAPREJECTION"), ["1"] = new CallState("SELECT 1") }
		};
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		parser.CommandParse(Arg.Any<MString>()).Returns(CallState.Empty);
		var admission = Substitute.For<IMediator>();
		admission.Send(Arg.Any<AdmitAttributeRequest>(), Arg.Any<CancellationToken>()).Returns(new QueueAdmissionResult(1, QueueRejectionReason.None));
		admission.Send(Arg.Any<ReserveCommandListRequest>(), Arg.Any<CancellationToken>()).Returns(QueueCommandReservation.Rejected(reason));
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(SqlWebAppFactoryArg.Services, admission);
		await commands.MapSql(parser, new SharpCommandAttribute { Name = "@MAPSQL" });
		await parser.DidNotReceive().CommandParse(Arg.Any<MString>());
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(player.DbRef),
			TestHelpers.MatchingMessage(new QueueAdmissionResult(null, reason).Error), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MapSqlNotifyReleasesAnExistingWaiterAtOwnerCapacity(bool saturated)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlNotifyCapacity");
		var parser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var marker = "mapsql-completed-" + Guid.NewGuid().ToString("N");
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPCAPACITY me=think row-" + marker));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wait me=think " + marker));
		var scheduler = SqlWebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
		var options = SqlWebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var blocker = await scheduler.AdmitWork(async () =>
		{
			started.TrySetResult();
			await release.Task;
			return null;
		}, "mapsql-completion-test", "test");
		await Assert.That(blocker.Accepted).IsTrue();
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (saturated)
			{
				for (var i = 3; i < options.CurrentValue.Limit.PlayerQueueLimit; i++)
				{
					var admission = await scheduler.AdmitCommandList(MarkupText.Plain("think reserved"),
						ParserState.RootFor(player.DbRef), TimeSpan.FromHours(1));
					await Assert.That(admission.Accepted).IsTrue();
				}
			}
			await parser.CommandParse(player.Handle, ConnectionService,
				MarkupText.Plain("@mapsql/notify me/MAPCAPACITY=SELECT 1 AS col1"));
			release.TrySetResult();
			await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, player.DbRef, text => text == marker, timeoutMs: 3000);
			var messages = SqlWebAppFactoryArg.Notifications.For(player.DbRef)
				.Where(text => text == marker || text == "row-" + marker).ToArray();
			await Assert.That(messages).IsEquivalentTo(new[] { "row-" + marker, marker });
			await Assert.That(messages[0]).IsEqualTo("row-" + marker);
		}
		finally
		{
			release.TrySetResult();
			await scheduler.Halt(player.DbRef);
			await scheduler.HaltByPid(blocker.Pid!.Value);
		}
	}

	[Test]
	public async Task MapSqlNotifyDoesNotCreateACreditBeforeQueuedRowsRun()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlOrderedCompletion");
		var parser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var marker = "ordered-map-" + Guid.NewGuid().ToString("N");
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPORDER me=think row-" + marker));
		var scheduler = SqlWebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
		var options = SqlWebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var blocker = await scheduler.AdmitWork(async () => { started.TrySetResult(); await release.Task; return null; }, "mapsql-order-test", "test");
		var reservations = new List<long>();
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
			for (var i = 2; i < options.CurrentValue.Limit.PlayerQueueLimit; i++)
			{
				var admission = await scheduler.AdmitCommandList(MarkupText.Plain("think reserved"), ParserState.RootFor(player.DbRef), TimeSpan.FromHours(1));
				await Assert.That(admission.Accepted).IsTrue();
				reservations.Add(admission.Pid!.Value);
			}
			await parser.CommandParse(player.Handle, ConnectionService,
				MarkupText.Plain("@mapsql/notify me/MAPORDER=SELECT 1 AS col1 UNION ALL SELECT 2 AS col1"));
			var counter = await Mediator.CreateStream(new GetAttributeQuery(player.DbRef, ["SEMAPHORE"])).LastOrDefaultAsync();
			await Assert.That(counter?.Value.ToPlainText() ?? "0").IsEqualTo("0");
			await scheduler.HaltByPid(reservations[0]);
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wait me=think " + marker));
			counter = await Mediator.CreateStream(new GetAttributeQuery(player.DbRef, ["SEMAPHORE"])).LastOrDefaultAsync();
			await Assert.That(counter!.Value.ToPlainText()).IsEqualTo("1");
			release.TrySetResult();
			await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, player.DbRef, text => text == marker, timeoutMs: 3000);
			var messages = SqlWebAppFactoryArg.Notifications.For(player.DbRef)
				.Where(text => text == marker || text == "row-" + marker).ToArray();
			await Assert.That(messages[0]).IsEqualTo("row-" + marker);
			await Assert.That(messages[^1]).IsEqualTo(marker);
		}
		finally
		{
			release.TrySetResult();
			await scheduler.Halt(player.DbRef);
			await scheduler.HaltByPid(blocker.Pid!.Value);
		}
	}

	[Test]
	public async Task MapSqlNotifyQueryErrorDoesNotLeakCompletionReservation()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlCompletionError");
		var parser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPERROR me=think unreachable"));
		var scheduler = SqlWebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
		var before = scheduler.GetQueueUsage().Total;
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@mapsql/notify me/MAPERROR=SELECT missing_column FROM test_mapsql_data_cmd"));
		await Assert.That(scheduler.GetQueueUsage().Total).IsEqualTo(before);
		await Assert.That(await Mediator.CreateStream(new GetAttributeQuery(player.DbRef, ["SEMAPHORE"])).AnyAsync()).IsFalse();
	}

	[Test]
	public async Task MapSqlNotifyWithZeroRowsStillPublishesCompletion()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlEmptyCompletion");
		var parser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var marker = "empty-map-" + Guid.NewGuid().ToString("N");
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPEMPTY me=think unexpected-row"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wait me=think " + marker));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@mapsql/notify me/MAPEMPTY=SELECT 1 WHERE 0"));
		await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, player.DbRef, text => text == marker, timeoutMs: 3000);
	}

	[Test]
	[Arguments(false, QueueRejectionReason.InvalidTarget)]
	[Arguments(true, QueueRejectionReason.InvalidTarget)]
	[Arguments(false, QueueRejectionReason.OwnerLimit)]
	[Arguments(true, QueueRejectionReason.OwnerLimit)]
	public async Task MapSqlReportsInvalidTargetAdmission(bool header, QueueRejectionReason reason)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlInvalidAdmission");
		var realParser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await realParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPINVALID me=think unreachable"));
		var state = ParserState.RootFor(player.DbRef) with
		{
			Switches = header ? ["COLNAMES"] : [],
			Arguments = new() { ["0"] = new CallState(player.DbRef + "/MAPINVALID"), ["1"] = new CallState("SELECT 1 AS col1") }
		};
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		var admission = Substitute.For<IMediator>();
		var rejected = new QueueAdmissionResult(null, reason);
		admission.Send(Arg.Any<AdmitAttributeRequest>(), Arg.Any<CancellationToken>()).Returns(rejected);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(SqlWebAppFactoryArg.Services, admission);
		await commands.MapSql(parser, new SharpCommandAttribute { Name = "@MAPSQL" });
		await Assert.That(SqlWebAppFactoryArg.Notifications.For(player.DbRef).Count(message => message == rejected.Error))
			.IsEqualTo(reason == QueueRejectionReason.InvalidTarget ? 1 : 0);
	}

	[Test]
	[Arguments("reservation")]
	[Arguments("header")]
	[Arguments("row")]
	public async Task MapSqlAdmissionPipelineReceivesExecutionCancellation(string boundary)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			SqlWebAppFactoryArg.Services, Mediator, ConnectionService, "MapSqlPipelineCancellation");
		var realParser = SqlWebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await realParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&MAPPIPELINE me=think row"));
		var state = ParserState.RootFor(player.DbRef) with
		{
			Switches = boundary == "header" ? ["NOTIFY", "COLNAMES"] : ["NOTIFY"],
			Arguments = new() { ["0"] = new CallState(player.DbRef + "/MAPPIPELINE"), ["1"] = new CallState("SELECT 1 AS col1") }
		};
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		async Task<T> Block<T>(CancellationToken token)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
			throw new InvalidOperationException("Unreachable blocked pipeline continuation");
		}
		var admission = Substitute.For<IMediator>();
		admission.Send(Arg.Any<ReserveCommandListRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
			boundary == "reservation" ? new ValueTask<QueueCommandReservation>(Block<QueueCommandReservation>(call.Arg<CancellationToken>()))
				: Mediator.Send(call.Arg<ReserveCommandListRequest>(), call.Arg<CancellationToken>()));
		admission.Send(Arg.Any<AdmitAttributeRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
			new ValueTask<QueueAdmissionResult>(Block<QueueAdmissionResult>(call.Arg<CancellationToken>())));
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(SqlWebAppFactoryArg.Services, admission);
		var operation = commands.MapSql(parser, new SharpCommandAttribute { Name = "@MAPSQL" }).AsTask();
		try
		{
			await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(3))).IsEqualTo(budget.Token);
			cancel.Cancel();
			OperationCanceledException? cancellation = null;
			try { await operation.WaitAsync(TimeSpan.FromSeconds(3)); }
			catch (OperationCanceledException ex) { cancellation = ex; }
			await Assert.That(cancellation).IsNotNull();
			await Assert.That(cancellation!.CancellationToken).IsEqualTo(budget.Token);
		}
		finally
		{
			cleanup.Cancel();
			try { await operation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask ListCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@list commands"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Current Message of the Day settings:"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnrecycleCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@unrecycle #100"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("@UNRECYCLE: Object recovery system not yet implemented."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DisableCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@disable TestCommand"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("No configuration option named 'TestCommand'."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnableCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@enable TestCommand"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("No configuration option named 'TestCommand'."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ClockCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@channel/add TestClockChannel"));

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@clock/join TestClockChannel=#TRUE"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask Test_Sql_SelectSingleRow()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlSelSingle");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql SELECT name, value FROM test_sql_data_cmd WHERE id = 1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "test_sql_row1")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_SelectMultipleRows()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlSelMulti");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql SELECT name FROM test_sql_data_cmd ORDER BY id"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "test_sql_row1") && TestHelpers.MessagePlainTextContains(msg, "test_sql_row2")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_SelectWithWhere()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlSelWhere");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql SELECT value FROM test_sql_data_cmd WHERE name = 'test_sql_row2'"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "200")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_Count()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlCount");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql SELECT COUNT(*) as total FROM test_sql_data_cmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "3")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_NoResults()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlNoResults");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql SELECT * FROM test_sql_data_cmd WHERE id = 999"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_Basic()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlBasic");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdBasic");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&mapsql_test_attr_basic {objDbRef}=think Test_MapSql_Basic: %0 - %1 - %2"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain($"@mapsql {objDbRef}/mapsql_test_attr_basic=SELECT col1, col2 FROM test_mapsql_data_cmd WHERE id = 1"));

		// Poll until the channel consumer has processed the queued attribute execution
		await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, wizardPlayer.DbRef, m => m.Contains("Test_MapSql_Basic"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_Basic")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithMultipleRows()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlMultiRow");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdMulti");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&mapsql_test_attr_mr {objDbRef}=think Test_MapSql_WithMultipleRows: %0 - %1 - %2 - %3"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain($"@mapsql {objDbRef}/mapsql_test_attr_mr=SELECT col1, col2, col3 FROM test_mapsql_data_cmd ORDER BY id"));

		// Poll until the channel consumer has processed the queued attribute executions (wait for last row)
		await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, wizardPlayer.DbRef, m => m.Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Test_MapSql_WithMultipleRows: 0")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_WithMultipleRows: 1 - data1_col1 - data1_col2 - 10")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_WithMultipleRows: 2 - data2_col1 - data2_col2 - 20")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_WithMultipleRows: 3 - data3_col1 - data3_col2 - 30")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		// TODO: There is a bug here. It keeps reading and loops around somehow. I don't get how.
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithColnamesSwitch()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlColnames");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdColnames");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&mapsql_test_attr_cn {objDbRef}=think Test_MapSql_WithColnamesSwitch: %0 - %1 - %2 - %3"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain($"@mapsql/colnames {objDbRef}/mapsql_test_attr_cn=SELECT col1, col2, col3 FROM test_mapsql_data_cmd WHERE id = 1"));

		// Poll until the channel consumer has processed the queued attribute executions (wait for last row)
		await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, wizardPlayer.DbRef, m => m.Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_WithColnamesSwitch: 0 - col1 - col2 - col3")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_WithColnamesSwitch: 1 - data1_col1 - data1_col2 - 10")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_InvalidObjectAttribute()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlInvalidObj");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@mapsql invalid=SELECT * FROM test_mapsql_data_cmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "#-1 INVALID OBJECT/ATTRIBUTE")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_InvalidQuery()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlInvalidQuery");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql SELECT * FROM nonexistent_table"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "#-1 SQL ERROR")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_SelectWithParameter()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepSelParam");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql/PREPARE lit(SELECT name FROM test_sql_data_cmd WHERE id = ?),1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "test_sql_row1")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_SelectWithMultipleParameters()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepSelMulti");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql/PREPARE lit(SELECT name FROM test_sql_data_cmd WHERE id >= ? AND id <= ? ORDER BY id),1,2"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "test_sql_row1") && TestHelpers.MessagePlainTextContains(msg, "test_sql_row2")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_WhereClauseWithStringParameter()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepWhereStr");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql/PREPARE lit(SELECT value FROM test_sql_data_cmd WHERE name = ?),test_sql_row2"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "200")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_NoResults()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepNoRes");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql/PREPARE lit(SELECT * FROM test_sql_data_cmd WHERE id = ?),999"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_Basic()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlPrepBasic");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdPrepBasic");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&mapsql_prepare_test_attr_basic {objDbRef}=think Test_MapSql_PrepareSwitch_Basic: %0 - %1 - %2"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain($"@mapsql/PREPARE {objDbRef}/mapsql_prepare_test_attr_basic=lit(SELECT col1 FROM test_mapsql_data_cmd WHERE id = ?),1"));

		// Poll until the channel consumer has processed the queued attribute execution
		await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, wizardPlayer.DbRef, m => m.Contains("Test_MapSql_PrepareSwitch_Basic"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_PrepareSwitch_Basic")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_WithMultipleRows()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlPrepMulti");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdPrepMulti");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&mapsql_prepare_test_attr_mr {objDbRef}=think Test_MapSql_PrepareSwitch_WithMultipleRows: %0 - %1 - %2 - %3"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain($"@mapsql/PREPARE {objDbRef}/mapsql_prepare_test_attr_mr=lit(SELECT col1 FROM test_mapsql_data_cmd WHERE id <= ? ORDER BY id),2"));

		// Poll until the channel consumer has processed the queued attribute executions (wait for last row)
		await WaitForNotificationAsync(SqlWebAppFactoryArg.Notifications, wizardPlayer.DbRef, m => m.Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 2 - data2_col1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_PrepareSwitch_WithMultipleRows: 1 - data1_col1")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test_MapSql_PrepareSwitch_WithMultipleRows: 2 - data2_col1")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_InvalidObjectAttribute()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlPrepInvalid");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@mapsql/PREPARE invalid=SELECT * FROM test_mapsql_data_cmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "#-1 INVALID OBJECT/ATTRIBUTE")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_PrepareSwitch_InvalidQuery()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepInvalidQ");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MarkupText.Plain("@sql/PREPARE SELECT * FROM nonexistent_table"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "#-1 SQL ERROR")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Polls the thread-safe recipient queue until the expected message arrives.
	/// </summary>
	private static async Task WaitForNotificationAsync(
		TestHelpers.NotificationRecorder notifications,
		DBRef recipient,
		Func<string, bool> messagePredicate,
		int timeoutMs = 15000,
		int pollIntervalMs = 50)
	{
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		while (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < timeoutMs)
		{
			if (notifications.For(recipient).Any(messagePredicate)) return;
			await Task.Delay(pollIntervalMs);
		}
		throw new TimeoutException($"Timed out after {timeoutMs}ms waiting for expected notification for {recipient}.");
	}
}
