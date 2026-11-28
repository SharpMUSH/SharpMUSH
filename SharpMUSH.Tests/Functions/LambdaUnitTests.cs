using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LambdaUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments(@"ulambda(#lambda/add\(1\,2\))", "3")]
	// With paren_groups off (the default), a bare ( in #lambda/add( is just text, and the first )
	// closes lit() instead of matching it. Use escaped parens or brackets instead. PennMUSH, and
	// paren_groups on, give 3 here: see ParenthesisGroupTests.
	// With strict parsing: the unbalanced body "add(1,2" is now a PARSER FAILURE (previously ANTLR recovered silently).
	[Arguments("ulambda(lit(#lambda/add(1,2)))", "#-1 PARSER FAILURE: Expected ) or , at end of expression)")]
	[Arguments("ulambda(#lambda/[add(1,2)])", "3")]
	[Arguments("ulambda(#lambda/3)", "3")]
	// Extra trailing parens: after the function closes, remaining ) become generic text.
	// Same PARSER FAILURE from the unbalanced lambda body, with additional trailing ) literal text.
	[Arguments("ulambda(lit(#lambda/add(1,2))))", "#-1 PARSER FAILURE: Expected ) or , at end of expression))")]
	[Arguments("ulambda(lit(#lambda/add(1,2)))))", "#-1 PARSER FAILURE: Expected ) or , at end of expression)))")]
	// The help's forms for code that calls functions, with paren_groups off.
	[Arguments(@"map(#lambda/ucstr\(\%0\),a b)", "A B")]
	[Arguments(@"map(#lambda/cat\([ucstr(x)]\,\%0\),a b)", "X a X b")]
	[Arguments("filter(lit(#lambda/[strmatch(%0,b*)]),a b bc)", "b bc")]
	[Arguments("map(#lambda/name(\\%0),a b)", "#-1 FUNCTION (MAP) EXPECTS BETWEEN 2 AND 35 ARGUMENTS BUT GOT 1,a b)")]
	public async Task BasicLambdaTest(string call, string expected)
	{
		var res = await Parser.EvaluateAsync(MarkupText.Plain(call));
		await Assert.That(res.ToPlainText()).IsEqualTo(expected);
	}
}