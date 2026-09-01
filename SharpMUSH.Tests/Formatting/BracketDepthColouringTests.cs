using MarkupString.MarkupImplementation;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// Bracket-depth colouring end to end: <see cref="SemanticTokenAnsiPalette.GetBracketDepthStyle"/>
/// supplies the colours, <see cref="SoftcodeLayout.ComputeDelimiterDepths"/> the offsets, and
/// <see cref="SoftcodeFormatter"/> composes them into the per-offset override
/// <see cref="SemanticTokenRenderer"/> already consults.
/// <para>
/// A real <see cref="IMUSHCodeParser"/> is required for the same reason
/// <c>SoftcodeFormatterTests</c> needs one: the classifier that tells a source-copying call from an
/// evaluating one comes from the real function library.
/// </para>
/// </summary>
public class BracketDepthColouringTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private MString Format(string src, IReadOnlyList<ParseError>? errors = null)
		=> SoftcodeFormatter.Format(MModule.single(src), TestLexer.Lex(src), [], errors ?? [], 78, Parser);

	private static AnsiStructure? StyleAt(MString ms, int offset)
	{
		foreach (var run in ms.Runs)
		{
			if (offset < run.Start || offset >= run.Start + run.Length)
				continue;
			foreach (var markup in run.Markups)
				if (markup is AnsiMarkup ansi)
					return ansi.Details;
			return null;
		}

		return null;
	}

	[Test]
	public async Task AdjacentDepths_GetDifferentColours()
	{
		await Assert.That(SemanticTokenAnsiPalette.GetBracketDepthStyle(0).Details)
			.IsNotEqualTo(SemanticTokenAnsiPalette.GetBracketDepthStyle(1).Details);
	}

	[Test]
	public async Task DepthsCycle_SoArbitrarilyDeepNestingStillHasAColour()
	{
		var first = SemanticTokenAnsiPalette.GetBracketDepthStyle(0).Details;
		var cycled = SemanticTokenAnsiPalette.GetBracketDepthStyle(SemanticTokenAnsiPalette.BracketDepthColorCount).Details;

		await Assert.That(cycled).IsEqualTo(first);
	}

	// "add(sub(1,2),3)" offsets: a0 d1 d2 (3 s4 u5 b6 (7 1:8 ,9 2:10 )11 ,12 3:13 )14
	[Test]
	public async Task MatchingParens_ShareOneColour()
	{
		var result = Format("add(sub(1,2),3)");

		await Assert.That(StyleAt(result, 3)).IsEqualTo(StyleAt(result, 14));
		await Assert.That(StyleAt(result, 7)).IsEqualTo(StyleAt(result, 11));
	}

	[Test]
	public async Task NestedParens_DifferFromTheirParent()
	{
		var result = Format("add(sub(1,2),3)");

		await Assert.That(StyleAt(result, 3)).IsNotEqualTo(StyleAt(result, 7));
	}

	[Test]
	public async Task PlainTextSurvivesColouring()
	{
		await Assert.That(MModule.plainText(Format("add(sub(1,2),3)"))).IsEqualTo("add(sub(1,2),3)");
	}

	// @"ljust(%b\[%b[left(%0)]%b\]%b,%1)" offsets: '(' 5, '\[' 8-9, '[' 12, '(' 17, ')' 20, ']' 21,
	// '\]' 24-25, ')' 31.
	[Test]
	public async Task EscapedBracket_IsNotColouredAsADelimiter()
	{
		var result = Format(@"ljust(%b\[%b[left(%0)]%b\]%b,%1)");

		// Null, not merely "different from the real bracket": this Format overload passes no semantic
		// tokens, so the depth override is the only thing that can paint anything here. Asserting a
		// difference would still pass if '\[' were painted some *other* depth's colour, which is the
		// exact defect a lexical matcher produces -- it pairs '\[' with the ']' at 21 and shifts every
		// depth after it.
		await Assert.That(StyleAt(result, 9)).IsNull();
		await Assert.That(StyleAt(result, 24)).IsNull();
		await Assert.That(StyleAt(result, 12)).IsEqualTo(StyleAt(result, 21));
	}

	/// <summary>
	/// A parse error is painted in inverse red on top of everything else. Bracket colouring must not
	/// displace it — an unbalanced bracket is precisely the case where both want the same character.
	/// </summary>
	[Test]
	public async Task ErrorHighlight_StillWinsOverBracketColour()
	{
		var errors = new[]
		{
			new ParseError { Line = 1, Column = 3, Message = "boom", OffendingToken = "(" }
		};

		var withError = Format("add(sub(1,2),3)", errors);
		var without = Format("add(sub(1,2),3)");

		await Assert.That(StyleAt(withError, 3)).IsNotEqualTo(StyleAt(without, 3));
	}
}
