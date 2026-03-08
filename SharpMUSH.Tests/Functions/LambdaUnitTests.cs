using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LambdaUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments(@"ulambda(#lambda/add\(1\,2\))", "3")]
	[Arguments("ulambda(lit(#lambda/add(1,2)))", "3")]
	[Arguments("ulambda(#lambda/[add(1,2)])", "3")]
	[Arguments("ulambda(#lambda/3)", "3")]
	// Extra trailing parens: after the function closes, remaining ) become generic text
	[Arguments("ulambda(lit(#lambda/add(1,2))))", "3)")]
	[Arguments("ulambda(lit(#lambda/add(1,2)))))", "3))")]
	public async Task BasicLambdaTest(string call, string expected)
	{
		var res = (await Parser.FunctionParse(MModule.single(call)))!.Message!;
		await Assert.That(res.ToPlainText()).IsEqualTo(expected);
	}
}