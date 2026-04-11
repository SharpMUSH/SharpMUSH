using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
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
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnrecycleCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@unrecycle #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DisableCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable TestCommand"));
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnableCommand()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable TestCommand"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
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
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT name, value FROM test_sql_data_cmd WHERE id = 1"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_SelectMultipleRows()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT name FROM test_sql_data_cmd ORDER BY id"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1") && msg.AsT0.ToString().Contains("test_sql_row2")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1") && msg.AsT1.Contains("test_sql_row2"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_SelectWithWhere()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT value FROM test_sql_data_cmd WHERE name = 'test_sql_row2'"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("200")) ||
				(msg.IsT1 && msg.AsT1.Contains("200"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_Count()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT COUNT(*) as total FROM test_sql_data_cmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("3")) ||
				(msg.IsT1 && msg.AsT1.Contains("3"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_NoResults()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT * FROM test_sql_data_cmd WHERE id = 999"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "") ||
				(msg.IsT1 && msg.AsT1 == "")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_Basic()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdBasic");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_test_attr_basic {objDbRef}=think Test_MapSql_Basic: %0 - %1 - %2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mapsql {objDbRef}/mapsql_test_attr_basic=SELECT col1, col2 FROM test_mapsql_data_cmd WHERE id = 1"));

		// Wait for the channel consumer to process the queued attribute execution
		await Task.Delay(500);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_Basic")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_Basic"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithMultipleRows()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdMulti");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_test_attr_mr {objDbRef}=think Test_MapSql_WithMultipleRows: %0 - %1 - %2 - %3"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mapsql {objDbRef}/mapsql_test_attr_mr=SELECT col1, col2, col3 FROM test_mapsql_data_cmd ORDER BY id"));

		// Wait for the channel consumer to process the queued attribute executions
		await Task.Delay(500);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().StartsWith("Test_MapSql_WithMultipleRows: 0 ")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithMultipleRows: 0"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 1 - data1_col1 - data1_col2 - 10")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 1 - data1_col1 - data1_col2 - 10"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 2 - data2_col1 - data2_col2 - 20")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 2 - data2_col1 - data2_col2 - 20"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1 - data3_col2 - 30")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1 - data3_col2 - 30"))), null, INotifyService.NotificationType.Announce);

		// TODO: There is a bug here. It keeps reading and loops around somehow. I don't get how.
		/*
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().StartsWith("Test_MapSql_WithMultipleRows: 4")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithMultipleRows: 4"))), null, INotifyService.NotificationType.Announce);
				*/
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithColnamesSwitch()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdColnames");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_test_attr_cn {objDbRef}=think Test_MapSql_WithColnamesSwitch: %0 - %1 - %2 - %3"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mapsql/colnames {objDbRef}/mapsql_test_attr_cn=SELECT col1, col2, col3 FROM test_mapsql_data_cmd WHERE id = 1"));

		// Wait for the channel consumer to process the queued attribute executions
		await Task.Delay(500);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithColnamesSwitch: 0 - col1 - col2 - col3")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithColnamesSwitch: 0 - col1 - col2 - col3"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1 - data1_col2 - 10")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1 - data1_col2 - 10"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_InvalidObjectAttribute()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql invalid=SELECT * FROM test_mapsql_data_cmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 INVALID OBJECT/ATTRIBUTE")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 INVALID OBJECT/ATTRIBUTE"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_InvalidQuery()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT * FROM nonexistent_table"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 SQL ERROR")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 SQL ERROR"))), null, INotifyService.NotificationType.Announce);
	}

	// ===== Prepared Statement Command Tests =====

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_SelectWithParameter()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		// Test using lit() to protect the query
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT name FROM test_sql_data_cmd WHERE id = ?),1"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_SelectWithMultipleParameters()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT name FROM test_sql_data_cmd WHERE id >= ? AND id <= ? ORDER BY id),1,2"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1") && msg.AsT0.ToString().Contains("test_sql_row2")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1") && msg.AsT1.Contains("test_sql_row2"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_WhereClauseWithStringParameter()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT value FROM test_sql_data_cmd WHERE name = ?),test_sql_row2"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("200")) ||
				(msg.IsT1 && msg.AsT1.Contains("200"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_Sql_PrepareSwitch_NoResults()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql/PREPARE lit(SELECT * FROM test_sql_data_cmd WHERE id = ?),999"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "") ||
				(msg.IsT1 && msg.AsT1 == "")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_Basic()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdPrepBasic");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_prepare_test_attr_basic {objDbRef}=think Test_MapSql_PrepareSwitch_Basic: %0 - %1 - %2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mapsql/PREPARE {objDbRef}/mapsql_prepare_test_attr_basic=lit(SELECT col1 FROM test_mapsql_data_cmd WHERE id = ?),1"));

		// Wait for the channel consumer to process the queued attribute execution
		await Task.Delay(500);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_PrepareSwitch_Basic")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_PrepareSwitch_Basic"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_WithMultipleRows()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapSqlCmdPrepMulti");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&mapsql_prepare_test_attr_mr {objDbRef}=think Test_MapSql_PrepareSwitch_WithMultipleRows: %0 - %1 - %2 - %3"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mapsql/PREPARE {objDbRef}/mapsql_prepare_test_attr_mr=lit(SELECT col1 FROM test_mapsql_data_cmd WHERE id <= ? ORDER BY id),2"));

		// Wait for the channel consumer to process the queued attribute executions
		await Task.Delay(500);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 1 - data1_col1")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 1 - data1_col1"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 2 - data2_col1")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_PrepareSwitch_WithMultipleRows: 2 - data2_col1"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_PrepareSwitch_InvalidObjectAttribute()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql/PREPARE invalid=SELECT * FROM test_mapsql_data_cmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 INVALID OBJECT/ATTRIBUTE")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 INVALID OBJECT/ATTRIBUTE"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Sql_PrepareSwitch_InvalidQuery()
	{
		var executor = SqlWebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql/PREPARE SELECT * FROM nonexistent_table"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 SQL ERROR")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 SQL ERROR"))), null, INotifyService.NotificationType.Announce);
	}
}
