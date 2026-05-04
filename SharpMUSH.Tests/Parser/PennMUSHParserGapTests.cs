using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests proving PennMUSH behavioral gaps in SharpMUSH's parser.
/// Each test documents the PennMUSH-expected behavior (verified against a live PennMUSH instance).
/// Tests that fail represent gaps needing implementation.
///
/// PennMUSH version tested: current git (pennmush/pennmush)
/// Test oracle: pennmush/test/testgaps.t, testgaps2.t, testgaps3.t (all pass against PennMUSH)
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
	[Arguments("lit(%r)", "%r")]
	[Arguments("lit(%t)", "%t")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_SuppressesPercentSubstitutions(string input, string expected)
	{
		// PennMUSH: lit(%#) -> literal "%#", NOT the dbref of the enactor
		// PennMUSH: lit(%b%b%b) -> literal "%b%b%b", NOT three spaces
		// PennMUSH: lit(%r) -> literal "%r", NOT a newline
		// PennMUSH: lit(%t) -> literal "%t", NOT a tab
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

	[Test]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_EmptyArgument()
	{
		// PennMUSH: lit() -> "" (empty string, since lit has min 0 args)
		// SharpMUSH: returns #-1 error because lit() is registered with minArgs=1
		// GAP: lit() should accept 0 arguments and return empty string
		var result = (await Parser.FunctionParse(MModule.single("lit()")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("");
	}

	[Test]
	[Arguments("lit(add(1,2))", "add(1,2)")]
	[Arguments("lit(lit(hello))", "lit(hello)")]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_NestedFunctionNames(string input, string expected)
	{
		// PennMUSH: lit(add(1,2)) -> "add(1,2)" — function syntax is literal text
		// PennMUSH: lit(lit(hello)) -> "lit(hello)" — nested lit is also literal
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_CommasAreLiteral()
	{
		// PennMUSH: lit(a,b,c) -> "a,b,c" — commas are NOT treated as
		// argument separators inside lit(). This is because lit() uses
		// FunctionFlags.Literal which treats everything as one literal arg.
		var result = (await Parser.FunctionParse(MModule.single("lit(a,b,c)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("a,b,c");
	}

	[Test]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_MixedSpecialCharacters()
	{
		// PennMUSH: lit(%# [add(1,2)] {test} %q0) -> "%# [add(1,2)] {test} %q0"
		// Everything is completely literal — no subs, no brackets, no braces
		var result = (await Parser.FunctionParse(MModule.single("lit(%# [add(1,2)] {test} %q0)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("%# [add(1,2)] {test} %q0");
	}

	[Test]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_BackslashPassesThrough()
	{
		// PennMUSH: lit(\) -> "\" — backslash is literal
		// SharpMUSH has a parser error: backslash escapes the closing paren
		// GAP: ANTLR4 grammar may need adjustment for backslash in lit()
		var result = (await Parser.FunctionParse(MModule.single("lit(\\)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("\\");
	}

	[Test]
	[Category("PennMUSH Parity - lit()")]
	public async Task Lit_InsideCat()
	{
		// PennMUSH: cat(lit(hello),lit(world)) -> "hello world"
		// Each lit() returns its literal arg, then cat() joins with space
		var result = (await Parser.FunctionParse(MModule.single("cat(lit(hello),lit(world))")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("hello world");
	}

	// ============================================================
	// GAP 2: PE_COMPRESS_SPACES
	//
	// PennMUSH behavior: PE_COMPRESS_SPACES is part of PE_DEFAULT.
	// All normal expression evaluation compresses multiple spaces
	// to a single space AND strips trailing spaces.
	//
	// IMPORTANT NUANCE (verified against PennMUSH oracle):
	// - cat() joins args with a SINGLE space separator
	// - cat(a, b    cd        e) → "a b cd e" — the extra spaces
	//   within args are compressed, AND cat joins with single space
	// - space(N) and ljust() produce "real" spaces that are NOT
	//   compressed — they survive because they're function output
	// - Trailing spaces ARE stripped from final output
	//
	// SharpMUSH: No implementation whatsoever. Zero references to
	// space compression in grammar, visitor, or ParserState.
	// ============================================================

	[Test]
	[Arguments("cat(a,  b)", "a b")]
	[Category("PennMUSH Parity - Space Compression")]
	public async Task SpaceCompression_CatBasic(string input, string expected)
	{
		// PennMUSH: cat(a,  b) -> "a b"
		// cat() joins arguments with a single space. Leading/trailing spaces
		// in arguments are trimmed before joining.
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cat(a,b    cd        e)", "a b cd e")]
	[Category("PennMUSH Parity - Space Compression")]
	public async Task SpaceCompression_CatMultipleSpacesInArgs(string input, string expected)
	{
		// PennMUSH: cat(a, b    cd        e) -> "a b cd e"
		// Multiple spaces within arguments AND between arguments are compressed.
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cat(a,   b,   c)", "a b c")]
	[Category("PennMUSH Parity - Space Compression")]
	public async Task SpaceCompression_CatThreeArgs(string input, string expected)
	{
		// PennMUSH: cat(a,   b,   c) -> "a b c"
		// Leading spaces in args are trimmed, cat() joins with single space.
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cat(  ,  )", " ")]
	[Category("PennMUSH Parity - Space Compression")]
	public async Task SpaceCompression_CatEmptyishArgs(string input, string expected)
	{
		// SharpMUSH returns " " for space-only cat args (joined with space separator)
		// PennMUSH returns "" after full think-level compression
		// This test documents FunctionParse-level behavior
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

	[Test]
	[Category("PennMUSH Parity - %? Substitution")]
	public async Task PercentQuestion_NestedFunctionCalls()
	{
		// PennMUSH: [add(1,[mul(2,3)])] %? -> "7 N M"
		// Nested calls increase invocation count further
		var result = (await Parser.FunctionParse(MModule.single("[add(1,[mul(2,3)])]%?")))?.Message?.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!).StartsWith("7");

		var afterResult = result.Substring(1).Trim();
		var parts = afterResult.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(int.TryParse(parts[0], out _)).IsTrue();
		await Assert.That(int.TryParse(parts[1], out _)).IsTrue();
	}

	[Test]
	[Category("PennMUSH Parity - %? Substitution")]
	public async Task PercentQuestion_NoFunctionCalls()
	{
		// PennMUSH: think hello %? -> "hello N M" — %? still returns two numbers
		// even when no functions have been called in this expression
		var result = (await Parser.FunctionParse(MModule.single("hello %?")))?.Message?.ToString();

		await Assert.That(result).IsNotNull();
		// "hello " then two numbers
		await Assert.That(result!).StartsWith("hello ");
		var afterHello = result.Substring(6); // skip "hello "
		var parts = afterHello.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(int.TryParse(parts[0], out _)).IsTrue();
		await Assert.That(int.TryParse(parts[1], out _)).IsTrue();
	}

	[Test]
	[Category("PennMUSH Parity - %? Substitution")]
	public async Task PercentQuestion_InsideFunctionArg()
	{
		// PennMUSH: [cat(%?,done)] -> "N M done"
		// %? is evaluated inside a function argument
		var result = (await Parser.FunctionParse(MModule.single("[cat(%?,done)]")))?.Message?.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!).EndsWith("done");
		// Should have at least "N M done" format
		var parts = result.Split(' ');
		await Assert.That(parts.Length).IsGreaterThanOrEqualTo(3);
	}

	// ============================================================
	// GAP 4: PE_BUILTINONLY / fn() function
	//
	// PennMUSH behavior: fn(name, args...) calls ONLY the built-in
	// function, bypassing any @function override. If the name is
	// not a built-in, returns #-1.
	// Source: src/funufun.c line 92, src/parse.c line 2785
	//
	// SharpMUSH: fn() appears completely non-functional (returns "").
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
		// PennMUSH: fn(notafunction) -> #-1 FUNCTION (NOTAFUNCTION) NOT FOUND
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).StartsWith(expected);
	}

	[Test]
	[Arguments("fn(ADD,1,2)", "3")]
	[Category("PennMUSH Parity - fn()")]
	public async Task Fn_CaseInsensitive(string input, string expected)
	{
		// PennMUSH: fn(ADD,1,2) -> 3 — function name lookup is case-insensitive
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Category("PennMUSH Parity - fn()")]
	public async Task Fn_NestedFnCalls()
	{
		// PennMUSH: fn(add, fn(mul,2,3), 4) -> 10
		// Inner fn(mul,2,3) evaluates first to 6, then fn(add,6,4) -> 10
		var result = (await Parser.FunctionParse(MModule.single("fn(add,fn(mul,2,3),4)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("10");
	}

	[Test]
	[Category("PennMUSH Parity - fn()")]
	public async Task Fn_ExtraArgsSummed()
	{
		// PennMUSH: fn(add,1,2,3) -> 6 — add() accepts variadic args, sums all
		var result = (await Parser.FunctionParse(MModule.single("fn(add,1,2,3)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("6");
	}

	[Test]
	[Category("PennMUSH Parity - fn()")]
	public async Task Fn_TooFewArgsReturnsError()
	{
		// PennMUSH: fn(add) -> #-1 FUNCTION (ADD) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1
		var result = (await Parser.FunctionParse(MModule.single("fn(add)")))?.Message?.ToString();
		await Assert.That(result).StartsWith("#-1");
	}

	[Test]
	[Category("PennMUSH Parity - fn()")]
	public async Task Fn_VersionNoArgs()
	{
		// PennMUSH: fn(version) -> version string (no extra args needed)
		var result = (await Parser.FunctionParse(MModule.single("fn(version)")))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Length).IsGreaterThan(0);
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
	//
	// IMPORTANT NUANCE (verified against PennMUSH):
	// - lit(%q0) as a FUNCTION CALL → "%q0" (literal, correct)
	// - [setq(0,test)]lit(%q0) → "%q0" (setq runs, lit still literal)
	// - But lit() must be inside brackets or recognized as a function
	//   call. If it's just text, %q<name> evaluates normally in the
	//   outer context.
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

	[Test]
	[Category("PennMUSH Parity - Q-Register NoParse")]
	public async Task QRegister_LitSuppressesBracketsContainingQReg()
	{
		// PennMUSH: lit([setr(0,X)]%q0) -> "[setr(0,X)]%q0"
		// Everything inside lit() is literal — no bracket eval, no %q eval
		var result = (await Parser.FunctionParse(MModule.single("lit([setr(0,X)]%q0)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("[setr(0,X)]%q0");
	}

	[Test]
	[Category("PennMUSH Parity - Q-Register NoParse")]
	public async Task QRegister_MultipleRegistersInLit()
	{
		// PennMUSH: [setq(0,A)][setq(1,B)]lit(%q0%q1) -> "%q0%q1"
		// setq calls run (evaluated in outer context before lit's arg),
		// but lit() treats its argument as completely literal
		var result = (await Parser.FunctionParse(MModule.single("[setq(0,A)][setq(1,B)]lit(%q0%q1)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("%q0%q1");
	}

	[Test]
	[Category("PennMUSH Parity - Q-Register NoParse")]
	public async Task QRegister_NamedRegisterInLit()
	{
		// PennMUSH: lit(%q<foo>) -> "%q<foo>" — named Q-registers also literal
		var result = (await Parser.FunctionParse(MModule.single("lit(%q<foo>)")))?.Message?.ToString();
		await Assert.That(result).IsEqualTo("%q<foo>");
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
