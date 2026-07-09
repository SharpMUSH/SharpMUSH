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
public class MushCodeAnalyzer(IMUSHCodeParser parser) : IMushCodeAnalyzer
{
	public IReadOnlyList<Diagnostic> Validate(string code, ParseType parseType = ParseType.Function)
	{
		try
		{
			return parser.GetDiagnostics(MModule.single(code), parseType);
		}
		catch (Exception ex)
		{
			return
			[
				new Diagnostic
				{
					Range = new Range
					{
						Start = new Position(0, 0),
						End = new Position(0, code.Length)
					},
					Severity = DiagnosticSeverity.Error,
					Source = "SharpMUSH.CodeAnalysis",
					Message = $"Parser error: {ex.Message}"
				}
			];
		}
	}
}
