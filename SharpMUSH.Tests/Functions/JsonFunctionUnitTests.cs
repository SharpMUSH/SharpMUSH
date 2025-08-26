using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;
public class JsonFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("json(string,foo)", "\"foo\"")]
	[Arguments("json(number,1.1)", "1.1")]
	[Arguments("json(number,-1)", "-1")]
	[Arguments("json(object,k,\"v\")", "{\"k\":\"v\"}")]
	[Arguments("json(object,k,\"v\",k,\"b\")", "#-1 DUPLICATE KEYS: k")]
	[Arguments("json(object,ansi(hr,k),\"v\")", "{\"k\":\"v\"}")]
	[Arguments("json(object,k,v)", "#-1 BAD ARGUMENT FORMAT TO json")] 
	[Arguments("json(array,1,2)", "[1,2]")] 
	[Arguments("json(array,1,blah)", "#-1 BAD ARGUMENT FORMAT TO json")]
	public async Task Json(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("json(string,ansi(hr,foo))")]
	[Arguments("json(object,key,json(string,ansi(hr,foo)))")]
	public async Task JsonNotABadArgument(string function)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsNotEqualTo("#-1 BAD ARGUMENT FORMAT TO json");
	}
}
