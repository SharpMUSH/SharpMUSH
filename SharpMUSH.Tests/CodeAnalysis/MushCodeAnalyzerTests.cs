using NSubstitute;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Tests.CodeAnalysis;

/// <summary>
/// Unit tests for <see cref="MushCodeAnalyzer"/>, the shared analysis wrapper used by
/// both the Language Server and the in-server MCP tools. The parser is mocked here:
/// these tests cover the analyzer's own responsibilities (passthrough + never-throw).
/// Real-parser diagnostic accuracy is covered by the Explicit MCP integration tests.
/// </summary>
public class MushCodeAnalyzerTests
{
	[Test]
	public async Task Validate_ReturnsDiagnosticsFromParser()
	{
		var expected = new Diagnostic
		{
			Range = new Range { Start = new Position(0, 0), End = new Position(0, 3) },
			Severity = DiagnosticSeverity.Error,
			Message = "unexpected token"
		};
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.GetDiagnostics(Arg.Any<MString>(), Arg.Any<ParseType>())
			.Returns([expected]);

		var analyzer = new MushCodeAnalyzer(parser);

		var result = analyzer.Validate("add(");

		await Assert.That(result.Count).IsEqualTo(1);
		await Assert.That(result[0].Message).IsEqualTo("unexpected token");
		await Assert.That(result[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
	}

	[Test]
	public async Task Validate_WhenParserThrows_ReturnsSingleErrorDiagnosticInsteadOfThrowing()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.GetDiagnostics(Arg.Any<MString>(), Arg.Any<ParseType>())
			.Returns(_ => throw new InvalidOperationException("boom"));

		var analyzer = new MushCodeAnalyzer(parser);

		var result = analyzer.Validate("add(1,2)");

		await Assert.That(result.Count).IsEqualTo(1);
		await Assert.That(result[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
		await Assert.That(result[0].Message).Contains("boom");
	}
}
