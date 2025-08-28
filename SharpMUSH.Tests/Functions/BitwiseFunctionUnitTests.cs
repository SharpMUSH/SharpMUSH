using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class BitwiseFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("10", "10", "2", "1010")]
	[Arguments("10", "2", "10", "2")]
	[Arguments("woof", "64", "32", "c52gv")]
	[Arguments("oof", "32", "64", "GMP")]
	[Arguments("woof", "1", "10", "#-1 Argument 1 must be between 2 and 64.")]
	[Arguments("woof", "10", "1", "#-1 Argument 2 must be between 2 and 64.")]
	[Arguments("woof", "10", "65", "#-1 Argument 2 must be between 2 and 64.")]
	[Arguments("woof", "65", "10", "#-1 Argument 1 must be between 2 and 64.")]
	public async Task BaseConv(string number, string frombase, string tobase, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single($"baseconv({number},{frombase},{tobase})"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}
}