using MarkupString.MarkupImplementation;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// <see cref="SoftcodeFormatter"/> composes <see cref="SoftcodeLayout"/> (Task 2/3) and
/// <see cref="SemanticTokenRenderer"/> (Task 4); it owns no highlighting or layout logic of its own.
/// These tests exercise the composition — round-tripping, character preservation, break insertion and
/// the error summary — not the rules those two services already have their own test suites for.
/// <para>
/// A real <see cref="IMUSHCodeParser"/> is required (via <see cref="ServerWebAppFactory"/>) because
/// <see cref="SoftcodeFormatter.Format"/> builds its classifier from
/// <see cref="SoftcodeLayout.ClassifierFor"/>, which needs the real function library to tell an
/// evaluating call from a source-copying one — <c>SoftcodeLayoutEquivalenceTests</c> relies on the same
/// fixture for the same reason.
/// </para>
/// </summary>
public class SoftcodeFormatterTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private MString Format(string src, IReadOnlyList<SemanticToken>? sem = null,
		IReadOnlyList<ParseError>? errors = null, int width = 78)
		=> SoftcodeFormatter.Format(MModule.single(src), TestLexer.Lex(src),
			sem ?? [], errors ?? [], width, Parser);

	[Test]
	public async Task PlainText_RoundTripsUnchanged()
	{
		var result = Format("add(1,2)");
		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task LongInput_GainsNewlines()
	{
		var result = Format("switch(words(%0),0,nothing at all,1,just one,many here)", width: 30);
		await Assert.That(MModule.plainText(result)).Contains("\n");
	}

	[Test]
	public async Task NoCharactersAreLost_EvenWithoutSemanticTokens()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many here)";
		var result = MModule.plainText(Format(src, width: 30));

		static string Strip(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
		await Assert.That(Strip(result)).IsEqualTo(Strip(src));
	}

	/// <summary>
	/// <see cref="ParseError.ToMushFailureString"/> reports "end of expression" rather than a numbered
	/// position whenever <see cref="ParseError.Column"/> sits at or past the end of the line in
	/// <see cref="ParseError.InputText"/> — including when <c>InputText</c> is left unset. This error's
	/// <c>InputText</c> is deliberately longer than "add(1,2" so column 7 lands mid-line and the summary
	/// exercises the numbered-position branch, which is the behaviour <c>@examine</c> callers see for a
	/// typical unterminated call reported against its enclosing attribute body.
	/// </summary>
	[Test]
	public async Task ErrorSummary_IsAppendedBeneathTheCode()
	{
		var errors = new[]
		{
			new ParseError
			{
				Line = 1, Column = 7, Message = "mismatched input",
				OffendingToken = ")", ExpectedTokens = ["COMMAWS", "CPAREN"],
				InputText = "add(1,2) trailing text so column 7 is not at the end of the line"
			}
		};

		var result = MModule.plainText(Format("add(1,2", errors: errors));

		// Pins both placement (the summary starts on the line right after the code, not merely
		// "somewhere in the output") and content (byte-identical to ToMushFailureString(), never a
		// hand-rolled format) in one assertion, rather than the weaker Contains("position 7") the
		// brief originally proposed.
		await Assert.That(result).Contains("add(1,2");
		await Assert.That(result).Contains("\n" + errors[0].ToMushFailureString());
	}

	/// <summary>
	/// Whether <paramref name="offset"/> carries an <c>AnsiMarkup</c> — the same shape as
	/// <c>SemanticTokenRendererTests.StyleDetailsAt</c>, simplified to a bool. A run covering the offset
	/// is not by itself proof of an override hit: <c>MString</c> carries a run over every character
	/// (e.g. the default/absent markup <c>MModule.single</c> assigns), so the check has to look inside
	/// the run's <c>Markups</c> for an actual <see cref="AnsiMarkup"/> rather than merely finding a run.
	/// </summary>
	private static bool IsStyled(MString ms, int offset)
	{
		foreach (var run in ms.Runs)
		{
			if (offset < run.Start || offset >= run.Start + run.Length)
			{
				continue;
			}

			return run.Markups.Any(markup => markup is AnsiMarkup);
		}

		return false;
	}

	/// <summary>
	/// Pins <see cref="SoftcodeFormatter"/>'s own offset math (converting a <see cref="ParseError"/>'s
	/// 1-based line / 0-based column plus <see cref="ParseError.OffendingToken"/> length into an
	/// absolute span) rather than trusting <c>SemanticTokenRenderer</c>'s own tests, which only ever
	/// exercise hand-written offsets and never <see cref="SoftcodeFormatter"/>'s conversion of them. An
	/// off-by-one here would paint the wrong characters while every other test in this file stayed
	/// green, since none of them inspect which characters are styled.
	/// <para>
	/// "add(1,2,3)" indices: a0 d1 d2 (3 1(4) ,(5) 2(6) ,(7) 3(8) )(9). The error's
	/// <see cref="ParseError.OffendingToken"/> "1,2" starts at <see cref="ParseError.Column"/> 4 and
	/// spans exactly 3 characters, so the styled span is [4, 7) — offsets 3 and 7 must fall outside it.
	/// </para>
	/// </summary>
	[Test]
	public async Task ErrorOverride_StylesExactlyTheOffendingTokenSpan()
	{
		const string src = "add(1,2,3)";
		var errors = new[] { new ParseError { Line = 1, Column = 4, Message = "x", OffendingToken = "1,2" } };

		var result = Format(src, errors: errors);

		await Assert.That(IsStyled(result, 3)).IsFalse().Because("offset 3 ('(') is before Column");
		await Assert.That(IsStyled(result, 4)).IsTrue();
		await Assert.That(IsStyled(result, 5)).IsTrue();
		await Assert.That(IsStyled(result, 6)).IsTrue();
		await Assert.That(IsStyled(result, 7)).IsFalse().Because("offset 7 is Column + OffendingToken.Length");
	}

	/// <summary>
	/// The <c>OffendingToken?.Length ?? 1</c> fallback: with no offending token at all, exactly one
	/// character is styled at <see cref="ParseError.Column"/>, not the whole rest of the source and not
	/// zero characters.
	/// </summary>
	[Test]
	public async Task ErrorOverride_WithNoOffendingToken_StylesExactlyOneCharacter()
	{
		const string src = "add(1,2,3)";
		var errors = new[] { new ParseError { Line = 1, Column = 4, Message = "x" } };

		var result = Format(src, errors: errors);

		await Assert.That(IsStyled(result, 3)).IsFalse();
		await Assert.That(IsStyled(result, 4)).IsTrue();
		await Assert.That(IsStyled(result, 5)).IsFalse().Because("no OffendingToken means a length-1 span");
	}

	/// <summary>
	/// Exercises the <c>Line - 1</c> conversion and the line-start table, which a single-line test
	/// cannot touch at all. Three lines, with the error on the (non-last) second one, so a bug that
	/// forgets to subtract 1 can't hide behind <c>Math.Clamp</c> quietly saving the last-line case.
	/// <para>
	/// "aaaa\nbbbb(1,2)\ncccc" indices: a0 a1 a2 a3 \n4 b5 b6 b7 b8 (9 1(10) ,(11) 2(12) )(13) \n14 c15..18.
	/// Line 2 (1-based) starts at absolute offset 5; column 5 within it lands on '1' at offset 10.
	/// </para>
	/// </summary>
	[Test]
	public async Task ErrorOverride_OnTheSecondLine_ConvertsLineAndColumnCorrectly()
	{
		const string src = "aaaa\nbbbb(1,2)\ncccc";
		var errors = new[] { new ParseError { Line = 2, Column = 5, Message = "x", OffendingToken = "1,2" } };

		var result = Format(src, errors: errors);

		await Assert.That(IsStyled(result, 9)).IsFalse().Because("offset 9 ('(') is before the span");
		await Assert.That(IsStyled(result, 10)).IsTrue();
		await Assert.That(IsStyled(result, 11)).IsTrue();
		await Assert.That(IsStyled(result, 12)).IsTrue();
		await Assert.That(IsStyled(result, 13)).IsFalse().Because("offset 13 (')') is past the span");
	}

	/// <summary>Boundary case: an error at the very start of the source, offset 0.</summary>
	[Test]
	public async Task ErrorOverride_AtOffsetZero_StylesTheFirstCharacter()
	{
		const string src = "add(1,2)";
		var errors = new[] { new ParseError { Line = 1, Column = 0, Message = "x", OffendingToken = "a" } };

		var result = Format(src, errors: errors);

		await Assert.That(IsStyled(result, 0)).IsTrue();
		await Assert.That(IsStyled(result, 1)).IsFalse();
	}

	[Test]
	public async Task NoErrors_AppendsNoSummary()
	{
		var result = MModule.plainText(Format("add(1,2)"));
		await Assert.That(result.Split('\n')).Count().IsEqualTo(1);
	}

	[Test]
	public async Task EmptyInput_ReturnsEmpty()
	{
		var result = SoftcodeFormatter.Format(MModule.empty(), [], [], [], 78, Parser);
		await Assert.That(MModule.plainText(result)).IsEqualTo("");
	}
}
