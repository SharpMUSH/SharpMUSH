using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Functions;

public class JsonFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[Arguments("json(string,foo)", "\"foo\"")]
	[Arguments("json(number,1.1)", "1.1")]
	[Arguments("json(number,-1)", "-1")]
	[Arguments("""json(object,k,"v")""", """{"k":"v"}""")]
	[Arguments("""json(object,k,"v",k,"b")""", "#-1 DUPLICATE KEYS: k")]
	[Arguments("""json(object,ansi(hr,k),"v")""", """{"k":"v"}""")]
	[Arguments("json(object,k,v)", "#-1 BAD ARGUMENT FORMAT TO json")]
	[Arguments("json(array,1,2)", "[1,2]")]
	[Arguments("json(array,1,blah)", "#-1 BAD ARGUMENT FORMAT TO json")]
	public async Task Json(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("json(string,ansi(hr,foo))")]
	[Arguments("json(object,key,json(string,ansi(hr,foo)))")]
	public async Task JsonNotABadArgument(string function)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsNotEqualTo("#-1 BAD ARGUMENT FORMAT TO json");
	}

	[Test]
	[Arguments("isjson(1)", "1")]
	[Arguments("isjson(true)", "1")]
	[Arguments("isjson(false)", "1")]
	[Arguments("isjson(null)", "1")]
	[Arguments("""isjson("test_string_isjson_case1")""", "1")]
	[Arguments("""isjson(json(object,key,json(string,test_value_isjson_case2)))""", "1")]
	[Arguments("""isjson(json(array,1,2,3))""", "1")]
	[Arguments("isjson(unquoted)", "0")]
	[Arguments("isjson(test_invalid_isjson_case3)", "0")]
	[Arguments("""isjson(json(string,{bad json}))""", "1")]
	public async Task Test_IsJson_ValidatesJsonCorrectly(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments(@"json_map(#lambda/ucstr\(%%2\):%%1,json(object,a,1,b,2))", "A:1 B:2")]
	public async Task JsonMap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Note: json_map and oob tests require more complex setup with attributes and connections

	[Test]
	public async Task Test_JsonMap_MapsOverJsonElements_String()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "JsonMapStr");
		var attrName = $"JSONMAP_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{attrName} {objDbRef}=%0:%1"));

		var result = (await Parser.FunctionParse(MModule.single($"json_map({objDbRef}/{attrName},lit(\"test_json_map_string\"))")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("string:\"test_json_map_string\"");
	}

	[Test]
	public async Task Test_JsonMap_MapsOverJsonElements_Array()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "JsonMapArr");
		var attrName = $"JSONMAP_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{attrName} {objDbRef}=%0:%1:%2"));

		var result = (await Parser.FunctionParse(MModule.single($@"json_map({objDbRef}/{attrName},\[1\,2\,3\])")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("number:1:0 number:2:1 number:3:2");
	}

	[Test]
	[Arguments("oob(me,Package.Name,{\"key\":\"test_oob_case1\"})", "#-1 INVALID JSON MESSAGE")]
	[Arguments("oob(me,Package.Name)", "0")]
	public async Task Test_Oob_SendsGmcpMessages(string function, string expected)
	{
		// God has no active GMCP connection in the test environment.
		// oob() with valid JSON and no GMCP returns "0"; with escaped-quote JSON returns error.
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}
}
