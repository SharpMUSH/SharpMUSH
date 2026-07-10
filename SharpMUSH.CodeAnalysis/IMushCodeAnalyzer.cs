using SharpMUSH.Library.Models;

namespace SharpMUSH.CodeAnalysis;

/// <summary>
/// Stateless, read-only MUSH code intelligence used by both the Language Server
/// (for editors) and the in-server MCP tools (for AI agents / tooling).
///
/// A single source of truth for analysing MUSH softcode: every consumer maps the
/// plain-domain results returned here into its own protocol (LSP types, MCP JSON, …).
/// Implementations never mutate parser or world state.
/// </summary>
public interface IMushCodeAnalyzer
{
	/// <summary>
	/// Parses <paramref name="code"/> and returns any diagnostics (syntax errors,
	/// warnings, hints). Never throws: a parser failure is surfaced as a single
	/// error diagnostic so callers can always render a result.
	/// </summary>
	/// <param name="code">The MUSH softcode to analyse.</param>
	/// <param name="mode">
	/// How to parse the code. <see cref="MushAnalysisMode.CommandsPerLine"/> parses each line as
	/// its own command (real-world <c>.mush</c> files); the others parse the whole buffer as one
	/// unit.
	/// </param>
	IReadOnlyList<Diagnostic> Validate(string code, MushAnalysisMode mode = MushAnalysisMode.Function);

	/// <summary>
	/// Formats MUSH softcode with a consistent style: trims trailing/leading whitespace per
	/// line, ensures a space after a comma, and a space between an <c>@command</c> and its
	/// first argument. Line count is preserved. Never throws.
	/// </summary>
	string Format(string code);

	/// <summary>
	/// Returns hover information (function/command signature docs, or a built-in pattern
	/// explanation) for the word at the 0-based <paramref name="line"/>/<paramref name="character"/>,
	/// or null if there is nothing to show. Never throws.
	/// </summary>
	HoverInfo? Hover(string code, int line, int character);

	/// <summary>
	/// Returns completion suggestions (functions, commands, and common substitutions) for the
	/// word prefix at the 0-based <paramref name="line"/>/<paramref name="character"/>. Never throws.
	/// </summary>
	IReadOnlyList<CompletionSuggestion> Complete(string code, int line, int character);

	/// <summary>
	/// Returns signature help for the function call surrounding the 0-based
	/// <paramref name="line"/>/<paramref name="character"/>, or null if the position is not
	/// inside a known function call. Never throws.
	/// </summary>
	SignatureInfo? SignatureHelp(string code, int line, int character);

	/// <summary>
	/// Returns an outline of the softcode: attribute definitions, <c>@set</c> attributes,
	/// function calls, and commands. Never throws.
	/// </summary>
	IReadOnlyList<CodeSymbol> DocumentSymbols(string code);
}
