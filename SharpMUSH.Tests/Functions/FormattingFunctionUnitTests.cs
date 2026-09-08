using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class FormattingFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("render(test %r newline)", "")]
	public async Task Render(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("tag(b,text)", "#-1 USE TAGWRAP INSTEAD")]
	public async Task Tag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("tagwrap(b,text)", "<b>text</b>")]
	// help tagwrap: tagwrap(<name>[, <parameters>], <string>) — the wrapped string is LAST.
	[Arguments("tagwrap(a,href=\"https://sharpmush.com\",SharpMUSH)", "<a href=\"https://sharpmush.com\">SharpMUSH</a>")]
	[Arguments("tagwrap(a,xch_cmd=\"+help scene\",Read it)", "<a xch_cmd=\"+help scene\">Read it</a>")]
	public async Task Tagwrap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("endtag(b)", "#-1 USE TAGWRAP INSTEAD")]
	public async Task Endtag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("wrap(test,5)", "test")]
	// Word wrap, not a chop at the width: the break lands on a space.
	[Arguments("wrap(This is a test of the wrap function,10)", "This is a\ntest of\nthe wrap\nfunction")]
	// A narrower first line, for softcode that puts a prefix in front of it.
	[Arguments("wrap(This is a test of the wrap function,10,5)", "This\nis a test\nof the\nwrap\nfunction")]
	[Arguments("wrap(one two three,10,10,|)", "one two|three")]
	// A word longer than the column has to break somewhere.
	[Arguments("wrap(aa abcdefghijkl bb,6)", "aa\nabcdef\nghijkl\nbb")]
	// Widths are display cells, so a two-cell character takes two of them.
	[Arguments("wrap(日本語です,4)", "日本\n語で\nす")]
	public async Task Wrap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	// 66 UTF-16 code units, 11 display columns. strlen answers the question softcode is actually
	// asking, which is how much room the string takes up.
	[Arguments("strlen(T͆́͂ͯe͕͓ͨx̼̀ͣt̜̭̪͒̉͗ͦ͂ ͕̈E͈̬̮̥͒ͣd͚͖̭͚ͩ̃͌i̺͑ͬ͊ͯt̞͔̂̏͒ͨo̥͓ͤͤ͗r̮͖̼͙͐)", "11")]
	[Arguments("strlen(日本語)", "6")]
	[Arguments("strlen(abc)", "3")]
	public async Task StringLengthMeasuresColumns(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	// align(1,<a two-cell character>) used to never return: the column was narrower than one
	// grapheme cluster, so the wrap made no progress.
	[Arguments("align(1,日本語)", true)]
	public async Task AlignTerminatesOnAColumnNarrowerThanItsText(string str, bool _)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("ljust(test,10)", "test      ")]
	// Penn ljust.1-ljust.5
	[Arguments("ljust(foo,3)", "foo")]
	[Arguments("ljust(foo,5,=)", "foo==")]
	[Arguments("ljust(foo,2)", "foo")]
	[Arguments("ljust(foo,-3)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task Ljust(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("rjust(test,10)", "      test")]
	// Penn rjust.1-rjust.5
	[Arguments("rjust(foo,3)", "foo")]
	[Arguments("rjust(foo,5)", "  foo")]
	[Arguments("rjust(foo,5,=)", "==foo")]
	[Arguments("rjust(foo,2)", "foo")]
	[Arguments("rjust(foo,-3)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task Rjust(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("center(test,10)", "   test   ")]
	// Penn center.1-center.7
	[Arguments("center(foo,3)", "foo")]
	[Arguments("center(foo,5,=)", "=foo=")]
	[Arguments("center(foo,2)", "foo")]
	[Arguments("center(foo,-3)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments("center(foo,5,=,~)", "=foo~")]
	public async Task Center(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("table(a b c,5,20)", "a     b     c    ")]
	// The alignment prefix picks the side the text lands on, not the side the filler does.
	[Arguments("table(a b c,>5,20)", "    a     b     c")]
	[Arguments("table(a b c,-5,20)", "  a     b     c  ")]
	// Fields flow onto a new line once the line length is used up.
	[Arguments("table(a b c d,5,10)", "a     b    \nc     d    ")]
	// A field wider than its column is cut to it.
	[Arguments("table(abcdefg,3,20)", "abc")]
	public async Task Table(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
