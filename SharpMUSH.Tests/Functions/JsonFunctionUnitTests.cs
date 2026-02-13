using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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

	[Test, Skip("json_map currently explicitly does not use #lambda. It should evaluate functions later in its loop instead and use the existing method of calling attributes.")]
	[Arguments(@"json_map(#lambda/toupper\(%%1\,%%2\),json(object,a,1,b,2))", "A:1 B:2")]
	public async Task JsonMap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Note: json_map and oob tests require more complex setup with attributes and connections
	// These are placeholder tests that should be expanded once the test infrastructure supports them
	
	[Test]
	[Arguments("&Test_JsonMap_MapsOverJsonElements_1 me=%0:%1", @"json_map(me/Test_JsonMap_MapsOverJsonElements_1,lit(""test_json_map_string""))", "string:\"test_json_map_string\"")]
	[Arguments("&Test_JsonMap_MapsOverJsonElements_2 me=%0:%1:%2", @"json_map(me/Test_JsonMap_MapsOverJsonElements_2,\[1\,2\,3\])", "number:1:0 number:2:1 number:3:2")]
	public async Task Test_JsonMap_MapsOverJsonElements(string setup, string function, string expected)
	{
		// Setup: set attribute
		// TODO: Implement attribute setting in test infrastructure
		await Parser.CommandParse(1, ConnectionService, MModule.single(setup));
		
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
	
	[Test]
	[Skip("Requires connection setup")]
	[Arguments("oob(me,Package.Name,{\"key\":\"test_oob_case1\"})", "1")]
	[Arguments("oob(me,Package.Name)", "1")]
	public async Task Test_Oob_SendsGmcpMessages(string function, string expected)
	{
		// This test requires proper GMCP connection setup
		// TODO: Implement connection mocking in test infrastructure
		
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}
}
