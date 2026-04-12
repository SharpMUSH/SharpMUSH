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
	// Penn json.null.1-3
	[Arguments("json(null)", "null")]
	[Arguments("json(null,null)", "null")]
	[Arguments("json(null,foo)", "#-1")]
	// Penn json.boolean.1-5
	[Arguments("json(boolean, true)", "true")]
	[Arguments("json(boolean, 1)", "true")]
	[Arguments("json(boolean, false)", "false")]
	[Arguments("json(boolean, 0)", "false")]
	[Arguments("json(boolean, 5)", "#-1 INVALID VALUE")]
	// Penn json.string.1-4
	[Arguments("json(string, foobar)", "\"foobar\"")]
	[Arguments("json(string, foo bar)", "\"foo bar\"")]
	[Arguments("json(string, foo \\\"bar\\\" baz)", "\"foo \\\"bar\\\" baz\"")]
	// Penn json.number.1-3
	[Arguments("json(number, 5)", "5")]
	[Arguments("json(number, 5.555)", "5.555")]
	[Arguments("json(number, foo)", "#-1 ARGUMENT MUST BE NUMBER")]
	// Penn json.array.1-2
	[Arguments("""json(array, "foo", 5, true)""", """["foo",5,true]""")]
	// Penn json.object.1
	[Arguments("""json(object, foo, 1, bar, "baz", boing, true)""", """{"foo":1,"bar":"baz","boing":true}""")]
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

	// Penn json.type.1-6,8-9 (json_query type)
	[Test]
	[Arguments("""json_query("foo", type)""", "string")]
	[Arguments("json_query(1, type)", "number")]
	[Arguments("json_query(3.14, type)", "number")]
	[Arguments("json_query(null, type)", "null")]
	[Arguments("json_query(true, type)", "boolean")]
	[Arguments("json_query(false, type)", "boolean")]
	[Arguments("json_query(json(array, 1), type)", "array")]
	[Arguments("json_query(foo, type)", "#-1 BAD ARGUMENT FORMAT TO json_query")]
	public async Task JsonQueryType(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn json.size.1-7 (json_query size)
	[Test]
	[Arguments("json_query(null, size)", "0")]
	[Arguments("json_query(true, size)", "1")]
	[Arguments("json_query(1, size)", "1")]
	[Arguments("json_query(1.1, size)", "1")]
	[Arguments("""json_query("foo", size)""", "1")]
	[Arguments(@"json_query(\[\], size)", "0")]
	[Arguments("json_query(json(array, 1, 2), size)", "2")]
	public async Task JsonQuerySize(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn json.patch.1-5 (json_mod patch)
	[Test]
	[Arguments("""json_mod(json(object,a,1,b,2),patch,json(object,c,3,d,4))""", """{"a":1,"b":2,"c":3,"d":4}""")]
	[Arguments("""json_mod(json(object,a,json(array,1,2),b,2), patch, json(object,a,9))""", """{"a":9,"b":2}""")]
	[Arguments("""json_mod(json(object,a,json(array,1,2),b,2), patch, json(object, a, null))""", """{"b":2}""")]
	[Arguments("""json_mod(json(object,a,1,b,2), patch, json(object,a,9,b,null,c,8))""", """{"a":9,"c":8}""")]
	[Arguments("""json_mod(json(object,a,json(object,x,1,y,2),b,3), patch, json(object,a,json(object,y,9),c,8))""", """{"a":{"x":1,"y":9},"b":3,"c":8}""")]
	public async Task JsonModPatch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn json.sort.1-4 (json_mod sort)
	[Test]
	[Arguments("json_mod(json(array, json(object, id, 5), json(object, id, 4)), sort, $.id)", """[{"id":4},{"id":5}]""")]
	[Arguments("""json_mod(json(array, json(object, id, "dog"), json(object, id, "cat")), sort, $.id)""", """[{"id":"cat"},{"id":"dog"}]""")]
	[Arguments("json_mod(json(array, 5, 3, 1, 2), sort, $)", "[1,2,3,5]")]
	[Arguments("""json_mod(json(array, "e","m","a","z"), sort, $)""", """["a","e","m","z"]""")]
	public async Task JsonModSort(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn json.size.8-9 (empty object, object with 3 keys)
	[Test]
	[Arguments(@"json_query(\{\}, size)", "0")]
	public async Task JsonQuerySizeObject(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn json.array.2, json.object.2 (nested structures)
	[Test]
	[Arguments("""json(array, "foo", 5, json(array, "bar", 10))""", """["foo",5,["bar",10]]""")]
	[Arguments("""json(object, foo, 1, bar, "baz", boing, json(array, "nested", "test", 1))""", """{"foo":1,"bar":"baz","boing":["nested","test",1]}""")]
	public async Task JsonNested(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	// Penn json.string.4: backslash escaping
	[Test]
	[Arguments(@"json(string, foo\\bar\\baz)", @"""foo\\bar\\baz""")]
	public async Task JsonStringBackslash(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	// Tests for json_query with a JSON object {"a":1,"b":2,"c":[1,2,3]}
	// (mirrors Penn testjson.t v(json) tests)
	[Test]
	public async Task JsonQueryWithJsonObject()
	{
		// json.type.7: object type
		var typeResult = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), type)")))?.Message!;
		await Assert.That(typeResult.ToPlainText()).IsEqualTo("object");

		// json.size.9: 3 keys
		var sizeResult = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), size)")))?.Message!;
		await Assert.That(sizeResult.ToPlainText()).IsEqualTo("3");

		// json.exists.1: key 'a' exists → 1
		var exists1 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), exists, a)")))?.Message!;
		await Assert.That(exists1.ToPlainText()).IsEqualTo("1");

		// json.exists.2: key 'd' not found → 0
		var exists2 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), exists, d)")))?.Message!;
		await Assert.That(exists2.ToPlainText()).IsEqualTo("0");

		// json.exists.3: c[1] exists → 1
		var exists3 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), exists, c, 1)")))?.Message!;
		await Assert.That(exists3.ToPlainText()).IsEqualTo("1");

		// json.exists.4: c[3] out-of-bounds → 0
		var exists4 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), exists, c, 3)")))?.Message!;
		await Assert.That(exists4.ToPlainText()).IsEqualTo("0");

		// json.get.1: get 'a' → 1
		var get1 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), get, a)")))?.Message!;
		await Assert.That(get1.ToPlainText()).IsEqualTo("1");

		// json.get.2: get 'd' (missing) → empty
		var get2 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), get, d)")))?.Message!;
		await Assert.That(get2.ToPlainText()).IsEqualTo(string.Empty);

		// json.get.3: get c[1] → 2
		var get3 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), get, c, 1)")))?.Message!;
		await Assert.That(get3.ToPlainText()).IsEqualTo("2");

		// json.get.4: get c[3] (out-of-bounds) → empty
		var get4 = (await Parser.FunctionParse(MModule.single("json_query(json(object,a,1,b,2,c,json(array,1,2,3)), get, c, 3)")))?.Message!;
		await Assert.That(get4.ToPlainText()).IsEqualTo(string.Empty);

		// json.extract.1: $.a → 1
		var ext1 = (await Parser.FunctionParse(MModule.single(@"json_query(json(object,a,1,b,2,c,json(array,1,2,3)), extract, $.a)")))?.Message!;
		await Assert.That(ext1.ToPlainText()).IsEqualTo("1");

		// json.extract.2: $.d (missing) → empty
		var ext2 = (await Parser.FunctionParse(MModule.single(@"json_query(json(object,a,1,b,2,c,json(array,1,2,3)), extract, $.d)")))?.Message!;
		await Assert.That(ext2.ToPlainText()).IsEqualTo(string.Empty);

		// json.extract.3: $.c[1] → 2 (must escape brackets in MUSH)
		var ext3 = (await Parser.FunctionParse(MModule.single(@"json_query(json(object,a,1,b,2,c,json(array,1,2,3)), extract, $.c\[1\])")))?.Message!;
		await Assert.That(ext3.ToPlainText()).IsEqualTo("2");

		// json.extract.4: $.c[3] (out-of-bounds) → empty
		var ext4 = (await Parser.FunctionParse(MModule.single(@"json_query(json(object,a,1,b,2,c,json(array,1,2,3)), extract, $.c\[3\])")))?.Message!;
		await Assert.That(ext4.ToPlainText()).IsEqualTo(string.Empty);
	}

	// Penn json.exists.5-6, json.get.5-6 (scalar JSON or invalid JSON with path)
	[Test]
	[Arguments("""json_query("foo", exists, a)""", "#-1 BAD ARGUMENT FORMAT TO json_query")]
	[Arguments("json_query(foo, exists, a)", "#-1 BAD ARGUMENT FORMAT TO json_query")]
	[Arguments("""json_query("foo", get, a)""", "#-1 BAD ARGUMENT FORMAT TO json_query")]
	[Arguments("json_query(foo, get, a)", "#-1 BAD ARGUMENT FORMAT TO json_query")]
	[Arguments("json_query(foo, extract, $.a)", "#-1 BAD ARGUMENT FORMAT TO json_query")]
	public async Task JsonQueryScalarErrors(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// json.extract.5: string JSON, path "$" → its value (unquoted)
	[Test]
	[Arguments("""json_query("foo", extract, $)""", "foo")]
	public async Task JsonExtractRoot(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Tests for json_mod set/insert/replace/remove on {"a":1,"b":2,"c":[1,2,3]}
	// (mirrors Penn testjson.t json.set/insert/replace/remove tests)
	[Test]
	public async Task JsonModOperations()
	{
		// json.set.1: set $.c to 3 (replaces existing) → result contains "c":3
		var set1 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), set, $.c, 3)")))?.Message!;
		await Assert.That(set1.ToPlainText()).Contains("\"c\":3");

		// json.set.2: set $.d to 3 (new key) → result contains "d":3
		var set2 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), set, $.d, 3)")))?.Message!;
		await Assert.That(set2.ToPlainText()).Contains("\"d\":3");

		// json.insert.1: insert $.b = 3 (key exists, unchanged) → result contains "b":2
		var ins1 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), insert, $.b, 3)")))?.Message!;
		await Assert.That(ins1.ToPlainText()).Contains("\"b\":2");

		// json.insert.2: insert $.d = 3 (new key) → result contains "d":3
		var ins2 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), insert, $.d, 3)")))?.Message!;
		await Assert.That(ins2.ToPlainText()).Contains("\"d\":3");

		// json.replace.1: replace $.b = 3 (key exists) → result contains "b":3
		var rep1 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), replace, $.b, 3)")))?.Message!;
		await Assert.That(rep1.ToPlainText()).Contains("\"b\":3");

		// json.replace.2: replace $.d = 3 (key absent) → "d":3 should NOT appear
		var rep2 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), replace, $.d, 3)")))?.Message!;
		await Assert.That(rep2.ToPlainText()).DoesNotContain("\"d\":3");

		// json.remove.1: remove $.c → result is {"a":1,"b":2}
		var rem1 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), remove, $.c)")))?.Message!;
		await Assert.That(rem1.ToPlainText()).IsEqualTo(@"{""a"":1,""b"":2}");

		// json.remove.2: remove $.d (absent) → result unchanged {"a":1,"b":2,"c":[1,2,3]}
		var rem2 = (await Parser.FunctionParse(MModule.single(@"json_mod(json(object,a,1,b,2,c,json(array,1,2,3)), remove, $.d)")))?.Message!;
		await Assert.That(rem2.ToPlainText()).IsEqualTo(@"{""a"":1,""b"":2,""c"":[1,2,3]}");
	}

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
	[Category("NeedsSetup")]
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
