using NSubstitute;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Tests.CodeAnalysis;

/// <summary>
/// Unit tests for <see cref="MushAnalysisMode.CommandsPerLine"/> — how a real-world .mush file
/// (one command per line) is validated. The parser is mocked so the split / per-line-offset /
/// blank-line-skip behaviour is deterministic.
/// </summary>
public class MushCodeAnalyzerPerLineTests
{
	/// <summary>A parser that flags every line it is given with an error at characters 0-1.</summary>
	private static MushCodeAnalyzer AnalyzerFlaggingEveryLine()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.GetDiagnostics(Arg.Any<MString>(), Arg.Any<ParseType>())
			.Returns(_ =>
			[
				new Diagnostic
				{
					Range = new Range { Start = new Position(0, 0), End = new Position(0, 1) },
					Severity = DiagnosticSeverity.Error,
					Message = "bad line"
				}
			]);
		return new MushCodeAnalyzer(parser);
	}

	[Test]
	public async Task CommandsPerLine_ProducesOneDiagnosticPerNonBlankLine_OffsetToItsLine()
	{
		var result = AnalyzerFlaggingEveryLine().Validate("aaa\nbbb\nccc", MushAnalysisMode.CommandsPerLine);

		await Assert.That(result.Count).IsEqualTo(3);
		await Assert.That(result.Select(d => d.Range.Start.Line)).IsEquivalentTo(new[] { 0, 1, 2 });
	}

	[Test]
	public async Task CommandsPerLine_SkipsBlankLines()
	{
		var result = AnalyzerFlaggingEveryLine().Validate("aaa\n\n   \nddd", MushAnalysisMode.CommandsPerLine);

		// Only lines 0 and 3 are non-blank.
		await Assert.That(result.Count).IsEqualTo(2);
		await Assert.That(result.Select(d => d.Range.Start.Line)).IsEquivalentTo(new[] { 0, 3 });
	}

	[Test]
	public async Task CommandsPerLine_ParsesEachLineAsSingleCommand()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.GetDiagnostics(Arg.Any<MString>(), Arg.Any<ParseType>()).Returns([]);
		var analyzer = new MushCodeAnalyzer(parser);

		analyzer.Validate("line one\nline two", MushAnalysisMode.CommandsPerLine);

		// Two non-blank lines → two parses, each as ParseType.Command.
		parser.Received(2).GetDiagnostics(Arg.Any<MString>(), ParseType.Command);
	}
}
