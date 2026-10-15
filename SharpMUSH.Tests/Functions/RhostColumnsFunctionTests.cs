using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's <c>columns()</c>. The expected results are what an unmodified RhostMUSH server answers
/// for the same input, written with each space as <c>.</c> and each line break as <c>/</c>.
/// </summary>
public class RhostColumnsFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code) =>
		((await Parser.FunctionParse(MarkupText.Plain(code)))?.Message?.ToPlainText() ?? string.Empty)
		.Replace("\r\n", "/").Replace('\n', '/').Replace(' ', '.');

	[Test]
	[Arguments("columns(this is a test of columns,10,3)", "this......a.........of......../is........test......columns...")]
	[Arguments("columns(this is a test of columns,10,3,L,,,,,,1)", "this......is........a........./test......of........columns...")]
	[Arguments("columns(this is a test of columns,10,3,R,1)", "...this......a.....of/.....is...testcolumns")]
	[Arguments("columns(  abcdefgh,3,2)", "abcgh./def")]
	[Arguments("columns(a b c d e f g,2,3,l,,,,|,,0)", "a.|d.|f./b.|e.|g./c.")]
	[Arguments("columns(a b c d e f g h,2,3,l,,,,|,,0)", "a.|d.|g./b.|e.|h./c.|f.")]
	[Arguments("columns(a b c d e f g h,2,3,l,,,,|,,0,,,1)", "a.|d.|g./b.|e.|h./c.|f.|..")]
	[Arguments("columns(a  b%tc,2,5,l,,,,|)", "a.|b.|c.")]
	[Arguments("columns(,3,2)", "")]
	[Arguments("columns(abc,2a,2)", "abc.")]
	public async Task Layout(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("columns(this is a test of columns,3,3,R,0,0)", "thitescol/..s..tumn/.is.of..s/..a")]
	[Arguments("columns(this is a test of columns,3,3,R,0,1)", "thi..a.of/.istescol")]
	[Arguments("columns(abcdefgh ij,3,2,L,1,0)", "abcgh./defij.")]
	[Arguments("columns(abcdefgh ij,3,2,L,1,1)", "abcij.")]
	[Arguments("columns(abcdefg h,3,2,R,1)", "abc..g/def..h")]
	public async Task CropAndCut(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("columns(this is a test of columns,10,3,R,0,0,<*,|,*>,0)", "<*......this|.........a|........of*>/<*........is|......test|...columns*>")]
	[Arguments("columns(this is a test of columns,10,3,R,0,0,<*,|,*>,1)", "<*......this|........is|.........a*>/<*......test|........of|...columns*>")]
	[Arguments("columns(this@is@a@test@of@columns,10,3,R,0,0,<*,|,*>,1,@,-=*=-)", "<*-=*=--this|-=*=--=*is|-=*=--=*=a*>/<*-=*=--test|-=*=--=*of|-=*columns*>")]
	[Arguments("columns(a b c d e,4,3,C,,,<,|,>)", "<.a..|.c..|.e..>/<.b..|.d..>")]
	[Arguments("columns(a b c d e,4,3,C,,,<,|,>,1)", "<.a..|.b..|.c..>/<.d..|.e..>")]
	[Arguments("columns(a b c d e,4,3,L,,,<,|,>,0,,,1)", "<a...|c...|e...>/<b...|d...|....>")]
	[Arguments("columns(a b c d e,4,3,L,,,<,|,>,1,,,1)", "<a...|b...|c...>/<d...|e...|....>")]
	[Arguments("columns(a b c d,4,3,L,,,<,|,>,0)", "<a...|c...|d...>/<b...>")]
	[Arguments("columns(a b c d,4,3,L,,,<,|,>,0,,,1)", "<a...|c...|d...>/<b...|....|....>")]
	[Arguments("columns(a b c d e,2,2,L,,,,,,1,,,1)", "a.b./c.d./e...")]
	[Arguments("columns(ab c,4,2,r,,,<,|,>)", "<..ab|...c>")]
	[Arguments("columns(ab c,4,2,c,,,,,,,,xy)", "xabxxcxy")]
	public async Task BordersAndFiller(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("columns(a b c,3,2,J)", "a..c../b..")]
	[Arguments("columns(ab cd@e,6,2,J,,,,|,,1,@)", "ab..cd|e.....")]
	[Arguments("columns(a b c d@x,10,1,J,,,,,,,@)", "a..b..c..d/x.........")]
	[Arguments("columns(a b c d@x,10,1,J,,,,,,,@,-+)", "a-+b-+c-+d/x-+-+-+-+-")]
	[Arguments("columns(a b c d e f@x,8,1,J,,,,,,,@,-+)", "a-b-c-d-/e-+-+-+f/x-+-+-+-")]
	public async Task Spread(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>Empty items between delimiters, which RhostMUSH runs on into the next item.</summary>
	[Test]
	[Arguments("columns(a@@b@,3,2,L,,,,|,,0,@)", "a..|.../...b..|...")]
	[Arguments("columns(@@a@b@@,3,2,L,,,,|,,1,@)", "......a..|b../......|....../......")]
	public async Task EmptyItems(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("columns(abc,0,2)", "#-1.WIDTH.MUST.BE.>=.1")]
	[Arguments("columns(abc,x,2)", "#-1.WIDTH.MUST.BE.>=.1")]
	[Arguments("columns(abc,99999999,2)", "#-1.WIDTH.ARGUMENT.OUT.OF.BOUNDS")]
	[Arguments("columns(abc,2,0)", "#-1.COLUMNS.ARGUMENT.OUT.OF.BOUNDS")]
	[Arguments("columns(abc,2,99999999)", "#-1.COLUMNS.ARGUMENT.OUT.OF.BOUNDS")]
	[Arguments("columns(abc,2,2,x)", "#-1.INVALID.JUSTIFICATION.SPECIFIED")]
	public async Task Errors(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>The examples in the help for columns(), as written there.</summary>
	[Test]
	[Arguments("columns(this is a test of columns, 10, 3)", "this......a.........of......../is........test......columns...")]
	[Arguments("columns(this is a test of columns, 10, 3, L, , , , , , 1)", "this......is........a........./test......of........columns...")]
	[Arguments("columns(this is a test of columns, 10, 3, R, 0, 0, <*, |, *>)", "<*......this|.........a|........of*>/<*........is|......test|...columns*>")]
	[Arguments("columns(a b c d e, 4, 3, L, , , <, |, >, 0, , , 1)", "<a...|c...|e...>/<b...|d...|....>")]
	[Arguments("columns(ab cd@e, 6, 2, J, , , , |, , 1, @)", "ab..cd|e.....")]
	public async Task HelpExamples(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>Many wide columns stay within the output limit.</summary>
	[Test]
	public async Task StaysWithinOutputLimit()
	{
		var parser = Parser;
		var result = await parser.FunctionParse(MarkupText.Plain("columns(a,5000,5000,L,,,,|,,,,,1)"));

		await Assert.That(result!.Message!.Length).IsLessThanOrEqualTo(parser.CurrentState.OutputLimit);
	}
}
