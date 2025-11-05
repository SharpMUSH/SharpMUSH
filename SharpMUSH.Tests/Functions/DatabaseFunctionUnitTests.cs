using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class DatabaseFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// sql() function tests - SQL is enabled in test environment
	[Test]
	[Arguments("sql(SELECT * FROM nonexistent)", "#-1 SQL ERROR")]
	public async Task Test_Sql_TableDoesNotExist(string str, string expectedPrefix)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var plainText = result.ToPlainText();
		await Assert.That(plainText).StartsWith(expectedPrefix);
	}

	// sqlescape() function tests - SQL is enabled in test environment
	[Test]
	[Arguments("sqlescape(test_string_sqlescape_case1)", "test_string_sqlescape_case1")]
	public async Task Test_Sqlescape_NoQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(test'string)", "test\\'string")]
	public async Task Test_Sqlescape_SingleQuote(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// MySqlHelper.EscapeString escapes with backslash, not doubling
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
	[Arguments("sqlescape()", "")]
	public async Task Test_Sqlescape_EmptyString(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// mapsql() function tests - SQL is enabled in test environment
	// Note: mapsql requires a valid attribute to exist, so we test the error case for non-existent attribute
	[Test]
	[Arguments("mapsql(me/nonexistent_attr_test,SELECT 1)", "#-1 NO SUCH ATTRIBUTE")]
	public async Task Test_Mapsql_AttributeDoesNotExist(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
