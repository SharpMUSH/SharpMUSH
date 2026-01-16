using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class UnusualUnitTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.FunctionParser;

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