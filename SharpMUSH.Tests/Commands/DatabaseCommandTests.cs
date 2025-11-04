using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class DatabaseCommandTests : IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory SqlWebAppFactoryArg { get; init; }

	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	private INotifyService NotifyService => SqlWebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => SqlWebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => SqlWebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

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

	public async ValueTask DisposeAsync()
	{
		// Clean up test data
		var connectionString = MySqlTestServer.Instance.GetConnectionString();
		await using var connection = new MySqlConnection(connectionString);
		await connection.OpenAsync();

		await using (var cmd = new MySqlCommand("DROP TABLE IF EXISTS test_sql_data", connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		await using (var cmd = new MySqlCommand("DROP TABLE IF EXISTS test_mapsql_data", connection))
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
	[Skip("Not Yet Implemented")]
	public async ValueTask ClockCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@clock"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
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
	public async ValueTask Test_MapSql_Basic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql #1/test_attr=SELECT col1, col2 FROM test_mapsql_data WHERE id = 1"));

		// MapSql queues attributes, so we check that it completes without error
		// The actual attribute execution would happen asynchronously
		// We can verify it didn't throw an error by checking no error message was sent
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1"))));
	}

	[Test]
	public async ValueTask Test_MapSql_WithMultipleRows()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql #1/test_attr=SELECT col1, col2, col3 FROM test_mapsql_data ORDER BY id"));

		// Verify no errors
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1"))));
	}

	[Test]
	public async ValueTask Test_MapSql_WithColnamesSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql/colnames #1/test_attr=SELECT col1, col2 FROM test_mapsql_data WHERE id = 1"));

		// Verify no errors - with /colnames, should queue an extra row for column names
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1"))));
	}

	[Test]
	public async ValueTask Test_MapSql_InvalidObjectAttribute()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql invalid=SELECT * FROM test_mapsql_data"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("#-1")) ||
				(msg.IsT1 && msg.AsT1.Contains("#-1"))));
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
