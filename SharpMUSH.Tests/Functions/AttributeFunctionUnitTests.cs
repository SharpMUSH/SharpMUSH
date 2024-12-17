using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;
public class AttributeFunctionUnitTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser();
	}

	[Test]
	[Arguments("[attrib_set(%!/attribute,ZAP!)][get(%!/attribute)]", "ZAP!")]
	[Arguments("[attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)]", "\e[1;31mZAP!\e[0m")]
	public async Task SetAndGet(string input, string expected)
	{
		var result = (await _parser!.FunctionParse(MModule.single(input)))?.Message?.ToString()!;
		await Assert.That(result).IsEqualTo(expected);
	}
}
