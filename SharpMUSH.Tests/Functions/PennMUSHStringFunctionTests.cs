using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests ported from PennMUSH .t files: testtr.t, testlnum.t, testjust.t, teststrreplace.t, teststringsecs.t
/// Only covers cases NOT already in existing test files.
/// </summary>
public class PennMUSHStringFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// === tr() - Penn testtr.t (NO existing tests) ===
	[Test]
	[Arguments("tr(test STRING,,)", "test STRING")]
	[Arguments("tr(test STRING,t,f)", "fesf STRING")]
	[Arguments("tr(test STRING,tT,fF)", "fesf SFRING")]
	[Arguments("tr(test STRING,Tt,Ff)", "fesf SFRING")]
	[Arguments("tr(test STRING,te,et)", "etse STRING")]
	public async Task Tr(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("tr(test STRING,t,)", "#-1")]
	[Arguments("tr(test STRING,,t)", "#-1")]
	public async Task TrErrors(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).StartsWith(expected);
	}

	// === lnum() - Penn testlnum.t (NO dedicated tests) ===
	[Test]
	[Arguments("lnum(5)", "0 1 2 3 4")]
	[Arguments("lnum(4.5)", "0 1 2 3")]
	[Arguments("lnum(1,5)", "1 2 3 4 5")]
	[Arguments("lnum(1,4,@)", "1@2@3@4")]
	[Arguments("lnum(1,5,@,2)", "1@3@5")]
	[Arguments("lnum(1,5,,2)", "135")]
	[Arguments("lnum(-2,2)", "-2 -1 0 1 2")]
	[Arguments("lnum(1.5, 4.5)", "1.5 2.5 3.5 4.5")]
	[Arguments("lnum(1.5,4.5,%b,.5)", "1.5 2 2.5 3 3.5 4 4.5")]
	public async Task Lnum(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lnum()")]
	[Arguments("lnum(#1)")]
	[Arguments("lnum(foo)")]
	[Arguments("lnum(1,)")]
	[Arguments("lnum(,5)")]
	public async Task LnumErrors(string expr)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).StartsWith("#-1");
	}

	// === ljust/rjust/center edge cases from testjust.t not already covered ===
	[Test]
	[Arguments("ljust(foo bar baz,5,=,1)", "foo b")]
	[Arguments("rjust(foo bar baz,5,=,1)", "foo b")]
	public async Task JustTruncate(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("center(foo, 5, =, ~)", "=foo~")]
	public async Task CenterAsymmetric(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	// === stringsecs - Penn teststringsecs.t (check against existing TimeFunctionUnitTests) ===
	[Test]
	[Arguments("stringsecs(10s)", "10")]
	[Arguments("stringsecs(5m 10s)", "310")]
	[Arguments("stringsecs(10s 5m)", "310")]
	[Arguments("stringsecs(1d 2h 3m 4s)", "93784")]
	public async Task StringSecs(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stringsecs(a)", "#-1 INVALID TIMESTRING")]
	[Arguments("stringsecs(h)", "#-1 INVALID TIMESTRING")]
	public async Task StringSecsErrors(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}
}
