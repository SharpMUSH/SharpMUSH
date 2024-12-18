using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;
public class JsonFunctionUnitTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser(
			ns: Substitute.For<INotifyService>()
		);
	}

	[Test]
	[Arguments("json(string,foo)", "\"foo\"")]
	[Arguments("json(number,1.1)", "1.1")]
	[Arguments("json(number,-1)", "-1")]
	// [Arguments("json(object,k,\"v\")", "{\"k\":\"v\"}")] // Does not work yet
	// [Arguments("json(array,1,2)", "[1,2]")] // Does not work yet
	public async Task Json(string function, string expected)
	{
		var result = (await _parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}
}
