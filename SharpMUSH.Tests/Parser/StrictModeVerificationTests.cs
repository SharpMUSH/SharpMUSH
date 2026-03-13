using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests to verify that strict mode is actually being applied.
/// Run with PARSER_STRICT_MODE=true to see strict mode behavior.
/// </summary>
public class StrictModeVerificationTests
{
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public required ServerWebAppFactory WebAppFactoryArg { get; init; }

private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

/// <summary>
/// Tests that parser handles intentionally invalid syntax.
/// 
/// Input: "add(1,2" - missing closing paren
/// Normal mode: Parser recovers and handles gracefully
/// Strict mode: StrictErrorStrategy throws InvalidOperationException
/// 
/// Expected results:
/// - Without PARSER_STRICT_MODE: Test passes, parser handles error gracefully
/// - With PARSER_STRICT_MODE=true: Test throws InvalidOperationException with strict mode message
/// </summary>
[Test]
public async Task StrictMode_InvalidSyntax_MissingClosingParen()
{
// This input is intentionally invalid - missing closing paren
var input = MModule.single("add(1,2");

// Get the strict mode setting from environment
var strictMode = Environment.GetEnvironmentVariable("PARSER_STRICT_MODE");
var isStrictMode = !string.IsNullOrEmpty(strictMode) &&
(strictMode.Equals("true", StringComparison.OrdinalIgnoreCase) || strictMode == "1");

Console.WriteLine($"[TEST] PARSER_STRICT_MODE={strictMode ?? "(not set)"}, isStrictMode={isStrictMode}");
Console.WriteLine($"[TEST] Testing invalid input: '{input}'");

try
{
var result = await Parser.FunctionParse(input);
Console.WriteLine($"[TEST] ✓ Parser completed without throwing (normal mode behavior)");
Console.WriteLine($"[TEST] Result: {result?.Message}");

// If strict mode is enabled, we should NOT reach here
if (isStrictMode)
{
Assert.Fail("Expected InvalidOperationException in strict mode, but parser completed successfully!");
}
}
catch (InvalidOperationException ex)
{
Console.WriteLine($"[TEST] ✗ Parser threw InvalidOperationException (strict mode behavior)");
Console.WriteLine($"[TEST] Exception message: {ex.Message}");

// If strict mode is NOT enabled, this is unexpected
if (!isStrictMode)
{
throw new Exception("Parser threw exception in normal mode - this should not happen!", ex);
}

// In strict mode, this is expected - test passes
Console.WriteLine($"[TEST] ✓ Strict mode correctly threw exception as expected");
}
}

/// <summary>
/// Diagnostic test to show strict mode configuration is being read.
/// This test always passes but outputs diagnostic information.
/// </summary>
[Test]
public async Task StrictMode_DiagnosticOutput()
{
var strictMode = Environment.GetEnvironmentVariable("PARSER_STRICT_MODE");
var isStrictMode = !string.IsNullOrEmpty(strictMode) &&
(strictMode.Equals("true", StringComparison.OrdinalIgnoreCase) || strictMode == "1");

Console.WriteLine("=== STRICT MODE DIAGNOSTIC ===");
Console.WriteLine($"PARSER_STRICT_MODE environment variable: '{strictMode ?? "(not set)"}'");
Console.WriteLine($"Is strict mode enabled: {isStrictMode}");
Console.WriteLine($"Expected behavior: {(isStrictMode ? "Parser should throw on syntax errors" : "Parser should recover from syntax errors")}");
Console.WriteLine("===============================");

// Test valid syntax to ensure parser is working
var validInput = MModule.single("add(1,2)");
var result = await Parser.FunctionParse(validInput);
Console.WriteLine($"Valid input test result: {result?.Message}");

await Assert.That(result).IsNotNull();
}
}
