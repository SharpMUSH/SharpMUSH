using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class LiteralBraceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("DUAL RIPOSTE SWORDS PARRY", true)]
	[Arguments("a,b(c)d{e}f;g=h>i", true)]
	[Arguments("x y\nz", true)]
	[Arguments("a  b", false)]
	[Arguments(" a", false)]
	[Arguments("a ", false)]
	[Arguments("a, b", false)]
	[Arguments("f( a)", false)]
	[Arguments("{ a}", false)]
	[Arguments("a\t b", false)]
	[Arguments("%#", false)]
	[Arguments("[add(1,2)]", false)]
	[Arguments("a]b", false)]
	[Arguments("\\a", false)]
	[Arguments("$0", false)]
	[Arguments("##", false)]
	[Arguments("", false)]
	public async Task OnlyTextEvaluationLeavesAloneIsLiteral(string text, bool literal)
		=> await Assert.That(SharpMUSHParserVisitor.IsLiteralBraceText(text)).IsEqualTo(literal);

	[Test]
	[Arguments("strlen({DUAL RIPOSTE SWORDS PARRY})", "25")]
	[Arguments("strcat(x,{a,b(c)d{e}f;g=h},y)", "xa,b(c)d{e}f;g=hy")]
	[Arguments("notafunction({a,add(1,2)})", "notafunction(a,add(1,2))")]
	[Arguments("iter(1 2,{a(b)})", "a(b) a(b)")]
	[Arguments("strcat({a b}{c d})", "a bc d")]
	[Arguments("strcat({a  b})", "a b")]
	[Arguments("strcat({[add(1,2)] x})", "3 x")]
	public async Task LiteralBracesEvaluateAsTheVisitorWould(string expression, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(expression));

		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
