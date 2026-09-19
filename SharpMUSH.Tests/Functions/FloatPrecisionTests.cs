using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>float_precision</c> is the number of decimal places every floating-point result is written
/// with, trailing zeros dropped: PennMUSH's <c>unparse_number</c> (<c>src/unparse.c:251</c>) is
/// <c>snprintf("%.*f", FLOAT_PRECISION)</c> and a strip, and every math function writes through it.
/// Here each function family formatted on its own: the decimal aggregates to a fixed 10 places,
/// <c>double</c> results to 15 significant digits, <c>e()</c> and the vector functions unrounded.
/// </summary>
public class FloatPrecisionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser.FromState(ParserState.RootFor(new DBRef(1)));

	private async Task<string> Evaluate(string code, uint? precision = null)
	{
		using var configuration = TestOptionsOverride.Scope(options => precision is { } places
			? options with { Cosmetic = options.Cosmetic with { FloatPrecision = places } }
			: options);
		return (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();
	}

	[Test]
	[Arguments("fdiv(1,3)", "0.333333")]
	[Arguments("fdiv(2,3)", "0.666667")]
	[Arguments("pi()", "3.141593")]
	[Arguments("e()", "2.718282")]
	[Arguments("sqrt(2)", "1.414214")]
	[Arguments("acos(0)", "1.570796")]
	[Arguments("lmath(fdiv,1 3)", "0.333333")]
	[Arguments("lmath(mean,1 2 2)", "1.666667")]
	[Arguments("vunit(1 1)", "0.707107 0.707107")]
	[Arguments("vmul(0.1234567 1,1 1)", "0.123457 1")]
	[Arguments("lnum(0,1,%b,0.3333333)", "0 0.333333 0.666667 1")]
	public async Task EveryFamilyUsesTheConfiguredPlaces(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]", precision: 6)).IsEqualTo(expected);

	/// <summary>
	/// PennMUSH's own suite sets <c>float_precision=10</c> around this case (<c>test/testmath.t:65-67</c>),
	/// which is what shows the setting counts decimal places: 10 of them, not 10 significant digits.
	/// </summary>
	[Test]
	public async Task PennMUSHRootCaseAtTenPlaces()
		=> await Assert.That(await Evaluate("[root(125, 5)]", precision: 10)).IsEqualTo("2.6265278044");

	[Test]
	[Arguments("fdiv(1,3)", "0.333333333333333")]
	[Arguments("pi()", "3.141592653589793")]
	[Arguments("e()", "2.718281828459045")]
	[Arguments("sqrt(2)", "1.414213562373095")]
	public async Task DefaultIsFifteenPlaces(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]")).IsEqualTo(expected);

	[Test]
	[Arguments("add(0.1,0.2)", "0.3")]
	[Arguments("fdiv(1,4)", "0.25")]
	[Arguments("fdiv(6,3)", "2")]
	// %.*f never switches to exponent notation.
	[Arguments("power(10,20)", "100000000000000000000")]
	[Arguments("fdiv(1,10000000)", "0")]
	// Rounds to zero from below: written as 0, not -0.
	[Arguments("fdiv(-1,10000000)", "0")]
	public async Task TrailingZerosAreDropped(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]", precision: 6)).IsEqualTo(expected);

	/// <summary><c>fun_round</c> caps <c>&lt;places&gt;</c> at <c>FLOAT_PRECISION</c> (<c>src/funmath.c:909</c>).</summary>
	[Test]
	[Arguments("round(pi(),10)", "3.141593")]
	[Arguments("round(pi(),2)", "3.14")]
	[Arguments("round(2.5,3,1)", "2.500")]
	[Arguments("round(pi(),10,1)", "3.141593")]
	public async Task RoundIsCappedAtThePrecision(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]", precision: 6)).IsEqualTo(expected);

	/// <summary>
	/// <c>fun_round</c> takes an unsigned place count and answers <c>e_int</c> otherwise
	/// (<c>src/funmath.c:898</c>). A negative count reached <c>Math.Round</c> and threw.
	/// </summary>
	[Test]
	public async Task RoundRefusesNegativePlaces()
		=> await Assert.That(await Evaluate("[round(pi(),-1)]")).IsEqualTo("#-1 ARGUMENT MUST BE INTEGER");
}
