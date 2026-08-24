using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

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
	/// Reflows MUSH softcode to fit within <paramref name="width"/> columns, indenting nested
	/// calls by depth. Unlike <see cref="Format"/>, this does <b>not</b> preserve line count —
	/// it is a distinct, additive rendering built on <see cref="SoftcodeLayout.Compute"/>, the
	/// same layout engine <c>@examine</c>/<c>@grep</c> use, so a break is only ever inserted where
	/// that engine has proven it is safe (never inside a call whose contents are copied verbatim
	/// from source, such as <c>lit(...)</c>, and never before a closing delimiter). CRLF line
	/// endings are preserved at every inserted break, mirroring <see cref="Format"/>. Never throws.
	/// <para>
	/// Deliberately does <b>not</b> run <see cref="Format"/>'s cosmetic pass first: that pass edits
	/// whitespace by a blind per-line regex with no notion of "this comma is inside a <c>lit(...)</c>
	/// call," so composing it here would let a formatting request silently rewrite a source-copying
	/// call's literal contents (not just insert a break, an actual whitespace-inserting text edit).
	/// This method only ever touches whitespace at a break position — never elsewhere in token
	/// text — which is why the "non-whitespace characters are identical before and after" property
	/// holds byte-for-byte, including inside <c>lit(...)</c>/<c>localize(...)</c>.
	/// </para>
	/// </summary>
	/// <param name="code">The MUSH softcode to reflow.</param>
	/// <param name="width">Target line width in columns.</param>
	/// <param name="mode">
	/// The dialect to lay out as — same parameter and same default as <see cref="Validate"/>.
	/// <see cref="MushAnalysisMode.CommandList"/> treats a root <c>;</c> as a break position;
	/// every other mode does not (see <see cref="SoftcodeLayout.Compute"/>'s <c>parseType</c> doc).
	/// The Language Server passes <c>MushParseMode.ForFileName(uri)</c> so a <c>.mushcmd</c>
	/// document's semicolons actually break; a caller with no such signal keeps the conservative
	/// default.
	/// </param>
	string FormatIndented(string code, int width = 78, MushAnalysisMode mode = MushAnalysisMode.Function);

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
