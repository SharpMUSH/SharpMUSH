using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

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
	/// <param name="parseType">Whether to parse the text as a function or command.</param>
	IReadOnlyList<Diagnostic> Validate(string code, ParseType parseType = ParseType.Function);
}
