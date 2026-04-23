using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests to verify that PARSER_STRICT_MODE environment variable correctly enables/disables strict error handling.
/// Split into separate tests for deterministic validation.
/// </summary>
public class StrictModeVerificationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	/// <summary>
	/// Tests that parser recovers from invalid syntax when PARSER_STRICT_MODE is NOT set.
	/// This test verifies normal error recovery behavior (production mode).
	/// 
	/// Input: "add(1,2" - missing closing paren
	/// Expected: Parser recovers and completes without throwing
	/// </summary>
	[Test]
	public async Task NormalMode_InvalidSyntax_ShouldRecover()
	{
		// Verify we're NOT in strict mode
		var strictMode = Environment.GetEnvironmentVariable("PARSER_STRICT_MODE");
		var isStrictMode = !string.IsNullOrEmpty(strictMode) &&
			(strictMode.Equals("true", StringComparison.OrdinalIgnoreCase) || strictMode == "1");

		// This test only runs in normal mode
		if (isStrictMode)
		{
			// Skip test if in strict mode
			return;
		}

		// This input is intentionally invalid - missing closing paren
		var input = MModule.single("add(1,2");

		// Normal mode should recover and complete without throwing
		var result = await Parser.FunctionParse(input);

		// Test passes if we get here without exception
		await Assert.That(result).IsNotNull();
	}

	/// <summary>
	/// Tests that parser throws exceptions on invalid syntax when PARSER_STRICT_MODE=true.
	/// This test verifies strict error handling behavior (diagnostic mode).
	/// 
	/// Input: "add(1,2" - missing closing paren
	/// Expected: StrictErrorStrategy throws InvalidOperationException
	/// </summary>
	[Test]
	public async Task StrictMode_InvalidSyntax_ShouldThrow()
	{
		// Verify we ARE in strict mode
		var strictMode = Environment.GetEnvironmentVariable("PARSER_STRICT_MODE");
		var isStrictMode = !string.IsNullOrEmpty(strictMode) &&
			(strictMode.Equals("true", StringComparison.OrdinalIgnoreCase) || strictMode == "1");

		// This test only runs in strict mode
		if (!isStrictMode)
		{
			// Skip test if NOT in strict mode
			return;
		}

		// This input is intentionally invalid - missing closing paren
		var input = MModule.single("add(1,2");

		// Strict mode should throw InvalidOperationException
		await Assert.That(async () => await Parser.FunctionParse(input))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("Unexpected token in rule");
	}

	/// <summary>
	/// Tests that parser handles valid syntax correctly regardless of strict mode setting.
	/// This test should always pass in both normal and strict modes.
	/// </summary>
	[Test]
	public async Task BothModes_ValidSyntax_ShouldComplete()
	{
		// Valid input should work in both modes
		var validInput = MModule.single("add(1,2)");
		var result = await Parser.FunctionParse(validInput);

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message!.ToString()).IsEqualTo("3");
	}

	/// <summary>
	/// Diagnostic test to show current strict mode configuration.
	/// Always passes but outputs diagnostic information for troubleshooting.
	/// </summary>
	[Test]
	public async Task DiagnosticOutput_ShowStrictModeStatus()
	{
		var strictMode = Environment.GetEnvironmentVariable("PARSER_STRICT_MODE");
		var isStrictMode = !string.IsNullOrEmpty(strictMode) &&
			(strictMode.Equals("true", StringComparison.OrdinalIgnoreCase) || strictMode == "1");

		Console.WriteLine("=== STRICT MODE DIAGNOSTIC ===");
		Console.WriteLine($"PARSER_STRICT_MODE environment variable: '{strictMode ?? "(not set)"}'");
		Console.WriteLine($"Is strict mode enabled: {isStrictMode}");
		Console.WriteLine($"Expected behavior: {(isStrictMode ? "Parser throws on syntax errors" : "Parser recovers from syntax errors")}");
		Console.WriteLine("===============================");

		// Test valid syntax to ensure parser is working
		var validInput = MModule.single("add(1,2)");
		var result = await Parser.FunctionParse(validInput);
		Console.WriteLine($"Valid input test result: {result?.Message}");

		await Assert.That(result).IsNotNull();
	}
}
