using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// The load-bearing proof of the formatter: a line break the layout engine inserts must never change
/// what the softcode does.
/// <para>
/// This is asserted by <em>evaluation</em>, not by comparing token streams. MUSH whitespace is literal
/// data almost everywhere — <c>VisitBeginGenericText</c> emits the raw token text with the whitespace
/// its lexer rule absorbed still in it — so a comparison that normalised trailing whitespace away
/// would normalise away exactly the defect this test exists to catch. Each corpus entry is run through
/// the real parser twice: as written, and after being laid out at each of several widths. The two
/// results must be identical strings.
/// </para>
/// </summary>
public class SoftcodeLayoutEquivalenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private static readonly int[] Widths = [20, 40, 78];

	/// <summary>
	/// Entries whose evaluation echoes their structure, so a stray newline shows up in the output.
	/// Guarded below to produce non-empty, non-error output: an entry evaluating to <c>""</c> or to
	/// <c>#-1 ...</c> would compare equal no matter what the formatter did to its innards.
	/// </summary>
	public static IEnumerable<Func<string>> Corpus() =>
	[
		// Fits flat at every width — the null case.
		() => "add(1,2)",

		// Nested arithmetic, long enough to break at 20 and 40 and stay flat at 78.
		() => "add(add(1,2),add(3,4),add(5,6),add(7,8),add(9,10),add(11,12))",

		// Deep nesting, so the indent clamp is exercised against a real evaluation.
		() => "add(1,add(2,add(3,add(4,add(5,add(6,add(7,8)))))))",

		// A wide argument list: every comma is a genuine separator and a candidate break.
		() => "strcat(alpha,bravo,charlie,delta,echo,foxtrot,golf,hotel,india,juliet)",

		// Nested strcat: breaks at several depths, and every character of every argument is echoed.
		() => "strcat(one,strcat(two,strcat(three,strcat(four,five))),six)",

		// A brace group containing a comma. That comma is data, not a separator.
		() => "strcat(a,b,{c,def})",

		// Brace groups suppressing and re-enabling function evaluation, long enough to break.
		() => "strcat(a,{add(1,2)},b,{[add(3,4)]},c,{a fairly long literal, with a comma},d)",

		// Brace atomicity with a matching switch, so the brace body reaches the output verbatim.
		() => "switch(1,1,{say a very long thing indeed, honestly},2,{other},none)",

		// A bracket sub-expression as the whole expression, long enough that OBRACK is a break position.
		() => "[strcat(alpha bravo,charlie delta,echo foxtrot,golf hotel,india juliet)]",

		// Bracket sub-expressions nested in an argument list, long enough that the inner one breaks.
		() => "strcat(a,[add(3,4)],b,[strcat(a long stretch of text here,and more of it)],z)",

		// Bracket groups at root with a comma in text position between them. Note this only parses at
		// root: inside a function's arguments the grammar rejects a comma in text position, so
		// `switch(a,[f(x),y],...)` (the Task 2 table's entry) is not valid softcode at all.
		() => "[strcat(a long stretch of text here,and more of it)],literal comma here,[add(1,2)]",

		// A name that resolves to no function is reproduced as text (PennMUSH: `think foo(bar)` prints
		// `foo(bar)`), and SharpMUSH copies its FUNCHAR/COMMAWS terminals verbatim from the source.
		() => "notafunction(aaaaaaaaaa,bbbbbbbbbb,cccccccccc)",

		// Literal commas in text position: prose commas are not separators (Task 2, Critical 1).
		() => "@emit A long line of prose, and more prose here, and yet more besides",

		// A bare parenthesis group is text, not structure (Task 2, Critical 2).
		() => "@emit a long parenthetical (with several words, inside it) and then more",

		// A mismatched closer inside braces must not pop the brace group (Task 2, round 2).
		() => "strcat(aaaa,{prose ) here, comma},b)",

		// The first ')' genuinely closes the call; the tail is root-level text.
		() => "strcat(aaaa, (bbbb) cccc, dddd)",

		// iter over a real list, with a bare parenthetical inside an argument.
		() => "iter(a b,a long chunk (with a parenthetical) of prose here ##,%b,%b)",

		// A stray closer at root, after a root-level semicolon that is literal here.
		() => "aaaaaaaaaaaaaaaaaaaa;)",

		// A literal newline already in the source resets the column (attributes have held them since PR #775).
		() => "aaaaaaaaaaaaaaaaaaaaaaaaaa\n[switch(1,1,matched,unmatched)]"
	];

	/// <summary>
	/// The Task 2 corpus table verbatim, including entries whose output is empty, an error, or a parser
	/// failure. Those cannot prove much on their own — the echoing variants above are what carry the
	/// proof — but they must still evaluate identically before and after formatting, and must not throw.
	/// </summary>
	public static IEnumerable<Func<string>> EdgeCorpus() =>
	[
		() => "f(aaaa,{prose ) here, comma},b)",
		() => "f(aaaa, (bbbb) cccc, dddd)",
		() => "switch(a,[ansi(hr,a long stretch of text),y],{b,c},trailing prose, and more)",
		() => "switch(%0,1,{say a very long thing indeed, honestly},2,{other})",
		() => "iter(%0,a long chunk (with a parenthetical) of prose here,%b,%b)",
		() => "aaaaaaaaaaaaaaaaaaaaaaaaaa\n[switch(1,a,b)]",
		() => "switch(a,b,c",
		() => "a,b,c)))",
		() => "add(1,add(1,add(1,add(1,5)))"
	];

	private async Task<string?> Eval(string code)
		=> (await Parser.FunctionParse(MModule.single(code)))?.Message?.ToString();

	private async Task AssertFormattingPreservesEvaluation(string source, string? expected)
	{
		foreach (var width in Widths)
		{
			var formatted = SoftcodeRenderer.Format(source, width);
			var actual = await Eval(formatted);

			await Assert.That(actual).IsEqualTo(expected)
				.Because($"width {width} changed what [{source}] evaluates to. Formatted:\n{formatted}");
		}
	}

	[Test]
	[MethodDataSource(nameof(Corpus))]
	public async Task Formatting_PreservesEvaluatedOutput(string source)
	{
		var expected = await Eval(source);

		// An entry evaluating to nothing, or to an error that swallows its arguments, would compare
		// equal however badly the formatter mangled it. Fail loudly rather than pass vacuously.
		await Assert.That(string.IsNullOrEmpty(expected)).IsFalse()
			.Because($"corpus entry [{source}] evaluates to nothing, so it proves nothing");
		await Assert.That(expected!.StartsWith("#-1")).IsFalse()
			.Because($"corpus entry [{source}] evaluates to an error, so it proves nothing: {expected}");

		await AssertFormattingPreservesEvaluation(source, expected);
	}

	[Test]
	[MethodDataSource(nameof(EdgeCorpus))]
	public async Task Formatting_PreservesEvaluatedOutput_ForEdgeCases(string source)
	{
		await AssertFormattingPreservesEvaluation(source, await Eval(source));
	}

	/// <summary>
	/// The comparison is only sound if evaluating the same source twice gives the same answer, so this
	/// pins that no corpus entry is time-, random- or state-dependent.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(Corpus))]
	public async Task CorpusEntry_EvaluatesDeterministically(string source)
	{
		var first = await Eval(source);
		var second = await Eval(source);

		await Assert.That(second).IsEqualTo(first).Because($"[{source}] is not deterministic");
	}

	/// <summary>
	/// Ruling 7: <c>OBRACK</c> is a break position on probation, settled by whether the corpus survives
	/// it. That verdict is worthless if no corpus entry ever breaks at a <c>[</c>, so this asserts the
	/// corpus actually exercises the position it is meant to be judging.
	/// </summary>
	[Test]
	public async Task Corpus_ActuallyBreaksAfterAnOpenBracket()
	{
		var exercised = new List<string>();
		foreach (var source in Corpus().Concat(EdgeCorpus()).Select(entry => entry()))
		{
			var tokens = TestLexer.Lex(source);
			foreach (var width in Widths)
			{
				if (SoftcodeLayout.Compute(tokens, width).Any(b => tokens[b.TokenIndex].Type == "OBRACK"))
				{
					exercised.Add($"{source} @ {width}");
				}
			}
		}

		Console.WriteLine("OBRACK break positions exercised by the corpus:");
		foreach (var entry in exercised)
		{
			Console.WriteLine($"  {entry}");
		}

		await Assert.That(exercised).IsNotEmpty()
			.Because("no corpus entry breaks after '[', so the equivalence run says nothing about OBRACK");
	}

	/// <summary>
	/// Ruling 7, with only one variable. Every other corpus entry that breaks after a <c>[</c> also
	/// breaks after a FUNCHAR or a COMMAWS, so a difference could not be attributed. Here the bracket
	/// group is the only group, its comma is prose (its enclosing opener is OBRACK, not FUNCHAR), and
	/// the layout therefore emits exactly one break — the one after <c>[</c>.
	/// </summary>
	[Test]
	public async Task OpenBracket_IsSafeWhenItIsTheOnlyBreak()
	{
		const string source = "[aaaaaaaaaaaa,bbbbbbbbbbbb]";
		var tokens = TestLexer.Lex(source);
		var breaks = SoftcodeLayout.Compute(tokens, width: 20);

		await Assert.That(breaks).Count().IsEqualTo(1);
		await Assert.That(tokens[breaks[0].TokenIndex].Type).IsEqualTo("OBRACK");

		var formatted = SoftcodeRenderer.Format(source, width: 20);
		await Assert.That(formatted).IsNotEqualTo(source);
		await Assert.That(await Eval(formatted)).IsEqualTo(await Eval(source))
			.Because($"breaking after '[' changed evaluation. Formatted:\n{formatted}");
	}

	/// <summary>
	/// Task 2, Critical 1: a comma in text position is prose. Its absorbed whitespace is literal data,
	/// so the formatter must emit no break at all here, at any width.
	/// </summary>
	[Test]
	public async Task ProseCommas_ProduceNoBreakAndNoTextChange()
	{
		const string source = "@emit A long line of prose, and more prose here";
		var tokens = TestLexer.Lex(source);

		foreach (var width in Widths)
		{
			await Assert.That(SoftcodeLayout.Compute(tokens, width)).IsEmpty()
				.Because($"width {width} broke at a prose comma");
			await Assert.That(SoftcodeRenderer.Format(source, width)).IsEqualTo(source);
		}

		await Assert.That(await Eval(source)).IsEqualTo(source);
	}

	/// <summary>
	/// Task 2, Critical 2: a bare <c>(</c> reaches the parser through <c>beginGenericText</c> and is
	/// text, so it neither opens a group nor licenses a break. Two content tokens sit inside the parens
	/// so that treating <c>(</c> as an opener would produce a break rather than being masked by the
	/// empty-group guard.
	/// </summary>
	[Test]
	public async Task BareParenthesisGroup_ProducesNoBreakAndNoTextChange()
	{
		const string source = "@emit a long parenthetical (with several words, inside it) and then more";
		var tokens = TestLexer.Lex(source);

		await Assert.That(tokens.Select(t => t.Type)).Contains("OPAREN");

		foreach (var width in Widths)
		{
			await Assert.That(SoftcodeLayout.Compute(tokens, width)).IsEmpty()
				.Because($"width {width} broke at a bare parenthesis");
			await Assert.That(SoftcodeRenderer.Format(source, width)).IsEqualTo(source);
		}

		await Assert.That(await Eval(source)).IsEqualTo(source);
	}
}
