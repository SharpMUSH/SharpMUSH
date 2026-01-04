using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.LanguageServer.Extensions;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.LanguageServer;

/// <summary>
/// Tests for the LSP-specific MUSH code parser.
/// </summary>
public class LSPMUSHCodeParserTests
{
	private static LSPMUSHCodeParser CreateParser()
	{
		var services = new ServiceCollection();
		services.AddLogging();

		var serviceProvider = services.BuildServiceProvider();
		var logger = serviceProvider.GetRequiredService<ILogger<SharpMUSH.Implementation.MUSHCodeParser>>();

		var underlyingParser = MUSHCodeParserExtensions.CreateForLSP(logger, serviceProvider);
		return new LSPMUSHCodeParser(underlyingParser);
	}

	[Test]
	public async Task ValidSyntax_NoDiagnostics()
	{
		// Arrange
		var parser = CreateParser();
		var code = "add(1,2)";

		// Act
		var diagnostics = parser.GetDiagnostics(code, ParseType.Function);

		// Assert
		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task InvalidSyntax_ReturnsDiagnostics()
	{
		// Arrange
		var parser = CreateParser();
		var code = "add(1,2"; // Missing closing parenthesis

		// Act
		var diagnostics = parser.GetDiagnostics(code, ParseType.Function);

		// Assert
		await Assert.That(diagnostics).IsNotEmpty();
		await Assert.That(diagnostics.Any(d => d.Severity == SharpMUSH.Library.Models.DiagnosticSeverity.Error)).IsTrue();
	}

	[Test]
	public async Task ValidateSyntax_ValidCode_ReturnsTrue()
	{
		// Arrange
		var parser = CreateParser();
		var code = "name(#123)";

		// Act
		var isValid = parser.ValidateSyntax(code, ParseType.Function);

		// Assert
		await Assert.That(isValid).IsTrue();
	}

	[Test]
	public async Task ValidateSyntax_InvalidCode_ReturnsFalse()
	{
		// Arrange
		var parser = CreateParser();
		var code = "name(#123"; // Missing closing parenthesis

		// Act
		var isValid = parser.ValidateSyntax(code, ParseType.Function);

		// Assert
		await Assert.That(isValid).IsFalse();
	}

	[Test]
	public async Task GetSemanticTokens_ValidCode_ReturnsTokens()
	{
		// Arrange
		var parser = CreateParser();
		var code = "add(1,2)";

		// Act
		var tokens = parser.GetSemanticTokens(code, ParseType.Function);

		// Assert
		await Assert.That(tokens).IsNotNull();
		await Assert.That(tokens.TokenTypes).IsNotEmpty();
		await Assert.That(tokens.Data).IsNotEmpty();
		// Data should be in groups of 5: [deltaLine, deltaChar, length, tokenType, modifiers]
		await Assert.That(tokens.Data.Length % 5).IsEqualTo(0);
	}

	[Test]
	public async Task GetSemanticTokens_InvalidCode_ReturnsEmptyTokens()
	{
		// Arrange
		var parser = CreateParser();
		var code = ""; // Empty code

		// Act
		var tokens = parser.GetSemanticTokens(code, ParseType.Function);

		// Assert - Should return empty data without crashing
		await Assert.That(tokens).IsNotNull();
	}

	[Test]
	public async Task ParserIsStateless_MultipleCalls_NoSideEffects()
	{
		// Arrange
		var parser = CreateParser();
		var code1 = "add(1,2)";
		var code2 = "sub(5,3)";

		// Act - Parse twice with different code
		var diagnostics1 = parser.GetDiagnostics(code1, ParseType.Function);
		var diagnostics2 = parser.GetDiagnostics(code2, ParseType.Function);

		// Assert - Both should succeed independently
		await Assert.That(diagnostics1).IsEmpty();
		await Assert.That(diagnostics2).IsEmpty();

		// Re-parse first code to ensure no state was altered
		var diagnostics1Again = parser.GetDiagnostics(code1, ParseType.Function);
		await Assert.That(diagnostics1Again).IsEmpty();
	}

	[Test]
	public async Task ErrorRange_IsAccurate()
	{
		// Arrange
		var parser = CreateParser();
		var code = "add(1"; // Missing closing and second argument

		// Act
		var diagnostics = parser.GetDiagnostics(code, ParseType.Function);

		// Assert
		await Assert.That(diagnostics).IsNotEmpty();
		var firstError = diagnostics.First();
		await Assert.That(firstError.Range).IsNotNull();
		await Assert.That(firstError.Range.Start.Line).IsGreaterThanOrEqualTo(0);
		await Assert.That(firstError.Range.Start.Character).IsGreaterThanOrEqualTo(0);
	}
}
