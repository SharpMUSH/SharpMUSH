using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LambdaUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments(@"ulambda(#lambda/add\(1\,2\))", "3")]
	// Without paren depth tracking (PennMUSH-compatible): bare ( in #lambda/add( is just text,
	// and the first ) closes lit() instead of matching the bare (. Use escaped parens or brackets instead.
	// With strict parsing: the unbalanced body "add(1,2" is now a PARSER FAILURE (previously ANTLR recovered silently).
	[Arguments("ulambda(lit(#lambda/add(1,2)))", "#-1 PARSER FAILURE: Expected ) or , at end of expression)")]
	[Arguments("ulambda(#lambda/[add(1,2)])", "3")]
	[Arguments("ulambda(#lambda/3)", "3")]
	// Extra trailing parens: after the function closes, remaining ) become generic text.
	// Same PARSER FAILURE from the unbalanced lambda body, with additional trailing ) literal text.
	[Arguments("ulambda(lit(#lambda/add(1,2))))", "#-1 PARSER FAILURE: Expected ) or , at end of expression))")]
	[Arguments("ulambda(lit(#lambda/add(1,2)))))", "#-1 PARSER FAILURE: Expected ) or , at end of expression)))")]
	public async Task BasicLambdaTest(string call, string expected)
	{
		var res = (await Parser.FunctionParse(MModule.single(call)))!.Message!;
		await Assert.That(res.ToPlainText()).IsEqualTo(expected);
	}
}