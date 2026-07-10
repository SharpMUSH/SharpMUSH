using System.Text.RegularExpressions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using MModule = MarkupString.MarkupStringModule;
using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.CodeAnalysis;

/// <summary>
/// Default <see cref="IMushCodeAnalyzer"/> implementation: a thin, exception-safe
/// wrapper over the live <see cref="IMUSHCodeParser"/>. When resolved from the game
/// server's container the parser carries the real function/command libraries, so the
/// analysis reflects the running world.
/// </summary>
public partial class MushCodeAnalyzer(IMUSHCodeParser parser) : IMushCodeAnalyzer
{
	public string Format(string code)
	{
		// Preserve the document's newline style so formatting a CRLF file doesn't silently
		// rewrite every line ending to LF (a huge, confusing diff in Windows editors).
		var newline = code.Contains("\r\n") ? "\r\n" : "\n";
		var lines = code.Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			lines[i] = FormatLine(lines[i]);
		}

		return string.Join(newline, lines);
	}

	private static string FormatLine(string line)
	{
		var formatted = NormalizeSpacing(line.TrimEnd());
		var trimmed = formatted.TrimStart();
		return string.IsNullOrEmpty(trimmed) ? string.Empty : trimmed;
	}

	private static string NormalizeSpacing(string line)
	{
		var result = CommaWithoutSpaceRegex().Replace(line, ", ");
		result = CommandWithoutSpaceRegex().Replace(result, "$1 $2");
		return result;
	}

	[GeneratedRegex(@",(?!\s)")]
	private static partial Regex CommaWithoutSpaceRegex();

	[GeneratedRegex(@"^(@[a-zA-Z]+)([^\s/])")]
	private static partial Regex CommandWithoutSpaceRegex();

	public IReadOnlyList<Diagnostic> Validate(string code, MushAnalysisMode mode = MushAnalysisMode.Function)
		=> mode == MushAnalysisMode.CommandsPerLine
			? ValidatePerLine(code)
			: ValidateWhole(code, mode.ToParseType());

	private IReadOnlyList<Diagnostic> ValidateWhole(string code, ParseType parseType)
	{
		try
		{
			return parser.GetDiagnostics(MModule.single(code), parseType);
		}
		catch (Exception ex)
		{
			// Anchor the fallback range to a valid position: the end lands on the last line
			// (so a multi-line buffer doesn't produce a character offset past line 0), and a
			// trailing CR is excluded so CRLF input isn't off-by-one.
			var lines = code.Split('\n');
			var lastLine = lines.Length - 1;
			var lastLineLength = lines[lastLine].TrimEnd('\r').Length;

			return
			[
				new Diagnostic
				{
					Range = new Range
					{
						Start = new Position(0, 0),
						End = new Position(lastLine, lastLineLength)
					},
					Severity = DiagnosticSeverity.Error,
					Source = "SharpMUSH.CodeAnalysis",
					Message = $"Parser error: {ex.Message}"
				}
			];
		}
	}

	/// <summary>
	/// Real-world <c>.mush</c> files are one command per line. Each non-blank line is parsed as a
	/// single <see cref="ParseType.Command"/> and its diagnostics are shifted to the line's
	/// position in the file.
	/// </summary>
	private IReadOnlyList<Diagnostic> ValidatePerLine(string code)
	{
		var lines = code.Split('\n');
		var result = new List<Diagnostic>();

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i].TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line)) continue;

			foreach (var diagnostic in ValidateWhole(line, ParseType.Command))
			{
				result.Add(ShiftLines(diagnostic, i));
			}
		}

		return result;
	}

	private static Diagnostic ShiftLines(Diagnostic diagnostic, int lineOffset)
		=> lineOffset == 0
			? diagnostic
			: diagnostic with
			{
				Range = new Range
				{
					Start = new Position(diagnostic.Range.Start.Line + lineOffset, diagnostic.Range.Start.Character),
					End = new Position(diagnostic.Range.End.Line + lineOffset, diagnostic.Range.End.Character)
				}
			};
}
