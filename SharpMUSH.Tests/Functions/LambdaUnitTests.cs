using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LambdaUnitTests 
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments(@"ulambda(#lambda/add\(1\,2\))", "3")]
	// vv CONSIDER: 3) is not the correct return value. This should just be: 3 vv
	// vv However, this is how our parser should handle this vv
	[Arguments("ulambda(lit(#lambda/add(1,2)))", "3)")] 
	[Arguments("ulambda(#lambda/[add(1,2)])", "3")]
	[Arguments("ulambda(#lambda/3)", "3")]
	public async Task BasicLambdaTest(string call, string expected)
	{
		var res = (await Parser.FunctionParse(MModule.single(call)))!.Message!;
		await Assert.That(res.ToPlainText()).IsEqualTo(expected);
	}
}