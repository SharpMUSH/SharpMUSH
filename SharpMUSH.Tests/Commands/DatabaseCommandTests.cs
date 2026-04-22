using Mediator;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
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
		// Grant WIZARD using God's (handle 1) privileged parser
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {player.DbRef}=WIZARD"));
		return player;
	}

	[Before(Test)]
	public async Task InitializeAsync()
	{
		// Use unique table names for command tests to avoid interference with function tests
		var connectionString = MySqlTestServer.Instance.GetConnectionString();

		await using var connection = new MySqlConnection(connectionString);
		await connection.OpenAsync();

		// Create test tables with unique names for command tests
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

		// Truncate tables to ensure clean state
		await using (var cmd = new MySqlCommand("TRUNCATE TABLE test_sql_data_cmd", connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("TRUNCATE TABLE test_mapsql_data_cmd", connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		// Insert test data
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
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask ListCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list commands"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Current Message of the Day settings:", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnrecycleCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@unrecycle #100"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "@UNRECYCLE: Object recovery system not yet implemented.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DisableCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable TestCommand"));
		
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "No configuration option named 'TestCommand'.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnableCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable TestCommand"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "No configuration option named 'TestCommand'.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ClockCommand()
	{
		// First create a test channel
		await Parser.CommandParse(1, ConnectionService, MModule.single("@channel/add TestClockChannel"));

		// Now test @clock to set a lock on the channel
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@clock/join TestClockChannel=#TRUE"));

		// Verify the command executed successfully (didn't throw or return error)
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask Test_Sql_SelectSingleRow()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlSelSingle");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql SELECT name, value FROM test_sql_data_cmd WHERE id = 1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_SelectMultipleRows()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlSelMulti");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql SELECT name FROM test_sql_data_cmd ORDER BY id"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1") && msg.AsT0.ToString().Contains("test_sql_row2")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1") && msg.AsT1.Contains("test_sql_row2"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_SelectWithWhere()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlSelWhere");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql SELECT value FROM test_sql_data_cmd WHERE name = 'test_sql_row2'"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("200")) ||
				(msg.IsT1 && msg.AsT1.Contains("200"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_Count()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlCount");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql SELECT COUNT(*) as total FROM test_sql_data_cmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("3")) ||
				(msg.IsT1 && msg.AsT1.Contains("3"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_NoResults()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlNoResults");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql SELECT * FROM test_sql_data_cmd WHERE id = 999"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "") ||
				(msg.IsT1 && msg.AsT1 == "")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_Basic()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlBasic");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdBasic");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_test_attr_basic {objDbRef}=think Test_MapSql_Basic: %0 - %1 - %2"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single($"@mapsql {objDbRef}/mapsql_test_attr_basic=SELECT col1, col2 FROM test_mapsql_data_cmd WHERE id = 1"));

		// Poll until the channel consumer has processed the queued attribute execution
		await WaitForNotificationAsync(NotifyService, m => m.Contains("Test_MapSql_Basic"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_Basic")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_Basic"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithMultipleRows()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlMultiRow");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdMulti");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_test_attr_mr {objDbRef}=think Test_MapSql_WithMultipleRows: %0 - %1 - %2 - %3"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single($"@mapsql {objDbRef}/mapsql_test_attr_mr=SELECT col1, col2, col3 FROM test_mapsql_data_cmd ORDER BY id"));

		// Poll until the channel consumer has processed the queued attribute executions (wait for last row)
		await WaitForNotificationAsync(NotifyService, m => m.Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().StartsWith("Test_MapSql_WithMultipleRows: 0 ")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithMultipleRows: 0"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 1 - data1_col1 - data1_col2 - 10")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 1 - data1_col1 - data1_col2 - 10"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 2 - data2_col1 - data2_col2 - 20")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 2 - data2_col1 - data2_col2 - 20"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1 - data3_col2 - 30")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1 - data3_col2 - 30"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		// TODO: There is a bug here. It keeps reading and loops around somehow. I don't get how.
		/*
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().StartsWith("Test_MapSql_WithMultipleRows: 4")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithMultipleRows: 4"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
				*/
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithColnamesSwitch()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlColnames");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdColnames");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_test_attr_cn {objDbRef}=think Test_MapSql_WithColnamesSwitch: %0 - %1 - %2 - %3"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single($"@mapsql/colnames {objDbRef}/mapsql_test_attr_cn=SELECT col1, col2, col3 FROM test_mapsql_data_cmd WHERE id = 1"));

		// Poll until the channel consumer has processed the queued attribute executions (wait for last row)
		await WaitForNotificationAsync(NotifyService, m => m.Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithColnamesSwitch: 0 - col1 - col2 - col3")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithColnamesSwitch: 0 - col1 - col2 - col3"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1 - data1_col2 - 10")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1 - data1_col2 - 10"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_InvalidObjectAttribute()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlInvalidObj");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@mapsql invalid=SELECT * FROM test_mapsql_data_cmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 INVALID OBJECT/ATTRIBUTE")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 INVALID OBJECT/ATTRIBUTE"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_InvalidQuery()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlInvalidQuery");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql SELECT * FROM nonexistent_table"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 SQL ERROR")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 SQL ERROR"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	// ===== Prepared Statement Command Tests =====

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_SelectWithParameter()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepSelParam");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		// Test using lit() to protect the query
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT name FROM test_sql_data_cmd WHERE id = ?),1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_SelectWithMultipleParameters()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepSelMulti");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT name FROM test_sql_data_cmd WHERE id >= ? AND id <= ? ORDER BY id),1,2"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1") && msg.AsT0.ToString().Contains("test_sql_row2")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1") && msg.AsT1.Contains("test_sql_row2"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_WhereClauseWithStringParameter()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepWhereStr");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT value FROM test_sql_data_cmd WHERE name = ?),test_sql_row2"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("200")) ||
				(msg.IsT1 && msg.AsT1.Contains("200"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_NoResults()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepNoRes");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT * FROM test_sql_data_cmd WHERE id = ?),999"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "") ||
				(msg.IsT1 && msg.AsT1 == "")), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_Basic()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlPrepBasic");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdPrepBasic");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_prepare_test_attr_basic {objDbRef}=think Test_MapSql_PrepareSwitch_Basic: %0 - %1 - %2"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single($"@mapsql/PREPARE {objDbRef}/mapsql_prepare_test_attr_basic=lit(SELECT col1 FROM test_mapsql_data_cmd WHERE id = ?),1"));

		// Poll until the channel consumer has processed the queued attribute execution
		await WaitForNotificationAsync(NotifyService, m => m.Contains("Test_MapSql_PrepareSwitch_Basic"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_PrepareSwitch_Basic")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_PrepareSwitch_Basic"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_WithMultipleRows()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlPrepMulti");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdPrepMulti");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_prepare_test_attr_mr {objDbRef}=think Test_MapSql_PrepareSwitch_WithMultipleRows: %0 - %1 - %2 - %3"));
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single($"@mapsql/PREPARE {objDbRef}/mapsql_prepare_test_attr_mr=lit(SELECT col1 FROM test_mapsql_data_cmd WHERE id <= ? ORDER BY id),2"));

		// Poll until the channel consumer has processed the queued attribute executions (wait for last row)
		await WaitForNotificationAsync(NotifyService, m => m.Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 2 - data2_col1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 1 - data1_col1")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 1 - data1_col1"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 2 - data2_col1")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 2 - data2_col1"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_InvalidObjectAttribute()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("MapSqlPrepInvalid");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@mapsql/PREPARE invalid=SELECT * FROM test_mapsql_data_cmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 INVALID OBJECT/ATTRIBUTE")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 INVALID OBJECT/ATTRIBUTE"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_PrepareSwitch_InvalidQuery()
	{
		var wizardPlayer = await CreateWizardTestPlayerAsync("SqlPrepInvalidQ");
		var testParser = SqlWebAppFactoryArg.CommandParserFor(wizardPlayer.DbRef, wizardPlayer.Handle);
		await testParser.CommandParse(wizardPlayer.Handle, ConnectionService, MModule.single("@sql/PREPARE SELECT * FROM nonexistent_table"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(wizardPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 SQL ERROR")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 SQL ERROR"))), TestHelpers.MatchingObject(wizardPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Polls <see cref="INotifyService.ReceivedCalls"/> until a notification whose plain-text message
	/// satisfies <paramref name="messagePredicate"/> arrives, avoiding a fixed-duration sleep.
	/// Only new calls since the previous iteration are examined to avoid redundant work.
	/// </summary>
	private static async Task WaitForNotificationAsync(
		INotifyService notifyService,
		Func<string, bool> messagePredicate,
		int timeoutMs = 5000,
		int pollIntervalMs = 50)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		var scannedCount = 0;
		while (DateTime.UtcNow < deadline)
		{
			var calls = notifyService.ReceivedCalls().ToList();
			var found = calls.Skip(scannedCount).Any(call =>
			{
				var args = call.GetArguments();
				if (args.Length < 2) return false;
				return args[1] is OneOf<MString, string> msg &&
					msg.Match(m => messagePredicate(m.ToString()), s => messagePredicate(s));
			});
			if (found) return;
			scannedCount = calls.Count;
			await Task.Delay(pollIntervalMs);
		}
		throw new TimeoutException($"Timed out after {timeoutMs}ms waiting for expected notification.");
	}
}
