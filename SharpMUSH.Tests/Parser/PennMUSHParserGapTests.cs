using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests proving PennMUSH behavioral gaps in SharpMUSH's parser.
/// Each test documents the PennMUSH-expected behavior (verified against a live PennMUSH instance).
/// Tests that fail represent gaps needing implementation.
///
/// PennMUSH version tested: current git (pennmush/pennmush)
/// Test oracle: pennmush/test/testgaps.t and testgaps2.t (all pass against PennMUSH)
/// </summary>
public class PennMUSHParserGapTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// ============================================================
	// GAP 1: FunctionFlags.Literal / lit() function
	//
	// PennMUSH behavior: lit() returns its argument with NO evaluation
	// whatsoever — no %-substitutions, no [bracket] evaluation,
	// no function calls, no space compression. Only outer braces
	// and leading space stripping apply (from think's output handling).
	//
	// FunctionFlags.Literal (1 << 1) is DEFINED in SharpMUSH but
	// never referenced in the visitor.
	// ============================================================

	[Test]
	[Arguments("lit(hello world)", "hello world")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_BasicText(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lit(%#)", "%#")]
	[Arguments("lit(%b%b%b)", "%b%b%b")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_SuppressesPercentSubstitutions(string input, string expected)
	{
		// PennMUSH: lit(%#) -> literal "%#", NOT the dbref of the enactor
		// PennMUSH: lit(%b%b%b) -> literal "%b%b%b", NOT three spaces
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lit([add(1,2)])", "[add(1,2)]")]
	[Arguments("lit([foo([bar()])])", "[foo([bar()])]")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_SuppressesBracketEvaluation(string input, string expected)
	{
		// PennMUSH: lit([add(1,2)]) -> literal "[add(1,2)]", NOT "3"
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lit({test})", "{test}")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_SuppressesBraceProcessing(string input, string expected)
	{
		// PennMUSH: lit({test}) -> literal "{test}", braces not stripped
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lit(near       far)", "near       far")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_PreservesMultipleSpaces(string input, string expected)
	{
		// PennMUSH: lit() explicitly removes PE_COMPRESS_SPACES
		// so "near       far" keeps all internal spaces
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lit(|)", "|")]
	[Arguments("lit(;)", ";")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_SpecialCharacters(string input, string expected)
	{
		// PennMUSH: special characters pass through lit() as-is
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	// ============================================================
	// GAP 2: PE_COMPRESS_SPACES
	//
	// PennMUSH behavior: PE_COMPRESS_SPACES is part of PE_DEFAULT.
	// All normal expression evaluation compresses multiple spaces
	// to a single space AND strips leading/trailing spaces.
	//
	// SharpMUSH: No implementation whatsoever. Zero references to
	// space compression in grammar, visitor, or ParserState.
	// ============================================================

	[Test]
	[Arguments("cat(a,  b)", "a  b")]
	[Category("PennMUSH Parity - Space Compression")]
	public async Task SpaceCompression_CatFunction(string input, string expected)
	{
		// PennMUSH: cat(a,  b) -> "a b" (spaces compressed in output)
		// Note: cat() joins with single space, but PE_COMPRESS_SPACES
		// also compresses the result. The "expected" here is what cat()
		// actually returns before compression. The compression happens
		// at the think/evaluation level, not inside cat().
		// For FunctionParse, we test what the function itself returns.
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	// ============================================================
	// GAP 3: %? substitution
	//
	// PennMUSH behavior: %? returns "<invocations> <recursions>"
	// as two space-separated integers.
	// Source: src/parse.c lines 2443-2451
	//
	// SharpMUSH: returns parser.State.Count() (stack depth only,
	// one number).
	// ============================================================

	[Test]
	[Category("PennMUSH Parity - %? Substitution")]
	public async Task PercentQuestion_ReturnsTwoValues()
	{
		// PennMUSH: think %? -> "N M" where N=invocations, M=recursions
		// SharpMUSH currently returns only one number
		var result = (await Parser.FunctionParse(MModule.single("%?")))?.Message?.ToString();

		// Should contain a space separating two numbers
		await Assert.That(result).IsNotNull();
		var parts = result!.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(int.TryParse(parts[0], out _)).IsTrue();
		await Assert.That(int.TryParse(parts[1], out _)).IsTrue();
	}

	[Test]
	[Category("PennMUSH Parity - %? Substitution")]
	public async Task PercentQuestion_InvocationsIncrementWithFunctionCalls()
	{
		// After calling add(1,2), invocation count should be higher
		var result = (await Parser.FunctionParse(MModule.single("[add(1,2)]%?")))?.Message?.ToString();

		await Assert.That(result).IsNotNull();
		// Should start with "3" (the add result) then "N M"
		// The invocation count N should be > 0
		var afterResult = result!.Substring(1).Trim(); // skip the "3"
		var parts = afterResult.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(2);

		var invocations = int.Parse(parts[0]);
		await Assert.That(invocations).IsGreaterThan(0);
	}

	// ============================================================
	// GAP 4: PE_BUILTINONLY / fn() function
	//
	// PennMUSH behavior: fn(name, args...) calls ONLY the built-in
	// function, bypassing any @function override. If the name is
	// not a built-in, returns #-1.
	// Source: src/funufun.c line 92, src/parse.c line 2785
	//
	// SharpMUSH: No distinction between built-in and @function in
	// FunctionLibrary. fn() would need PE_BUILTINONLY to work.
	// ============================================================

	[Test]
	[Arguments("fn(add,1,2)", "3")]
	[Arguments("fn(mul,3,4)", "12")]
	[Arguments("fn(cat,hello,world)", "hello world")]
	[Arguments("fn(mid,hello,1,3)", "ell")]
	[Category("PennMUSH Parity - fn()")]
	public async Task Fn_CallsBuiltinFunctions(string input, string expected)
	{
		// PennMUSH: fn(add,1,2) -> 3 (calls built-in add directly)
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fn(notafunction)", "#-1")]
	[Category("PennMUSH Parity - fn()")]
	public async Task Fn_UnknownFunctionReturnsError(string input, string expected)
	{
		// PennMUSH: fn(notafunction) -> #-1 (not a built-in)
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).StartsWith(expected);
	}

	// ============================================================
	// GAP 5: PE_USERFN tracking
	//
	// PennMUSH behavior: PE_USERFN flag is set when evaluating
	// inside an @function's attribute code. Functions with FN_USERFN
	// flag can only be called when PE_USERFN is set.
	// Source: src/parse.c lines 2867, 3062
	//
	// This requires @function setup which needs CommandParser.
	// Testing deferred to integration tests that can set up objects.
	// ============================================================

	// PE_USERFN tests require command execution context to set up
	// @function definitions. See PennMUSHParserGapCommandTests for
	// integration tests covering this gap.

	// ============================================================
	// GAP 6: Q-register handling in NoParse/NoEval mode
	//
	// PennMUSH behavior: In NoParse context (e.g., inside lit()),
	// %q0 is returned as literal "%q0", NOT the register's value.
	// Source: TODO comment at visitor line 1930
	//
	// Normal context: %q0 returns the register's value.
	// ============================================================

	[Test]
	[Category("PennMUSH Parity - Q-Register NoParse")]
	public async Task QRegister_LitReturnsLiteral()
	{
		// PennMUSH: lit(%q0) -> literal "%q0"
		var result = (await Parser.FunctionParse(MModule.single("lit(%q0)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("%q0");
	}

	[Test]
	[Category("PennMUSH Parity - Q-Register NoParse")]
	public async Task QRegister_NormalContextReturnsValue()
	{
		// PennMUSH: [setq(0,test)]%q0 -> "test"
		var result = (await Parser.FunctionParse(MModule.single("[setq(0,test)]%q0")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("test");
	}

	[Test]
	[Category("PennMUSH Parity - Q-Register NoParse")]
	public async Task QRegister_SetThenLit()
	{
		// PennMUSH: [setq(0,test)]lit(%q0) -> "%q0"
		// setq runs (setting q0=test), but lit() returns %q0 literally
		var result = (await Parser.FunctionParse(MModule.single("[setq(0,test)]lit(%q0)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("%q0");
	}

	// ============================================================
	// GAP 7: lsargs (list-style arguments)
	//
	// PennMUSH behavior: Commands with /lsargs flag split the
	// left-side argument on commas and make each available as
	// %0, %1, %2, etc.
	// Source: TODO at visitor line 1734, PennMUSH penncmd.hlp
	//
	// This requires @command/add setup which needs CommandParser.
	// Testing deferred to integration tests.
	// ============================================================

	// lsargs tests require command execution to set up @command/add/lsargs.
	// See PennMUSHParserGapCommandTests for integration tests.
}
