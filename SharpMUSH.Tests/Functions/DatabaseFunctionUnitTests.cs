using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class DatabaseFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// SQL() function tests
	[Test]
	[Arguments("sql(SELECT * FROM test)", "#-1 SQL NOT CONFIGURED")]
	public async Task Test_Sql_NotConfigured(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sql(test_query_sql_case1)", "#-1 SQL NOT CONFIGURED")]
	public async Task Test_Sql_SimpleQuery(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// SQLESCAPE() function tests
	[Test]
	[Arguments("sqlescape(test_string_sqlescape_case1)", "test_string_sqlescape_case1")]
	public async Task Test_Sqlescape_NoQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(test'string)", "test''string")]
	public async Task Test_Sqlescape_SingleQuote(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(You don't say)", "You don''t say")]
	public async Task Test_Sqlescape_MultipleQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(test''double)", "test''''double")]
	public async Task Test_Sqlescape_DoubleQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqlescape(It's a test's test)", "It''s a test''s test")]
	public async Task Test_Sqlescape_ManyQuotes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// MAPSQL() function tests
	[Test]
	[Arguments("mapsql(obj/attr,SELECT * FROM test)", "#-1 SQL NOT CONFIGURED")]
	public async Task Test_Mapsql_NotConfigured(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mapsql(me/test_attr_mapsql_case1,test_query_mapsql_case2)", "#-1 SQL NOT CONFIGURED")]
	public async Task Test_Mapsql_WithAttribute(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
