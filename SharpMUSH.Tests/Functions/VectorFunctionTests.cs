namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The vector family, which had no test at all: every one of vadd(), vsub(), vmul(), vdot(),
/// vmax(), vmin() and vdim() was registered, documented and never once called by the suite.
///
/// <para>A vector is a list of numbers, separated by a space or by <c>&lt;delimiter&gt;</c>.
/// SharpMUSH adds an output separator PennMUSH does not have, defaulting to the delimiter.</para>
/// </summary>
public class VectorFunctionTests : ServerTestBase
{
	[Test]
	[Arguments("vadd(1 2 3,4 5 6)", "5 7 9")]
	[Arguments("vsub(4 5 6,1 2 3)", "3 3 3")]
	[Arguments("vmax(1 5 3,4 2 6)", "4 5 6")]
	[Arguments("vmin(1 5 3,4 2 6)", "1 2 3")]
	[Arguments("vdot(1 2 3,4 5 6)", "32")]
	[Arguments("vcross(1 0 0,0 1 0)", "0 0 1")]
	[Arguments("vdim(1 2 3)", "3")]
	[Arguments("vdim()", "0")]
	public async Task TheVectorOperations(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>Scaling: one side may be a single number rather than a vector.</summary>
	[Test]
	[Arguments("vmul(1 2 3,2)", "2 4 6")]
	[Arguments("vmul(2,1 2 3)", "2 4 6")]
	public async Task MultiplicationScalesAVectorByANumber(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>
	/// The delimiter splits both inputs; the output separator defaults to it, which is what keeps a
	/// pipe-delimited vector pipe-delimited on the way out.
	/// </summary>
	[Test]
	[Arguments("vadd(1|2|3,4|5|6,|)", "5|7|9")]
	[Arguments("vdim(1|2|3,|)", "3")]
	public async Task ADelimiterAppliesToBothSidesAndToTheResult(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>
	/// PennMUSH's delim_check (function.c:254) reads a delimiter argument that is present but empty as
	/// the default space, which is what makes the four-argument form reachable at all: you have to
	/// pass something in slot three to get to slot four.
	/// </summary>
	/// <remarks>
	/// Only the functions that read their own arguments are covered here. <c>vdim()</c> and
	/// <c>vmag()</c> take theirs through <c>ArgHelpers.NoParseDefaultNoParseArgument</c>, which
	/// substitutes its default only for an <em>absent</em> slot, so <c>vdim(1 2 3,)</c> answers 1
	/// rather than 3. That helper is shared with the whole list family and changing it is a far wider
	/// behaviour change than this lane should make quietly — reported rather than fixed.
	/// </remarks>
	[Test]
	[Arguments("vadd(1 2 3,4 5 6,)", "5 7 9")]
	[Arguments("vadd(1 2 3,4 5 6,,|)", "5|7|9")]
	[Arguments("vcross(1 0 0,0 1 0,)", "0 0 1")]
	public async Task AnEmptyDelimiterIsTheDefaultSpace(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>SharpMUSH's fourth argument: the output separator, where PennMUSH has only three.</summary>
	[Test]
	public async Task AnOutputSeparatorOverridesTheDelimiter()
		=> await Assert.That(await Eval("vadd(1|2|3,4|5|6,|,+)")).IsEqualTo("5+7+9");

	/// <summary>
	/// PennMUSH pairs the vectors element by element, so a length mismatch is an error rather than a
	/// short or zero-padded answer (funmath.c:522).
	/// </summary>
	[Test]
	[Arguments("vadd(1 2 3,4 5)")]
	[Arguments("vsub(1 2,4 5 6)")]
	[Arguments("vdot(1 2 3,4 5)")]
	public async Task MismatchedLengthsAreRefused(string code)
		=> await Assert.That(await Eval(code)).IsEqualTo("#-1 VECTORS MUST BE SAME DIMENSIONS");
}
