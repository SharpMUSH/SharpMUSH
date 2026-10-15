using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's list maths, <c>ladd()</c> through <c>lxnor()</c>. The expected results are what an
/// unmodified RhostMUSH server answers for the same input.
/// </summary>
public class RhostListMathFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code) =>
		(await Parser.FunctionParse(MarkupText.Plain(code)))?.Message?.ToPlainText() ?? string.Empty;

	[Test]
	[Arguments("ladd(1 1 1 1 1)", "5")]
	[Arguments("ladd(1@1@1,@)", "3")]
	[Arguments("ladd(1.5 2.25)", "3.75")]
	[Arguments("ladd(a 2)", "2")]
	[Arguments("ladd(1.5x 1)", "2.5")]
	[Arguments("ladd(1e2 1)", "101")]
	[Arguments("ladd(-3 +2)", "-1")]
	[Arguments("ladd(.5 .5)", "1")]
	[Arguments("ladd(1@@2,@)", "3")]
	[Arguments("ladd(2 2,)", "4")]
	[Arguments("ladd(0.1 0.2)", "0.3")]
	[Arguments("ladd(1000000 1000000)", "2000000")]
	[Arguments("ladd()", "0")]
	[Arguments("ladd(,@)", "0")]
	public async Task LAdd(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lsub(10 2 3 1)", "4")]
	[Arguments("lsub(5)", "5")]
	[Arguments("lsub(1.5 0.25)", "1.25")]
	[Arguments("lsub()", "0")]
	public async Task LSub(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lmul(2 2 2 2)", "16")]
	[Arguments("lmul(1.5 2)", "3")]
	[Arguments("lmul(2@@3,@)", "0")]
	[Arguments("lmul(99999 99999)", "9999800001")]
	[Arguments("lmul()", "0")]
	public async Task LMul(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ldiv(20 2 0 0 0 0 2)", "5")]
	[Arguments("ldiv(7 2)", "3.5")]
	[Arguments("ldiv(1 3)", "0.333333")]
	[Arguments("ldiv(5 0)", "5")]
	[Arguments("ldiv(-7 2)", "-3.5")]
	[Arguments("ldiv(10 0 0)", "10")]
	[Arguments("ldiv(10 4)", "2.5")]
	[Arguments("ldiv()", "0")]
	public async Task LDiv(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lavg(2 4 6 8 10)", "6")]
	[Arguments("lavg(1 2)", "1.5")]
	[Arguments("lavg(1 2 4)", "2.333333")]
	[Arguments("lavg(a)", "0")]
	[Arguments("lavg()", "0")]
	public async Task LAvg(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lmax(1 10 2 4 6)", "10")]
	[Arguments("lmax(1.5 1.25)", "1.5")]
	[Arguments("lmax(-1 -2)", "-1")]
	[Arguments("lmax(,@)", "0")]
	[Arguments("lmin(1 10 2 -4 6)", "-4")]
	[Arguments("lmin(a 3)", "0")]
	[Arguments("lmin()", "0")]
	public async Task LMaxMin(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("land(0 1 1)", "0")]
	[Arguments("land(1 1)", "1")]
	[Arguments("land(1 a)", "0")]
	[Arguments("land(2 3)", "1")]
	[Arguments("land(1 -1)", "1")]
	[Arguments("land(1.9 1)", "1")]
	[Arguments("land(0.9 1)", "0")]
	[Arguments("land(1|1,|)", "1")]
	[Arguments("land()", "0")]
	[Arguments("lor(0 1 1)", "1")]
	[Arguments("lor(0 0)", "0")]
	[Arguments("lor(0 a)", "0")]
	[Arguments("lor(0 2)", "1")]
	[Arguments("lor()", "0")]
	public async Task LAndOr(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lxor(0 1 0 1 0 1)", "1")]
	[Arguments("lxor(1 1)", "0")]
	[Arguments("lxor(1)", "1")]
	[Arguments("lxor(2 0)", "1")]
	[Arguments("lxor(5)", "5")]
	[Arguments("lnor(0 0)", "1")]
	[Arguments("lnor(0 1)", "0")]
	[Arguments("lnor(1)", "1")]
	[Arguments("lnor(0)", "0")]
	[Arguments("lnor(5)", "5")]
	[Arguments("lxnor(1 1 1 1 1 0)", "0")]
	[Arguments("lxnor(1 1)", "1")]
	[Arguments("lxnor(0 0)", "1")]
	[Arguments("lxnor(1 0 1)", "0")]
	[Arguments("lxnor(1 1 1)", "1")]
	[Arguments("lxnor(0)", "0")]
	[Arguments("lxnor(1)", "1")]
	[Arguments("lxnor(7)", "7")]
	[Arguments("[lxor()][lnor()][lxnor()]", "000")]
	public async Task LFolds(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	/// <summary>The examples in the help for ladd() and land(), as written there.</summary>
	[Test]
	[Arguments("ladd(1 2 3 4)", "10")]
	[Arguments("lsub(10 2 3 1)", "4")]
	[Arguments("lmul(2|2|2|2, |)", "16")]
	[Arguments("ldiv(20 2 0 2)", "5")]
	[Arguments("lavg(2 4 6 8 10)", "6")]
	[Arguments("lmax(1 10 2 4 6)", "10")]
	[Arguments("lmin(1 10 2 -4 6)", "-4")]
	[Arguments("land(1 1 0)", "0")]
	[Arguments("lor(0 0 2)", "1")]
	[Arguments("lxor(0 1 0 1 0 1)", "1")]
	[Arguments("lnor(0 0)", "1")]
	[Arguments("lxnor(1 0 1)", "0")]
	public async Task HelpExamples(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}
}
