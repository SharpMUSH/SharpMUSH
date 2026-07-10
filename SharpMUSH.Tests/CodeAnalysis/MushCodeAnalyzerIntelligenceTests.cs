using NSubstitute;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.CodeAnalysis;

/// <summary>
/// Unit tests for the position-based code intelligence in <see cref="MushCodeAnalyzer"/>
/// (hover, completion, signature help, document symbols). A small in-memory function/command
/// library fixture stands in for the live world's libraries.
/// </summary>
public class MushCodeAnalyzerIntelligenceTests
{
	private static MushCodeAnalyzer AnalyzerWithLibraries()
	{
		var functions = new FunctionLibraryService
		{
			{
				"add",
				(new FunctionDefinition(
					new SharpFunctionAttribute { Name = "add", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular },
					_ => default), true)
			}
		};
		var commands = new CommandLibraryService
		{
			{
				"@foo",
				(new CommandDefinition(
					new SharpCommandAttribute { Name = "@foo", MinArgs = 0, MaxArgs = 1, Switches = ["BAR"] },
					_ => default), true)
			}
		};

		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FunctionLibrary.Returns(functions);
		parser.CommandLibrary.Returns(commands);
		return new MushCodeAnalyzer(parser);
	}

	private static MushCodeAnalyzer EmptyAnalyzer()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FunctionLibrary.Returns(new FunctionLibraryService());
		parser.CommandLibrary.Returns(new CommandLibraryService());
		return new MushCodeAnalyzer(parser);
	}

	// ── Hover ──────────────────────────────────────────────────────────────────

	[Test]
	public async Task Hover_OnPatternSubstitution_ExplainsIt()
	{
		var hover = EmptyAnalyzer().Hover("%#", 0, 0);

		await Assert.That(hover).IsNotNull();
		await Assert.That(hover!.Markdown).Contains("Current object");
	}

	[Test]
	public async Task Hover_OnUnknownWord_ReturnsNull()
	{
		var hover = EmptyAnalyzer().Hover("xyzzy", 0, 0);

		await Assert.That(hover).IsNull();
	}

	[Test]
	public async Task Hover_OnKnownFunction_ReturnsSignatureMarkdown()
	{
		var hover = AnalyzerWithLibraries().Hover("add(1,2)", 0, 1);

		await Assert.That(hover).IsNotNull();
		await Assert.That(hover!.Markdown).Contains("Function: `add`");
	}

	// ── Completion ───────────────────────────────────────────────────────────────

	[Test]
	public async Task Complete_WithFunctionPrefix_SuggestsMatchingFunction()
	{
		var completions = AnalyzerWithLibraries().Complete("ad", 0, 2);

		await Assert.That(completions.Select(c => c.Label)).Contains("add");
	}

	[Test]
	public async Task Complete_WithEmptyPrefix_IncludesCommonPatterns()
	{
		var labels = EmptyAnalyzer().Complete("", 0, 0).Select(c => c.Label).ToList();

		await Assert.That(labels).Contains("%#");
		await Assert.That(labels).Contains("%qa");
	}

	[Test]
	public async Task Complete_WithSubstitutionPrefix_KeepsFilteringSubstitutions()
	{
		// Typing "%q" should still narrow to %q… substitutions — the '%' must be part of the prefix.
		var labels = EmptyAnalyzer().Complete("%q", 0, 2).Select(c => c.Label).ToList();

		await Assert.That(labels).Contains("%qa");
		await Assert.That(labels).DoesNotContain("%#");
	}

	[Test]
	public async Task Complete_WhileTypingAtCommand_SuggestsMatchingCommands()
	{
		// A command prefix like "@fo" should offer commands even though the char before the
		// cursor isn't whitespace.
		var labels = AnalyzerWithLibraries().Complete("@fo", 0, 3).Select(c => c.Label).ToList();

		await Assert.That(labels).Contains("@foo");
	}

	[Test]
	public async Task DocumentSymbols_TrimsCarriageReturn_ForCrlfInput()
	{
		var symbols = EmptyAnalyzer().DocumentSymbols("&foo bar\r");

		var symbol = symbols.First(s => s.Name == "foo");
		await Assert.That(symbol.Range.End.Character).IsEqualTo("&foo bar".Length);
	}

	// ── Signature help ───────────────────────────────────────────────────────────

	[Test]
	public async Task SignatureHelp_InsideKnownCall_ReturnsSignatureWithActiveParameter()
	{
		var signature = AnalyzerWithLibraries().SignatureHelp("add(1,", 0, 6);

		await Assert.That(signature).IsNotNull();
		await Assert.That(signature!.Label).Contains("add(");
		await Assert.That(signature.Parameters.Count).IsEqualTo(2);
		await Assert.That(signature.ActiveParameter).IsEqualTo(1);
	}

	[Test]
	public async Task SignatureHelp_OutsideAnyCall_ReturnsNull()
	{
		var signature = AnalyzerWithLibraries().SignatureHelp("think hello", 0, 5);

		await Assert.That(signature).IsNull();
	}

	// ── Document symbols ─────────────────────────────────────────────────────────

	[Test]
	public async Task DocumentSymbols_FindsAttributeDefinition()
	{
		var symbols = EmptyAnalyzer().DocumentSymbols("&greeting think hello");

		await Assert.That(symbols.Select(s => s.Name)).Contains("greeting");
		await Assert.That(symbols.First(s => s.Name == "greeting").Kind).IsEqualTo("Property");
	}

	[Test]
	public async Task DocumentSymbols_FindsFunctionAndCommandAcrossLines()
	{
		var symbols = EmptyAnalyzer().DocumentSymbols("add(1,2)\n@pemit #1=hi");

		var function = symbols.FirstOrDefault(s => s.Name == "add");
		var command = symbols.FirstOrDefault(s => s.Name == "@pemit");

		await Assert.That(function).IsNotNull();
		await Assert.That(function!.Kind).IsEqualTo("Function");
		await Assert.That(command).IsNotNull();
		await Assert.That(command!.Kind).IsEqualTo("Method");
	}
}
