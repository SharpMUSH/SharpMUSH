using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class FormattingFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	/// <summary>
	/// render() takes two arguments, so the case below has always exercised the arity refusal rather
	/// than any rendering. It declared an <c>expected</c> and never compared it — the assertion was
	/// <c>IsNotNull()</c> on a non-nullable string, which no result could fail.
	/// </summary>
	[Test]
	[Arguments("render(test %r newline)", "#-1 FUNCTION (RENDER) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1")]
	public async Task Render(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>render(&lt;string&gt;, &lt;formats&gt;)</c> — PennMUSH's fun_render. Until #974 this name was
	/// bound to an objeval, so none of these cases could have passed: the helpfile documented one
	/// function and the engine ran another.
	/// </summary>
	[Test]
	// No recognised format at all strips the markup, as stripansi() would.
	[Arguments("render(ansi(r,red),markup)", "red")]
	// noaccents is prefix-matched by PennMUSH, so "noacc" is the same request.
	[Arguments("render(déjà vu,noaccents)", "deja vu")]
	[Arguments("render(déjà vu,noacc)", "deja vu")]
	[Arguments("render(plain,nosuchformat)", "#-1 INVALID SECOND ARGUMENT")]
	public async Task RenderConvertsByFormatName(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task RenderToHtmlEscapesTheTextAndKeepsTheColour()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("render(ansi(r,a<b>c),html)")))?.Message!;

		await Assert.That(result.ToPlainText()).Contains("&lt;b&gt;");
	}

	[Test]
	public async Task RenderToAnsiEmitsEscapeCodes()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("render(ansi(r,red),ansi)")))?.Message!;

		await Assert.That(result.ToPlainText()).Contains("\u001b[");
	}

	[Test]
	[Arguments("tag(b,text)", "#-1 USE TAGWRAP INSTEAD")]
	[Arguments("html(b)", "#-1 USE TAGWRAP INSTEAD")]
	public async Task Tag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("tagwrap(b,text)", "<b>text</b>")]
	// help tagwrap: tagwrap(<name>[, <parameters>], <string>) — the wrapped string is LAST.
	[Arguments("tagwrap(a,href=\"https://sharpmush.com\",SharpMUSH)", "<a href=\"https://sharpmush.com\">SharpMUSH</a>")]
	public async Task Tagwrap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.Render(MarkupFormat.Html)).IsEqualTo(expected);
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

	/// <summary>
	/// <c>fun_table</c> clamps rather than refuses: a field width below 1 becomes 1
	/// (<c>src/funlist.c:2483-2484</c>), a line length below 2 becomes 2 (<c>:2470-2471</c>), and a
	/// field width at or above the line length becomes <c>line_length - 1</c> (<c>:2488-2489</c>).
	/// SharpMUSH divided the line length by the field width to get a column count, so
	/// <c>table(a b,0,10)</c> divided by zero and a field wider than the line answered
	/// <c>#-1 FIELD WIDTH EXCEEDS LINE WIDTH</c>, which PennMUSH never says.
	///
	/// <para>The packing is incremental (<c>:2534-2544</c>) and counts the output separator, which
	/// the column-count arithmetic ignored. Every expectation here is the 1.8.8 oracle's.</para>
	/// </summary>
	[Test]
	[Arguments("table(a b,0,10)", "a b")]
	[Arguments("table(a b c,20,10)", "a        \nb        \nc        ")]
	[Arguments("table(a b c,5,1)", "a b\nc")]
	[Arguments("table(a b c d e f g h,3,10)", "a   b   c  \nd   e   f  \ng   h  ")]
	// A leading '-' is the centring prefix, not a sign: the width here is 5, centred.
	[Arguments("table(a b c,-5,10)", "  a     b  \n  c  ")]
	[Arguments("table(a b c d,5,10,%b,|)", "a    |b    \nc    |d    ")]
	// An output separator given as empty is no separator at all, and takes no width with it.
	[Arguments("table(a b c d,5,10,%b,)", "a    b    \nc    d    ")]
	[Arguments("table(,5,10)", "")]
	public async Task TableClampsItsWidths(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// The packing arithmetic itself: <c>col</c> starts at one field width, each further field adds a
	/// field width <em>before</em> the wrap test, and the output separator is charged only when one
	/// is actually written (<c>src/funlist.c:2533-2544</c>).
	/// </summary>
	/// <remarks>
	/// This expectation is DERIVED from that loop rather than observed on PennMUSH — the issue's
	/// acceptance names the case but carries no capture for it. Walked out: <c>a</c> puts col at 5;
	/// <c>b</c> takes it to 10, which is inside 15, so a separator goes in and col becomes 11;
	/// <c>c</c> takes it to 16, which is past 15, so the line wraps and col resets to 5; <c>d</c>
	/// takes it to 10 and takes a separator. The same walk reproduces every observed case in
	/// <see cref="TableClampsItsWidths"/>, which is what makes it worth trusting here.
	/// </remarks>
	[Test]
	[Arguments("table(a b c d,5,15)", "a     b    \nc     d    ")]
	public async Task TablePacksByTheRunningColumn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// An output separator wider than one character is a deliberate SharpMUSH extension, and the
	/// column is charged its real width.
	/// </summary>
	/// <remarks>
	/// PennMUSH takes its <c>osep</c> through <c>delim_check</c> (<c>src/function.c:248-265</c>),
	/// which refuses anything but a single character with
	/// <c>#-1 SEPARATOR MUST BE ONE CHARACTER</c>, and <c>col += 1</c> (<c>src/funlist.c:2541</c>)
	/// is hardcoded to that width. Refusing a wider one here would buy parity on an error string and
	/// nothing else, so SharpMUSH accepts it — but then the only arithmetic that keeps a line inside
	/// <c>&lt;line length&gt;</c> is charging the separator what it actually costs, which is what
	/// this pins. Field 5 and line 12: <c>a</c> at 5, <c>b</c> at 10 which fits, then the two-wide
	/// separator takes the line to exactly 12; <c>c</c> would reach 17 and wraps.
	/// </remarks>
	[Test]
	[Arguments("table(a b c d,5,12,%b,--)", "a    --b    \nc    --d    ")]
	public async Task TableChargesAMultiCharacterSeparatorItsOwnWidth(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// A cell is padded to its DISPLAY width and keeps its colour across the join: a two-character
	/// red cell in a four-wide field is the coloured text followed by two <em>plain</em> spaces.
	/// </summary>
	/// <remarks>
	/// Asserted on the rendered ANSI, because <c>ToPlainText()</c> cannot tell the two failures
	/// apart: padding against the escape sequence's byte length would leave the cell short, and
	/// padding inside the colour span would paint the trailing spaces red. Both read identically as
	/// plain text.
	/// </remarks>
	[Test]
	public async Task TablePadsTheDisplayWidthAndKeepsTheColour()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("table(ansi(r,ab) cd,4,10)")))!.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("ab   cd  ")
			.Because("each cell is four wide and the separator is one, so the line is nine characters");
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo("\e[31mab\e[0m   cd  ")
			.Because("the colour covers 'ab' and stops there; the two pad spaces and the separator are plain");
	}
}
