using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using MModule = MarkupString.MarkupStringModule;

namespace SharpMUSH.LanguageServer.Services;

/// <summary>
/// A stateless, read-only wrapper around the MUSH parser for LSP operations.
/// This parser only performs syntax validation and semantic analysis without
/// altering any state or requiring runtime dependencies.
/// </summary>
public class LSPMUSHCodeParser
{
	private readonly IMUSHCodeParser _parser;
	private readonly IMushCodeAnalyzer _analyzer;

	public LSPMUSHCodeParser(IMUSHCodeParser parser)
	{
		_parser = parser;
		// Diagnostics share a single source of truth with the in-server MCP tools.
		_analyzer = new MushCodeAnalyzer(parser);
	}

	/// <summary>
	/// Analyzes MUSH code and returns diagnostics (errors, warnings, etc.)
	/// This operation is read-only and does not alter parser state.
	/// </summary>
	public IReadOnlyList<Diagnostic> GetDiagnostics(string text, MushAnalysisMode mode = MushAnalysisMode.Function)
		=> _analyzer.Validate(text, mode);

	/// <summary>
	/// Performs semantic analysis and returns tokens for syntax highlighting.
	/// This operation is read-only and does not alter parser state.
	/// </summary>
	public SemanticTokensData GetSemanticTokens(string text, ParseType parseType = ParseType.Function)
	{
		try
		{
			return _parser.GetSemanticTokensData(MModule.single(text), parseType);
		}
		catch (Exception ex)
		{
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
	public bool ValidateSyntax(string text, MushAnalysisMode mode = MushAnalysisMode.Function)
	{
		var diagnostics = GetDiagnostics(text, mode);
		return diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
	}
}
