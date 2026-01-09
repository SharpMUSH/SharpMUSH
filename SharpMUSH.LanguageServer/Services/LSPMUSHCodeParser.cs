using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using MModule = MarkupString.MarkupStringModule;
using MString = MarkupString.MarkupStringModule.MarkupString;

namespace SharpMUSH.LanguageServer.Services;

/// <summary>
/// A stateless, read-only wrapper around the MUSH parser for LSP operations.
/// This parser only performs syntax validation and semantic analysis without
/// altering any state or requiring runtime dependencies.
/// </summary>
public class LSPMUSHCodeParser
{
	private readonly IMUSHCodeParser _parser;

	public LSPMUSHCodeParser(IMUSHCodeParser parser)
	{
		_parser = parser;
	}

	/// <summary>
	/// Analyzes MUSH code and returns diagnostics (errors, warnings, etc.)
	/// This operation is read-only and does not alter parser state.
	/// </summary>
	public IReadOnlyList<Diagnostic> GetDiagnostics(string text, ParseType parseType = ParseType.Function)
	{
		try
		{
			// Use the parser's diagnostic capabilities
			// The GetDiagnostics method is designed to be stateless
			return _parser.GetDiagnostics(MModule.single(text), parseType);
		}
		catch (Exception ex)
		{
			// If parsing fails catastrophically, return a diagnostic
			return new List<Diagnostic>
			{
				new()
				{
					Range = new Library.Models.Range
					{
						Start = new Position(0, 0),
						End = new Position(0, text.Length)
					},
					Severity = DiagnosticSeverity.Error,
					Source = "SharpMUSH.LSP",
					Message = $"Parser error: {ex.Message}"
				}
			};
		}
	}

	/// <summary>
	/// Performs semantic analysis and returns tokens for syntax highlighting.
	/// This operation is read-only and does not alter parser state.
	/// </summary>
	public SemanticTokensData GetSemanticTokens(string text, ParseType parseType = ParseType.Function)
	{
		try
		{
			// Use the parser's semantic token capabilities
			// The GetSemanticTokensData method is designed to be stateless
			return _parser.GetSemanticTokensData(MModule.single(text), parseType);
		}
		catch (Exception ex)
		{
			// If analysis fails, return empty token data
			Console.Error.WriteLine($"Semantic analysis error: {ex.Message}");
			return new SemanticTokensData
			{
				TokenTypes = Array.Empty<string>(),
				TokenModifiers = Array.Empty<string>(),
				Data = Array.Empty<int>()
			};
		}
	}

	/// <summary>
	/// Validates syntax without performing semantic analysis.
	/// Returns true if the syntax is valid, false otherwise.
	/// </summary>
	public bool ValidateSyntax(string text, ParseType parseType = ParseType.Function)
	{
		var diagnostics = GetDiagnostics(text, parseType);
		return diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
	}
}
