using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests ported from PennMUSH .t files testlnum.t and testjust.t, for cases not covered elsewhere.
/// testtr.t is in <c>StringFunctionUnitTests.Tr</c>, teststringsecs.t in
/// <c>TimeFunctionUnitTests.Stringsecs</c>, and testjust.t's center() rows in
/// <c>FormattingFunctionUnitTests.Center</c>.
/// </summary>
public class PennMUSHStringFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// === lnum() - Penn testlnum.t (NO dedicated tests) ===
	[Test]
	[Arguments("lnum(5)", "0 1 2 3 4")]
	[Arguments("lnum(4.5)", "0 1 2 3")]
	[Arguments("lnum(1,5)", "1 2 3 4 5")]
	[Arguments("lnum(10,1)", "10 9 8 7 6 5 4 3 2 1")]
	[Arguments("lnum(5,1,|,2)", "5|3|1")]
	[Arguments("lnum(5,1,|,-2)", "5|3|1")]
	[Arguments("lnum(1,5,|,-2)", "1|3|5")]
	[Arguments("lnum(4.5,1.5,%b,.5)", "4.5 4 3.5 3 2.5 2 1.5")]
	[Arguments("lnum(2,-2)", "2 1 0 -1 -2")]
	[Arguments("lnum(3,3)", "3")]
	[Arguments("lnum(1,4,@)", "1@2@3@4")]
	[Arguments("lnum(1,5,@,2)", "1@3@5")]
	[Arguments("lnum(1,5,,2)", "135")]
	[Arguments("lnum(-2,2)", "-2 -1 0 1 2")]
	[Arguments("lnum(1.5, 4.5)", "1.5 2.5 3.5 4.5")]
	[Arguments("lnum(1.5,4.5,%b,.5)", "1.5 2 2.5 3 3.5 4 4.5")]
	public async Task Lnum(string expr, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(expr));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// lnum() with no argument is empty, as in TinyMUX (help compatibility arguments); PennMUSH errors.
	[Test]
	[Arguments("lnum(#1)")]
	[Arguments("lnum(foo)")]
	[Arguments("lnum(1,)")]
	[Arguments("lnum(,5)")]
	public async Task LnumErrors(string expr)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(expr));
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// A range too long to return is refused before it is built.
	[Test]
	[Arguments("lnum(100000000)")]
	[Arguments("lnum(0,99999999)")]
	[Arguments("lnum(0,1000000000000000,%b,1)")]
	[Arguments("strlen(lnum(0,99999999))")]
	public async Task LnumPastTheOutputLimit(string expr)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(expr));
		await Assert.That(result.ToPlainText()).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
	}

	// === ljust/rjust edge cases from testjust.t not already covered ===
	[Test]
	[Arguments("ljust(foo bar baz,5,=,1)", "foo b")]
	[Arguments("rjust(foo bar baz,5,=,1)", "foo b")]
	public async Task JustTruncate(string expr, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(expr));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
