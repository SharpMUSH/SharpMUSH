using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class UnusualUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments(@"s(ansi\(rG\,ansi(D\,[ansi(y,foo)]\)\))", "foo")]
	public async Task S(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message;

		Console.WriteLine("Result: {0}", result);
		await Assert.That(result!.ToPlainText()).IsEqualTo(expected);
	}
}