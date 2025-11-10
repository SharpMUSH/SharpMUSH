using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class DatabaseCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory SqlWebAppFactoryArg { get; init; }

	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	private INotifyService NotifyService => SqlWebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => SqlWebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => SqlWebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

	[Before(Test)]
	public async Task InitializeAsync()
	{
		// Create test database and populate with test data
		var connectionString = MySqlTestServer.Instance.GetConnectionString();
		await using var connection = new MySqlConnection(connectionString);
		await connection.OpenAsync();

		// Create test tables
		await using (var cmd = new MySqlCommand("""
		                                        			CREATE TABLE IF NOT EXISTS test_sql_data (
		                                        				id INT PRIMARY KEY AUTO_INCREMENT,
		                                        				name VARCHAR(255),
		                                        				value INT
		                                        			)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("""
		                                        			CREATE TABLE IF NOT EXISTS test_mapsql_data (
		                                        				id INT PRIMARY KEY AUTO_INCREMENT,
		                                        				col1 VARCHAR(50),
		                                        				col2 VARCHAR(50),
		                                        				col3 INT
		                                        			)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		// Insert test data
		await using (var cmd = new MySqlCommand("""
		                                        			INSERT INTO test_sql_data (name, value) VALUES 
		                                        			('test_sql_row1', 100),
		                                        			('test_sql_row2', 200),
		                                        			('test_sql_row3', 300)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("""
		                                        			INSERT INTO test_mapsql_data (col1, col2, col3) VALUES 
		                                        			('data1_col1', 'data1_col2', 10),
		                                        			('data2_col1', 'data2_col2', 20),
		                                        			('data3_col1', 'data3_col2', 30)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ListCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list commands"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnrecycleCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@unrecycle #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DisableCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable TestCommand"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnableCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable TestCommand"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT name, value FROM test_sql_data WHERE id = 1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1"))));
	}

	[Test]
	public async ValueTask Test_Sql_SelectMultipleRows()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT name FROM test_sql_data ORDER BY id"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("test_sql_row1") && msg.AsT0.ToString().Contains("test_sql_row2")) ||
				(msg.IsT1 && msg.AsT1.Contains("test_sql_row1") && msg.AsT1.Contains("test_sql_row2"))));
	}

	[Test]
	public async ValueTask Test_Sql_SelectWithWhere()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT value FROM test_sql_data WHERE name = 'test_sql_row2'"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("200")) ||
				(msg.IsT1 && msg.AsT1.Contains("200"))));
	}

	[Test]
	public async ValueTask Test_Sql_Count()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT COUNT(*) as total FROM test_sql_data"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("3")) ||
				(msg.IsT1 && msg.AsT1.Contains("3"))));
	}

	[Test]
	public async ValueTask Test_Sql_NoResults()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT * FROM test_sql_data WHERE id = 999"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "") ||
				(msg.IsT1 && msg.AsT1 == "")));
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_Basic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&mapsql_test_attr_basic #1=think Test_MapSql_Basic: %0 - %1 - %2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql #1/mapsql_test_attr_basic=SELECT col1, col2 FROM test_mapsql_data WHERE id = 1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_Basic")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_Basic"))));
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithMultipleRows()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&mapsql_test_attr_mr #1=think Test_MapSql_WithMultipleRows: %0 - %1 - %2 - %3"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql #1/mapsql_test_attr_mr=SELECT col1, col2, col3 FROM test_mapsql_data ORDER BY id"));

		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().StartsWith("Test_MapSql_WithMultipleRows: 0 ")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithMultipleRows: 0"))));
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 1 - data1_col1 - data1_col2 - 10")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 1 - data1_col1 - data1_col2 - 10"))));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 2 - data2_col1 - data2_col2 - 20")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 2 - data2_col1 - data2_col2 - 20"))));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1 - data3_col2 - 30")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithMultipleRows: 3 - data3_col1 - data3_col2 - 30"))));

		// TODO: There is a bug here. It keeps reading and loops around somehow. I don't get how.
		/*
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().StartsWith("Test_MapSql_WithMultipleRows: 4")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithMultipleRows: 4"))));
				*/
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_WithColnamesSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&mapsql_test_attr_cn #1=think Test_MapSql_WithColnamesSwitch: %0 - %1 - %2 - %3"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql/colnames #1/mapsql_test_attr_cn=SELECT col1, col2, col3 FROM test_mapsql_data WHERE id = 1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithColnamesSwitch: 0 - col1 - col2 - col3")) ||
				(msg.IsT1 && msg.AsT1.StartsWith("Test_MapSql_WithColnamesSwitch: 0 - col1 - col2 - col3"))));
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1 - data1_col2 - 10")) ||
				(msg.IsT1 && msg.AsT1.Contains("Test_MapSql_WithColnamesSwitch: 1 - data1_col1 - data1_col2 - 10"))));
	}

	[Test]
	[NotInParallel]
	public async ValueTask Test_MapSql_InvalidObjectAttribute()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&mapsql_test_attr #1=think %0 - %1 - %2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql invalid=SELECT * FROM test_mapsql_data"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 INVALID OBJECT/ATTRIBUTE")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 INVALID OBJECT/ATTRIBUTE"))));
	}

	[Test]
	public async ValueTask Test_Sql_InvalidQuery()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT * FROM nonexistent_table"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1 SQL ERROR")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1 SQL ERROR"))));
	}
}
