using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class DatabaseFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[NotInParallel]
	[Before(Test)]
	public async Task InitializeAsync()
	{
		// Use unique table names for function tests to avoid interference with command tests
		var connectionString = MySqlTestServer.Instance.GetConnectionString();
		
		await using var connection = new MySqlConnection(connectionString);
		await connection.OpenAsync();

		// Verify test_sql_data_func exists
		await using (var cmd = new MySqlCommand("""
		                                        	CREATE TABLE IF NOT EXISTS test_sql_data_func (
		                                        		id INT PRIMARY KEY AUTO_INCREMENT,
		                                        		name VARCHAR(255),
		                                        		value INT
		                                        	)
		                                        """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		// Truncate to ensure clean state
		await using (var cmd = new MySqlCommand("TRUNCATE TABLE test_sql_data_func", connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}

		// Insert test data
		await using (var cmd = new MySqlCommand("""
		                                       	INSERT INTO test_sql_data_func (name, value) VALUES 
		                                       	('test_sql_row1', 100),
		                                       	('test_sql_row2', 200),
		                                       	('test_sql_row3', 300)
		                                       """, connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}
	}

	[Test]
	public async Task Test_Sql_SelectSingleRow()
	{
		var result =
			(await Parser.FunctionParse(MModule.single("sql(lit(SELECT `name`,`value` FROM `test_sql_data_func` WHERE id = 1))")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("test_sql_row1");
		await Assert.That(plainText).Contains("100");
	}

	[Test]
	public async Task Test_Sql_SelectMultipleRows_DefaultSeparators()
	{
		var result = (await Parser.FunctionParse(MModule.single("sql(SELECT `name` FROM `test_sql_data_func` ORDER BY id)")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("test_sql_row1");
		await Assert.That(plainText).Contains("test_sql_row2");
		await Assert.That(plainText).Contains("test_sql_row3");
	}

	[Test]
	public async Task Test_Sql_SelectWithCustomRowSeparator()
	{
		var result = (await Parser.FunctionParse(MModule.single("sql(SELECT `name` FROM `test_sql_data_func` ORDER BY id,|)")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("test_sql_row1|test_sql_row2|test_sql_row3");
	}

	[Test]
	public async Task Test_Sql_SelectWithCustomFieldSeparator()
	{
		var result =
			(await Parser.FunctionParse(
				MModule.single("sql(lit(SELECT `name`,`value` FROM `test_sql_data_func` WHERE id = 1),%b,~)")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("test_sql_row1~100");
	}

	[Test]
	public async Task Test_Sql_SelectWithCustomSeparators()
	{
		var result =
			(await Parser.FunctionParse(
				MModule.single("sql(lit(SELECT `name`,`value` FROM `test_sql_data_func` ORDER BY id),|,~)")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("test_sql_row1~100");
		await Assert.That(plainText).Contains("test_sql_row2~200");
		await Assert.That(plainText).Contains("|");
	}

	[Test]
	public async Task Test_Sql_Count()
	{
		var result = (await Parser.FunctionParse(MModule.single("sql(lit(SELECT `id` FROM `test_sql_data_func`))")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsNotEmpty();
	}

	[Test]
	public async Task Test_Sql_WhereClause()
	{
		var result =
			(await Parser.FunctionParse(MModule.single("sql(lit(SELECT `value` FROM `test_sql_data_func` WHERE id = 2))")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEqualTo("200");
	}

	[Test]
	public async Task Test_Sql_NoResults()
	{
		var result = (await Parser.FunctionParse(MModule.single("sql(lit(SELECT * FROM `test_sql_data_func` WHERE id = 999))")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEmpty();
	}

	[Test]
	public async Task Test_Sql_TableDoesNotExist()
	{
		var result = (await Parser.FunctionParse(MModule.single("sql(lit(SELECT * FROM `nonexistent_table`))")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).StartsWith("#-1 SQL ERROR");
	}

	[Test]
	public async Task Test_Sql_WithRegister()
	{
		var result =
			(await Parser.FunctionParse(MModule.single("sql(lit(SELECT `name` FROM `test_sql_data_func`), , ,rowcount)")))
			?.Message!;

		// The register should be set, but we can't easily test it in function context
		// Just verify the query succeeded
		await Assert.That(result.ToPlainText()).Contains("test_sql_row");
	}

	[Test]
	[Arguments("sqlescape(test_string_no_special_chars)", "test_string_no_special_chars")]
	public async Task Test_Sqlescape_NoSpecialChars(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(test'string)", "test\\'string")]
	public async Task Test_Sqlescape_SingleQuote(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(You don't say)", "You don\\'t say")]
	public async Task Test_Sqlescape_MultipleQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(test''double)", "test\\'\\'double")]
	public async Task Test_Sqlescape_DoubleQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(It's a test's test)", "It\\'s a test\\'s test")]
	public async Task Test_Sqlescape_ManyQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(Jim = \"John\" and 'Jimmy')", "Jim = \\\"John\\\" and \\'Jimmy\\'")]
	public async Task Test_Sqlescape_MixedQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Test_Sqlescape_RealWorldUse()
	{
		// Test using sqlescape in an actual query
		var escapedValue = (await Parser.FunctionParse(MModule.single("sqlescape(test_sql_row1)")))?.Message!.ToPlainText();
		var query = $"Test_Sqlescape_RealWorldUse: [sql(lit(SELECT DISTINCT value FROM test_sql_data_func WHERE name = '{escapedValue}'))]";
		var result = (await Parser.FunctionParse(MModule.single(query)))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("Test_Sqlescape_RealWorldUse: 100");
	}

	[Test]
	public async Task Test_Sqlescape_PreventInjection()
	{
		var maliciousInput = "test' OR '1'='1";
		var escaped = (await Parser.FunctionParse(MModule.single($"sqlescape({maliciousInput})")))?.Message!.ToPlainText();

		await Assert.That(escaped).Contains("\\'");

		var query = $"sql(lit(SELECT * FROM test_sql_data_func WHERE name = '{escaped}'))";
		var result = (await Parser.FunctionParse(MModule.single(query)))?.Message!;

		await Assert.That(result.ToPlainText()).IsEmpty();
	}

	[Test]
	public async Task Test_Mapsql_AttributeDoesNotExist()
	{
		var result = (await Parser.FunctionParse(MModule.single("mapsql(me/nonexistent_attr_test,SELECT 1)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1 NO SUCH ATTRIBUTE");
	}

	[Test]
	public async Task Test_Mapsql_BasicExecution()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&Test_Mapsql_BasicExecution #1=Test_Mapsql_BasicExecution: Row %0 has value %2"));

		await Task.Delay(100);

		var result =
			(await Parser.FunctionParse(MModule.single(
				"mapsql(#1/Test_Mapsql_BasicExecution,lit(SELECT `name`,`value` FROM `test_sql_data_func` WHERE id = 1))")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("Test_Mapsql_BasicExecution: Row 1 has value 100");
	}

	[Test]
	public async Task Test_Mapsql_BasicExecution2()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&Test_Mapsql_BasicExecution2 #1=Test_Mapsql_BasicExecution2: Row %0 has value %2"));

		await Task.Delay(100);

		var result =
			(await Parser.FunctionParse(
				MModule.single("mapsql(#1/Test_Mapsql_BasicExecution2,lit(SELECT `name`,`value` FROM `test_sql_data_func` LIMIT 3),%r)")))
			?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("Test_Mapsql_BasicExecution2: Row 1 has value 100" +
		                                                  "\nTest_Mapsql_BasicExecution2: Row 2 has value 200" +
		                                                  "\nTest_Mapsql_BasicExecution2: Row 3 has value 300");
	}

	[Test]
	public async Task Test_Mapsql_InvalidObjectAttribute()
	{
		var result = (await Parser.FunctionParse(MModule.single("mapsql(invalid_format,SELECT 1)")))?.Message!;
		await Assert.That(result.ToPlainText()).Contains("#-1");
	}

	[Test]
	public async Task Test_Mapsql_TableDoesNotExist()
	{
		// Set up an attribute first
		await Parser.CommandParse(1, ConnectionService, MModule.single("&Test_Mapsql_TableDoesNotExist #1=think %0"));
		await Task.Delay(100);

		var result =
			(await Parser.FunctionParse(
				MModule.single("mapsql(#1/Test_Mapsql_TableDoesNotExist,lit(SELECT * FROM nonexistent_table))")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1 SQL ERROR");
	}

	// ===== Prepared Statement Tests =====

	[Test]
	public async Task Test_Sql_PreparedStatement_SelectWithParameter()
	{
		// sql() with more than 4 args automatically uses prepared statements
		// Args: query, rowsep, fieldsep, register, param1
		var result =
			(await Parser.FunctionParse(MModule.single("sql(lit(SELECT `name`,`value` FROM `test_sql_data_func` WHERE id = ?),%b,%b,%b,1)")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("test_sql_row1");
		await Assert.That(plainText).Contains("100");
	}

	[Test]
	public async Task Test_Sql_PreparedStatement_SelectWithMultipleParameters()
	{
		// Args: query, rowsep, fieldsep, register, param1, param2
		var result =
			(await Parser.FunctionParse(MModule.single("sql(lit(SELECT `name` FROM `test_sql_data_func` WHERE id >= ? AND id <= ? ORDER BY id),%b,%b,%b,1,2)")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("test_sql_row1");
		await Assert.That(plainText).Contains("test_sql_row2");
	}

	[Test]
	public async Task Test_Sql_PreparedStatement_WhereClauseWithStringParameter()
	{
		var result =
			(await Parser.FunctionParse(MModule.single("sql(lit(SELECT `value` FROM `test_sql_data_func` WHERE name = ? LIMIT 1),%b,%b,%b,test_sql_row2)")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEqualTo("200");
	}

	[Test]
	public async Task Test_Sql_PreparedStatement_NoResults()
	{
		var result = (await Parser.FunctionParse(MModule.single("sql(lit(SELECT * FROM `test_sql_data_func` WHERE id = ?),%b,%b,%b,999)")))
			?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEmpty();
	}

	[Test]
	public async Task Test_Sql_PreparedStatement_PreventsSqlInjection()
	{
		// Try to inject SQL via a string parameter - prepared statements should prevent this
		// Use a string that would normally cause issues if directly concatenated
		var result = (await Parser.FunctionParse(MModule.single("sql(lit(SELECT * FROM `test_sql_data_func` WHERE name = ?),%b,%b,%b,test' OR '1'='1)")))
			?.Message!;
		var plainText = result.ToPlainText();

		// The parameter "test' OR '1'='1" should be treated as a literal string value
		// This should fail to find a match since there's no row with that exact name
		await Assert.That(plainText).IsEmpty();
	}

	[Test]
	public async Task Test_Mapsql_PreparedStatement_BasicExecution()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&Test_Mapsql_PreparedStatement_BasicExecution #1=Test_Mapsql_PreparedStatement_BasicExecution: Row %0 has value %2"));

		await Task.Delay(100);

		// Args: obj/attr, query, osep, fieldnames, param1
		var result =
			(await Parser.FunctionParse(MModule.single(
				"mapsql(#1/Test_Mapsql_PreparedStatement_BasicExecution,lit(SELECT `name`,`value` FROM `test_sql_data_func` WHERE id = ?),%b,0,1)")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("Test_Mapsql_PreparedStatement_BasicExecution: Row 1 has value 100");
	}

	[Test]
	public async Task Test_Mapsql_PreparedStatement_MultipleRows()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&Test_Mapsql_PreparedStatement_MultipleRows #1=Test_Mapsql_PreparedStatement_MultipleRows: Row %0 has value %2"));

		await Task.Delay(100);

		var result =
			(await Parser.FunctionParse(
				MModule.single("mapsql(#1/Test_Mapsql_PreparedStatement_MultipleRows,lit(SELECT `name`,`value` FROM `test_sql_data_func` WHERE id <= ? ORDER BY id),%b,0,2)")))
			?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("Test_Mapsql_PreparedStatement_MultipleRows: Row 1 has value 100 Test_Mapsql_PreparedStatement_MultipleRows: Row 2 has value 200");
	}

	[Test]
	public async Task Test_Mapsql_PreparedStatement_WithStringParameter()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&Test_Mapsql_PreparedStatement_StringParam #1=Test_Mapsql_PreparedStatement_StringParam: %1=%2"));

		await Task.Delay(500); // Increased delay to ensure attribute is fully set

		var result =
			(await Parser.FunctionParse(
				MModule.single("mapsql(#1/Test_Mapsql_PreparedStatement_StringParam,lit(SELECT `name`,`value` FROM `test_sql_data_func` WHERE name = ?),%b,0,test_sql_row3)")))
			?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("Test_Mapsql_PreparedStatement_StringParam: test_sql_row3=300");
	}

	[Test]
	public async Task Test_Mapsql_PreparedStatement_InvalidObjectAttribute()
	{
		var result = (await Parser.FunctionParse(MModule.single("mapsql(invalid_format,SELECT 1,%b,0,param)")))?.Message!;
		await Assert.That(result.ToPlainText()).Contains("#-1");
	}

	[Test]
	public async Task Test_Mapsql_PreparedStatement_TableDoesNotExist()
	{
		// Set up an attribute first
		await Parser.CommandParse(1, ConnectionService, MModule.single("&Test_Mapsql_PreparedStatement_TableDoesNotExist #1=think %0"));
		await Task.Delay(100);

		var result =
			(await Parser.FunctionParse(
				MModule.single("mapsql(#1/Test_Mapsql_PreparedStatement_TableDoesNotExist,lit(SELECT * FROM nonexistent_table),%b,0,param)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1 SQL ERROR");
	}
}