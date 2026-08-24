using MarkupString.MarkupImplementation;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Tests.Formatting;

public class SemanticTokenRendererTests
{
	private static SemanticToken Tok(int start, string text, SemanticTokenType type) => new()
	{
		Range = new Range { Start = new Position(0, start), End = new Position(0, start + text.Length) },
		TokenType = type,
		Text = text
	};

	[Test]
	public async Task NoTokens_ReturnsSourceUnchanged()
	{
		var result = SemanticTokenRenderer.Render(MModule.single("add(1,2)"), []);
		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task PlainTextIsPreserved_WhenStylesApply()
	{
		var src = MModule.single("add(1,2)");
		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function), Tok(4, "1", SemanticTokenType.Number)]);

		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task StylesAreActuallyApplied()
	{
		var src = MModule.single("add(1,2)");
		var styled = SemanticTokenRenderer.Render(src, [Tok(0, "add(", SemanticTokenType.Function)]);

		await Assert.That(MModule.render("ansi", styled)).IsNotEqualTo(MModule.render("ansi", src));
	}

	[Test]
	public async Task OverrideTakesPrecedenceOverPalette()
	{
		var src = MModule.single("add(1,2)");
		var red = AnsiCodeParser.ParseCodes("r");
		var withOverride = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)], offset => offset < 4 ? red : null);
		var withoutOverride = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)]);

		await Assert.That(MModule.render("ansi", withOverride)).IsNotEqualTo(MModule.render("ansi", withoutOverride));
		await Assert.That(MModule.plainText(withOverride)).IsEqualTo("add(1,2)");
	}

	// "add(1,2)" offsets: a0 d1 d2 (3 1(4) ,(5) 2(6) )(7)
	private static readonly AnsiMarkup FunctionStyle =
		SemanticTokenAnsiPalette.GetStyle(SemanticTokenType.Function, SemanticTokenModifier.None)!;
	private static readonly AnsiMarkup NumberStyle =
		SemanticTokenAnsiPalette.GetStyle(SemanticTokenType.Number, SemanticTokenModifier.None)!;

	/// <summary>The <see cref="AnsiStructure"/> of the first <see cref="AnsiMarkup"/> covering
	/// <paramref name="offset"/>, or null if that offset carries no ansi markup.</summary>
	private static AnsiStructure? StyleDetailsAt(MString ms, int offset)
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
	public async Task OverrideAppliesToSubRangeOfSingleToken()
	{
		var src = MModule.single("add(1,2)");
		var red = AnsiCodeParser.ParseCodes("r");
		// Override only the middle 'd' (offset 1) of the "add(" Function token (offsets 0-3).
		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)], offset => offset == 1 ? red : null);

		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
		await Assert.That(StyleDetailsAt(result, 0)).IsEqualTo(FunctionStyle.Details);
		await Assert.That(StyleDetailsAt(result, 1)).IsEqualTo(red.Details);
		await Assert.That(StyleDetailsAt(result, 2)).IsEqualTo(FunctionStyle.Details);
		await Assert.That(StyleDetailsAt(result, 3)).IsEqualTo(FunctionStyle.Details);
	}

	[Test]
	public async Task OverrideAppliesToGapBetweenTokens()
	{
		var src = MModule.single("add(1,2)");
		var red = AnsiCodeParser.ParseCodes("r");
		// Tokens cover [0,4) and [6,7); the gap [4,6) is "1,". Override only the comma (offset 5).
		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function), Tok(6, "2", SemanticTokenType.Number)],
			offset => offset == 5 ? red : null);

		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
		await Assert.That(StyleDetailsAt(result, 4)).IsNull(); // '1' — untouched gap text
		await Assert.That(StyleDetailsAt(result, 5)).IsEqualTo(red.Details); // ',' — overridden gap text
		await Assert.That(StyleDetailsAt(result, 6)).IsEqualTo(NumberStyle.Details); // '2' — palette still applies
	}

	[Test]
	public async Task OverrideStraddlesTokenBoundary()
	{
		var src = MModule.single("add(1,2)");
		var red = AnsiCodeParser.ParseCodes("r");
		// Tokens are adjacent: [0,4) Function, [4,5) Number. Override offsets 3 and 4, which
		// straddles the boundary between the two tokens.
		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function), Tok(4, "1", SemanticTokenType.Number)],
			offset => offset is 3 or 4 ? red : null);

		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
		await Assert.That(StyleDetailsAt(result, 2)).IsEqualTo(FunctionStyle.Details);
		await Assert.That(StyleDetailsAt(result, 3)).IsEqualTo(red.Details);
		await Assert.That(StyleDetailsAt(result, 4)).IsEqualTo(red.Details);
	}

	[Test]
	public async Task OverlappingTokens_ClampsAndDoesNotDoubleEmit()
	{
		var src = MModule.single("add(1,2)");
		var overlapping = new SemanticToken
		{
			// Deliberately overlaps the "add(" Function token (offsets 0-3) at offsets 2-5.
			Range = new Range { Start = new Position(0, 2), End = new Position(0, 6) },
			TokenType = SemanticTokenType.Number,
			Text = "d(1,"
		};

		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function), overlapping]);

		// No duplicated characters from re-slicing the overlapped prefix.
		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
		// Offsets 2-3 stay owned by the first token; only the clamped remainder (4-5) of the
		// overlapping token is emitted, with its own style.
		await Assert.That(StyleDetailsAt(result, 2)).IsEqualTo(FunctionStyle.Details);
		await Assert.That(StyleDetailsAt(result, 3)).IsEqualTo(FunctionStyle.Details);
		await Assert.That(StyleDetailsAt(result, 4)).IsEqualTo(NumberStyle.Details);
		await Assert.That(StyleDetailsAt(result, 5)).IsEqualTo(NumberStyle.Details);
	}

	[Test]
	public async Task OverrideWithFreshButStructurallyIdenticalStyle_MergesIntoOneRun()
	{
		// Exactly the source's length so the single token covers [0, end) with no surrounding gap —
		// isolates the run count to what EmitStyledRuns produces for this one span.
		var src = MModule.single("add(");
		// AnsiCodeParser.ParseCodes allocates a fresh AnsiMarkup (and, for single-letter codes like
		// "r", a fresh backing byte[] inside AnsiColor.ANSI) on every call — the "obvious way to
		// write it" for a caller like Task 5's error-span override. If run-merging depended on
		// ReferenceEquals (round 1) or on AnsiColor.ANSI's pre-fix reference-equal array field, every
		// character here would re-fragment into its own run despite being visually identical.
		Func<int, AnsiMarkup?> freshRedEachCall = _ => AnsiCodeParser.ParseCodes("r");
		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)], freshRedEachCall);

		await Assert.That(MModule.plainText(result)).IsEqualTo("add(");
		// The closest honest proxy for "one run, not one per character": MarkupString.Runs itself.
		// Four structurally-identical-but-distinct-instance styles across four characters must still
		// collapse into a single AttributeRun.
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(StyleDetailsAt(result, 0)).IsEqualTo(AnsiCodeParser.ParseCodes("r").Details);
		await Assert.That(StyleDetailsAt(result, 3)).IsEqualTo(AnsiCodeParser.ParseCodes("r").Details);
	}
}
