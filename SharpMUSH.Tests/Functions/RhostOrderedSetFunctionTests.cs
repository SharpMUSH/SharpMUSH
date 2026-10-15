using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's order-keeping set operations, <c>listdiff()</c>, <c>listinter()</c> and
/// <c>listunion()</c>, and <c>elementpos()</c>. The expected results are what an unmodified RhostMUSH
/// server answers for the same input.
/// </summary>
public class RhostOrderedSetFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code) =>
		(await Parser.FunctionParse(MarkupText.Plain(code)))?.Message?.ToPlainText() ?? string.Empty;

	[Test]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep)", "foo baz")]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep,,-)", "foo-baz")]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep,,-,1)", "foo-baz-foo-foo")]
	[Arguments("listdiff(a|b|c,b,|)", "a|c")]
	[Arguments("listdiff(A b,a)", "A b")]
	[Arguments("listdiff(a  b,x)", "a b")]
	[Arguments("listdiff(a||b,x,|)", "a|b")]
	[Arguments("listdiff(a||b,x,|,|,1)", "a|b")]
	[Arguments("[listdiff(,a)]|[listdiff(a,)]", "|a")]
	public async Task ListDiff(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep)", "gleep bar")]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep,,-)", "gleep-bar")]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep,,-,1)", "gleep-bar-bar")]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep,,,1)", "gleep bar bar")]
	[Arguments("[listinter(,a)]|", "|")]
	public async Task ListInter(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep)", "foo baz gleep bar moof")]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep,,-)", "foo-baz-gleep-bar-moof")]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep,,-,1)", "foo-baz-gleep-bar-foo-bar-moof")]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep moof,,-,1)", "foo-baz-gleep-bar-foo-bar-moof")]
	[Arguments("listunion(a a,b b)", "a b")]
	[Arguments("listunion(a a,b b,,,1)", "a a b")]
	[Arguments("listunion(a b,c,,-,2)", "a-b-c")]
	[Arguments("listunion(a b,c,,-,x)", "a-b-c")]
	[Arguments("listunion(a||b,|x,|,|,1)", "a|b|x")]
	[Arguments("[listunion(,a)]|[listunion(a,)]", "a|a")]
	public async Task ListUnion(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("elementpos(abcdefg,1 4)", "a d")]
	[Arguments("elementpos(abcdefg,1 4 7,)", "adg")]
	[Arguments("elementpos(abcdefg,4 1 4 0 8 -1)", "d a d")]
	[Arguments("elementpos(abcdefg,2 3,--)", "b--c")]
	[Arguments("elementpos(abc,a 2)", "b")]
	[Arguments("elementpos(abc,2  3)", "b c")]
	[Arguments("elementpos(abc,2|3)", "b")]
	[Arguments("elementpos(abc,1.5 3)", "a c")]
	[Arguments("[elementpos(abcdefg,)]|[elementpos(,1)]|", "||")]
	public async Task ElementPos(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>The examples in the help for listdiff() and elementpos(), as written there.</summary>
	[Test]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep)", "foo baz")]
	[Arguments("listdiff(foo baz gleep bar foo foo, bar moof gleep, , -, 1)", "foo-baz-foo-foo")]
	[Arguments("listinter(foo baz gleep bar foo bar, bar moof gleep)", "gleep bar")]
	[Arguments("listunion(foo baz gleep bar foo bar, bar moof gleep)", "foo baz gleep bar moof")]
	[Arguments("elementpos(abcdefg, 1 4)", "a d")]
	[Arguments("elementpos(abcdefg, 1 4 7, )", "adg")]
	[Arguments("elementpos(abcdefg, 4 1 4 9)", "d a d")]
	public async Task HelpExamples(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>A character keeps its color.</summary>
	[Test]
	public async Task ElementPos_KeepsMarkup()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("elementpos(a[ansi(r,b)]c,2)"));

		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("b");
		await Assert.That(result.Message!.Render(MarkupFormat.Ansi)).IsNotEqualTo("b");
	}
}
