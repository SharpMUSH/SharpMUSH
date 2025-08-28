using SharpMUSH.Library.ParserInterfaces;
namespace SharpMUSH.Tests.Functions;

public class AttributeFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/attribute,ZAP!)][get(%!/attribute)]", "ZAP!")]
	[Arguments("[attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)]", "\e[1;31mZAP!\e[0m")]
	[Arguments("[attrib_set(%!/attribute,ansi(hr,ZIP!))][get(%!/attribute)][attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)]", "\e[1;31mZIP!\e[0m\e[1;31mZAP!\e[0m")]
	public async Task SetAndGet(string input, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}
}
