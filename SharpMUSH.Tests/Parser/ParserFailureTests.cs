using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests that the evaluating parser (FunctionParse / CommandParse) correctly surfaces
/// syntax errors as <c>#-1 PARSER FAILURE: ...</c> messages. ANTLR's <c>DefaultErrorStrategy</c>
/// still recovers internally, but the collecting <see cref="SharpMUSH.Implementation.ParserErrorListener"/> intercepts
/// every <c>SyntaxError</c> callback and causes <c>ParseInternal</c> to return the formatted
/// failure string before visiting the (partial) recovery tree.
/// </summary>
public class ParserFailureTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// ─── Helper ────────────────────────────────────────────────────────────────

	private async Task<string?> Eval(string input)
	{
		var result = await Parser.FunctionParse(MModule.single(input));
		return result?.Message?.ToString();
	}

	// ─── Missing closing parenthesis ───────────────────────────────────────────

	/// <summary>Baseline: what does the parser return for a valid expression?</summary>
	[Test]
	public async Task Valid_Add_ReturnsSix()
	{
		var result = await Eval("add(1,5)");
		await Assert.That(result).IsEqualTo("6");
	}

	/// <summary>
	/// Single-level missing ')'. The error listener intercepts ANTLR's EOF error
	/// and returns a PARSER FAILURE string instead of a partial result.
	/// </summary>
	[Test]
	public async Task MissingParen_SingleLevel_CurrentBehavior()
	{
		var result = await Eval("add(1,2");
		await Assert.That(result).IsEqualTo("#-1 PARSER FAILURE: Expected ) or , at end of expression");
	}

	/// <summary>
	/// Deeply nested missing ')' — the case from the failing unit test.
	/// </summary>
	[Test]
	public async Task MissingParen_FourLevelsDeep_CurrentBehavior()
	{
		var result = await Eval("add(1,add(1,add(1,add(1,5)))");
		await Assert.That(result).IsEqualTo("#-1 PARSER FAILURE: Expected ) or , at end of expression");
	}

	/// <summary>Missing ')' with a string function.</summary>
	[Test]
	public async Task MissingParen_StrCat_CurrentBehavior()
	{
		var result = await Eval("strcat(foo,bar");
		await Assert.That(result).IsEqualTo("#-1 PARSER FAILURE: Expected ) or , at end of expression");
	}

	// ─── Missing closing bracket ───────────────────────────────────────────────

	/// <summary>
	/// Unclosed bracket evaluation expression. The bracket opener starts an
	/// evaluated sub-expression, but the closing ']' is missing.
	/// </summary>
	[Test]
	public async Task MissingBracket_SingleLevel_CurrentBehavior()
	{
		var result = await Eval("[add(1,2)");
		await Assert.That(result).StartsWith("#-1 PARSER FAILURE:");
		await Assert.That(result).Contains("Expected ]");
	}

	/// <summary>Function call inside bracket, bracket unclosed.</summary>
	[Test]
	public async Task MissingBracket_InsideFunction_CurrentBehavior()
	{
		var result = await Eval("strcat(a,[add(1,2),b)");
		await Assert.That(result).StartsWith("#-1 PARSER FAILURE:");
		await Assert.That(result).Contains("Expected ]");
	}

	// ─── Missing closing brace ─────────────────────────────────────────────────

	/// <summary>
	/// Unclosed brace pattern. Braces suppress function evaluation inside them.
	/// </summary>
	[Test]
	public async Task MissingBrace_CurrentBehavior()
	{
		var result = await Eval("{unclosed content");
		await Assert.That(result).StartsWith("#-1 PARSER FAILURE:");
		await Assert.That(result).Contains("Expected }");
	}

	/// <summary>Unclosed brace inside a function argument.</summary>
	[Test]
	public async Task MissingBrace_InsideFunction_CurrentBehavior()
	{
		var result = await Eval("strcat(a,{unclosed,b)");
		await Assert.That(result).StartsWith("#-1 PARSER FAILURE:");
		await Assert.That(result).Contains("Expected }");
	}

	// ─── Multiple errors ───────────────────────────────────────────────────────

	/// <summary>Two separate unclosed function calls in one expression.</summary>
	[Test]
	public async Task MultipleErrors_TwoUnclosedFunctions_CurrentBehavior()
	{
		var result = await Eval("add(strcat(a,b)");
		await Assert.That(result).IsEqualTo("#-1 PARSER FAILURE: Expected ) or , at end of expression");
	}

	// ─── Position accuracy (via ValidateAndGetErrors) ──────────────────────────

	/// <summary>
	/// Verifies the error column reported by the parser for a missing ')' at a
	/// known position. The input "add(1,2" is 7 characters; EOF is at column 7.
	/// </summary>
	[Test]
	public async Task MissingParen_ErrorColumnIsCorrect()
	{
		var errors = Parser.ValidateAndGetErrors(MModule.single("add(1,2"), ParseType.Function);
		await Assert.That(errors).IsNotEmpty();
		var col = errors[0].Column;
		Console.WriteLine($@"add(1,2 → error column {col}");
		// EOF is reported at the position after the last character (column 7)
		await Assert.That(col).IsEqualTo(7);
	}

	/// <summary>
	/// Snippet content is populated and covers text around the error position.
	/// </summary>
	[Test]
	public async Task MissingParen_SnippetIsPopulated()
	{
		var errors = Parser.ValidateAndGetErrors(MModule.single("add(1,2"), ParseType.Function);
		await Assert.That(errors).IsNotEmpty();
		var snippet = errors[0].Snippet;
		await Assert.That(snippet).IsNotEmpty();
	}

	/// <summary>
	/// Verifies the column for a deeply nested missing paren.
	/// Input: "add(1,add(1,add(1,add(1,5)))" — 28 chars, EOF at column 28.
	/// </summary>
	[Test]
	public async Task NestedMissingParen_ErrorColumnIsCorrect()
	{
		const string input = "add(1,add(1,add(1,add(1,5)))";
		var errors = Parser.ValidateAndGetErrors(MModule.single(input), ParseType.Function);
		await Assert.That(errors).IsNotEmpty();
		var col = errors[0].Column;
		Console.WriteLine($@"{input} → error column {col} (input length {input.Length})");
		await Assert.That(col).IsEqualTo(input.Length);
	}

	/// <summary>
	/// Verifies ToMushFailureString() produces the expected format.
	/// </summary>
	[Test]
	public async Task ToMushFailureString_ProducesCorrectFormat()
	{
		const string input = "add(1,add(1,add(1,add(1,5)))";
		var errors = Parser.ValidateAndGetErrors(MModule.single(input), ParseType.Function);
		await Assert.That(errors).IsNotEmpty();
		var failureMsg = errors[0].ToMushFailureString();
		Console.WriteLine($"ToMushFailureString → '{failureMsg}'");
		await Assert.That(failureMsg).StartsWith("#-1 PARSER FAILURE:");
		await Assert.That(failureMsg).Contains(")");
	}

	// ─── Command parse modes ───────────────────────────────────────────────────

	/// <summary>Missing paren in a CommandEqSplit context (e.g., &ATTR obj=add(1,2).</summary>
	[Test]
	public async Task MissingParen_InEqSplit_CurrentBehavior()
	{
		var errors = Parser.ValidateAndGetErrors(
			MModule.single("some=add(1,2"), ParseType.CommandEqSplit);
		await Assert.That(errors).IsNotEmpty();
	}

	// ─── Edge cases ────────────────────────────────────────────────────────────

	/// <summary>
	/// A trailing ')' with no matching opener is rewritten to OTHER by the
	/// RewriteOrphanedBracketClosers pass, so it should NOT produce an error.
	/// </summary>
	[Test]
	public async Task TrailingCloseParen_NoOpener_IsLiteralText()
	{
		// add(1,5)) — the outer ) closes add(), inner ) has no opener → literal text
		var result = await Eval("add(1,5))");
		Console.WriteLine($@"add(1,5)) → '{result}'");
		// Expect "6)" — the orphaned ) becomes literal text
		await Assert.That(result).IsEqualTo("6)");
	}

	/// <summary>
	/// An escaped paren '\)' should always be literal text, never a closer.
	/// </summary>
	[Test]
	public async Task EscapedCloseParen_IsLiteralText()
	{
		var result = await Eval("strcat(a,\\),b)");
		Console.WriteLine($@"strcat(a,\),b) → '{result}'");
		await Assert.That(result).IsEqualTo("a)b");
	}
}
