using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// The load-bearing proof of the formatter: a line break the layout engine inserts must never change
/// what the softcode does.
/// <para>
/// This is asserted by <em>evaluation</em>, not by comparing token streams. MUSH whitespace is literal
/// data almost everywhere — <c>VisitBeginGenericText</c> emits the raw token text with the whitespace
/// its lexer rule absorbed still in it — so a comparison that normalised trailing whitespace away
/// would normalise away exactly the defect this test exists to catch. Each corpus entry is run through
/// the real parser twice: as written, and after being laid out at each of several widths.
/// </para>
/// <para>
/// There are three corpora and they carry <b>different strengths of claim</b>. Read the summary on
/// each before adding an entry to it:
/// <list type="number">
/// <item><description>
/// <see cref="ParseableEchoingCorpus"/> — the real proof. Parses, and evaluates to something a stray
/// newline would visibly damage. Guarded, and compared as exact strings.
/// </description></item>
/// <item><description>
/// <see cref="ParseableWeakOutputCorpus"/> — parses, but evaluates to nothing much. Still compared as
/// exact strings; just cannot prove as much on its own.
/// </description></item>
/// <item><description>
/// <see cref="UnparseableCorpus"/> — does not parse. Only the <em>parse outcome</em> is compared, not
/// the message, because a parser failure quotes a source offset and excerpt that any reformatting
/// necessarily moves. Both entry and exit from this group are policed by assertions.
/// </description></item>
/// </list>
/// </para>
/// </summary>
public class SoftcodeLayoutEquivalenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser? _oracleParser;

	/// <summary>
	/// One parser instance, reused for name resolution. <see cref="ServerWebAppFactory.FunctionParser"/>
	/// builds a fresh parser on every access, which is what keeps <see cref="Eval"/> deterministic, but
	/// the oracle only reads the function library and does not want the cost.
	/// </summary>
	private IMUSHCodeParser OracleParser => _oracleParser ??= WebAppFactoryArg.FunctionParser;

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private static readonly int[] Widths = [20, 40, 78];

	/// <summary>
	/// The oracle Ruling 9 requires, backed by the real parser and following
	/// <c>SharpMUSHParserVisitor.CallFunction</c>'s resolution order exactly: the parser's
	/// <c>FunctionLibrary</c> first (a <c>FunctionLibraryService</c>, so <c>OrdinalIgnoreCase</c>),
	/// then the <c>@function</c> registry. Deliberately a plain lookup rather than a case-folded one,
	/// so that if the library ever stopped being case-insensitive this would resolve fewer names and
	/// the formatter would break less — never more.
	/// </summary>
	private bool IsKnownFunction(string name)
		=> OracleParser.FunctionLibrary.ContainsKey(name)
			 || OracleParser.ServiceProvider.GetService<IUserDefinedFunctionService>()?.Resolve(name) is not null;

	/// <summary>
	/// Parses, and evaluates to something a stray newline would visibly damage. Guarded below to
	/// produce non-empty, non-error output: an entry evaluating to <c>""</c> or to an argument-
	/// swallowing <c>#-1</c> would compare equal no matter what the formatter did to its innards.
	/// </summary>
	public static IEnumerable<Func<string>> ParseableEchoingCorpus() =>
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

		// Ruling 9. A name that resolves to no function is reproduced as text — PennMUSH prints
		// `foo(bar)` for `think foo(bar)` — and SharpMUSH's LiteralFunctionCall slices its
		// FUNCHAR/COMMAWS/CPAREN terminals verbatim, absorbed whitespace included. No break is safe.
		() => "notafunction(aaaaaaaaaa,bbbbbbbbbb,cccccccccc)",

		// Ruling 9, propagation inward: _suppressFunctionEval makes every call inside an unresolved
		// one literal too, so `strcat` here is text and must not be broken either.
		() => "notafunction(aaaa,strcat(bbbbbbbbbb,cccccccccc),dddd)",

		// Ruling 9, and the one exception to it: VisitBracketPattern clears the suppression, so the
		// bracketed call really is dispatched and really is safe to break.
		() => "notafunction(aaaa,[strcat(bbbbbbbbbb,cccccccccc)],dddd)",

		// Ruling 9, outward: a resolved call is not tainted by an unresolved one among its arguments.
		() => "strcat(aaaa,notafunction(bbbbbbbbbb,cccccccccc),dddd)",

		// Literal commas in text position: prose commas are not separators (Task 2, Critical 1).
		() => "@emit A long line of prose, and more prose here, and yet more besides",

		// A bare parenthesis group is text, not structure (Task 2, Critical 2).
		() => "@emit a long parenthetical (with several words, inside it) and then more",

		// A mismatched closer inside braces must not pop the brace group (Task 2, round 2).
		() => "strcat(aaaa,{prose ) here, comma},b)",
		() => "f(aaaa,{prose ) here, comma},b)",

		// The first ')' genuinely closes the call; the tail is root-level text.
		() => "strcat(aaaa, (bbbb) cccc, dddd)",
		() => "f(aaaa, (bbbb) cccc, dddd)",

		// iter over a real list, with a bare parenthetical inside an argument.
		() => "iter(a b,a long chunk (with a parenthetical) of prose here ##,%b,%b)",

		// A stray closer at root, after a root-level semicolon that is literal under FunctionParse.
		() => "aaaaaaaaaaaaaaaaaaaa;)",

		// Stray closers at root, and root-level commas that are prose.
		() => "a,b,c)))",

		// A literal newline already in the source resets the column (attributes have held them
		// since PR #775).
		() => "aaaaaaaaaaaaaaaaaaaaaaaaaa\n[switch(1,1,matched,unmatched)]"
	];

	/// <summary>
	/// Parses, but evaluates to nothing or to an error that swallows its arguments — a formatter could
	/// mangle the innards without the output moving. Held to the same exact-string comparison anyway,
	/// because they are cheap and they must not throw; they simply cannot carry the proof themselves.
	/// From the Task 2 corpus table, which wrote them against an executor that has no <c>%0</c>.
	/// </summary>
	public static IEnumerable<Func<string>> ParseableWeakOutputCorpus() =>
	[
		() => "switch(%0,1,{say a very long thing indeed, honestly},2,{other})",
		() => "iter(%0,a long chunk (with a parenthetical) of prose here,%b,%b)",
		() => "aaaaaaaaaaaaaaaaaaaaaaaaaa\n[switch(1,a,b)]"
	];

	/// <summary>
	/// Does <b>not</b> parse. Ruling 10: assert only that the parse outcome is unchanged, never that
	/// the message matches. <c>ErrorMessages.Returns.ParserFailure</c> carries a source offset and a
	/// source excerpt, so inserting a single space anywhere changes the text — that is inherent to
	/// reporting errors against source, not something break placement could preserve.
	/// <para>
	/// The weaker claim cannot leak: <see cref="Formatting_PreservesTheParseFailure"/> requires the
	/// original to actually fail, and the two parseable corpora require theirs to actually parse, so
	/// an entry cannot be quietly demoted into this group to silence it.
	/// </para>
	/// </summary>
	public static IEnumerable<Func<string>> UnparseableCorpus() =>
	[
		// The `,y` sits inside [...] inside an argument list, where inFunction > 0 and
		// beginGenericText's COMMAWS predicate (SharpMUSHParser.g4:159) is false. Position 41.
		() => "switch(a,[ansi(hr,a long stretch of text),y],{b,c},trailing prose, and more)",
		() => "switch(a,b,c",
		() => "add(1,add(1,add(1,add(1,5)))"
	];

	private async Task<string?> Eval(string code)
		=> (await Parser.FunctionParse(MModule.single(code)))?.Message?.ToString();

	private static bool IsParseFailure(string? result) =>
		result?.StartsWith(ErrorMessages.Returns.ParserFailure[..^3], StringComparison.Ordinal) == true;

	private async Task AssertFormattingPreservesEvaluation(string source, string? expected)
	{
		foreach (var width in Widths)
		{
			var formatted = SoftcodeRenderer.Format(source, width, IsKnownFunction);
			var actual = await Eval(formatted);

			await Assert.That(actual).IsEqualTo(expected)
				.Because($"width {width} changed what [{source}] evaluates to. Formatted:\n{formatted}");
		}
	}

	[Test]
	[MethodDataSource(nameof(ParseableEchoingCorpus))]
	public async Task Formatting_PreservesEvaluatedOutput(string source)
	{
		var expected = await Eval(source);

		await Assert.That(IsParseFailure(expected)).IsFalse()
			.Because($"[{source}] does not parse — it belongs in UnparseableCorpus, not here: {expected}");

		// An entry evaluating to nothing, or to an error that swallows its arguments, would compare
		// equal however badly the formatter mangled it. Fail loudly rather than pass vacuously.
		await Assert.That(string.IsNullOrEmpty(expected)).IsFalse()
			.Because($"[{source}] evaluates to nothing — it belongs in ParseableWeakOutputCorpus");
		await Assert.That(expected!.StartsWith("#-1", StringComparison.Ordinal)).IsFalse()
			.Because($"[{source}] evaluates to an error — it belongs in ParseableWeakOutputCorpus: {expected}");

		await AssertFormattingPreservesEvaluation(source, expected);
	}

	[Test]
	[MethodDataSource(nameof(ParseableWeakOutputCorpus))]
	public async Task Formatting_PreservesEvaluatedOutput_WhereThereIsLittleOutputToPreserve(string source)
	{
		var expected = await Eval(source);

		await Assert.That(IsParseFailure(expected)).IsFalse()
			.Because($"[{source}] does not parse — it belongs in UnparseableCorpus, not here: {expected}");

		await AssertFormattingPreservesEvaluation(source, expected);
	}

	/// <summary>
	/// Ruling 10. The claim here is only that formatting does not turn a parse failure into something
	/// else, or move where it fails to a different <em>kind</em> of outcome — not that the message is
	/// byte-identical, which nothing could deliver.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(UnparseableCorpus))]
	public async Task Formatting_PreservesTheParseFailure(string source)
	{
		await Assert.That(IsParseFailure(await Eval(source))).IsTrue()
			.Because($"[{source}] parses cleanly — it belongs in a parseable corpus, where the "
							 + "assertion is exact-string equality rather than this weaker one");

		foreach (var width in Widths)
		{
			var formatted = SoftcodeRenderer.Format(source, width, IsKnownFunction);

			await Assert.That(IsParseFailure(await Eval(formatted))).IsTrue()
				.Because($"width {width} turned a parse failure into something else for [{source}]. "
								 + $"Formatted:\n{formatted}");
		}
	}

	/// <summary>
	/// The comparison is only sound if evaluating the same source twice gives the same answer, so this
	/// pins that no corpus entry is time-, random- or state-dependent.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(ParseableEchoingCorpus))]
	public async Task CorpusEntry_EvaluatesDeterministically(string source)
	{
		var first = await Eval(source);
		var second = await Eval(source);

		await Assert.That(second).IsEqualTo(first).Because($"[{source}] is not deterministic");
	}

	/// <summary>
	/// The oracle is what decides whether a call is broken into at all, so a broken oracle would make
	/// the whole suite pass by emitting almost no breaks. This pins both of its answers.
	/// </summary>
	[Test]
	public async Task Oracle_AnswersForResolvedAndUnresolvedNames()
	{
		string[] corpusFunctions = ["add", "strcat", "switch", "iter", "ansi", "ucstr"];

		foreach (var name in corpusFunctions)
		{
			await Assert.That(IsKnownFunction(name)).IsTrue().Because($"the corpus calls {name}()");
		}

		await Assert.That(IsKnownFunction("notafunction")).IsFalse();
	}

	/// <summary>
	/// A verdict about a break position is worthless if the corpus never reaches it. Prints the
	/// <c>OBRACK</c> sites, which are Ruling 7's evidence.
	/// </summary>
	[Test]
	public async Task Corpus_ExercisesEachBreakPosition()
	{
		var exercised = new Dictionary<string, List<string>>
		{
			["OBRACK"] = [],
			["FUNCHAR"] = [],
			["COMMAWS"] = []
		};

		var corpus = ParseableEchoingCorpus()
			.Concat(ParseableWeakOutputCorpus())
			.Concat(UnparseableCorpus())
			.Select(entry => entry());

		foreach (var source in corpus)
		{
			var tokens = TestLexer.Lex(source);
			foreach (var width in Widths)
			{
				foreach (var b in SoftcodeLayout.Compute(tokens, width, isKnownFunction: IsKnownFunction))
				{
					if (exercised.TryGetValue(tokens[b.TokenIndex].Type, out var sites))
					{
						sites.Add($"{source} @ {width}");
					}
				}
			}
		}

		Console.WriteLine("OBRACK break sites exercised by the corpus:");
		foreach (var site in exercised["OBRACK"].Distinct())
		{
			Console.WriteLine($"  {site}");
		}

		foreach (var (type, sites) in exercised)
		{
			await Assert.That(sites).IsNotEmpty()
				.Because($"no corpus entry breaks at a {type}, so the equivalence run says nothing about it");
		}
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
		var breaks = SoftcodeLayout.Compute(tokens, width: 20, isKnownFunction: IsKnownFunction);

		await Assert.That(breaks).Count().IsEqualTo(1);
		await Assert.That(tokens[breaks[0].TokenIndex].Type).IsEqualTo("OBRACK");

		var formatted = SoftcodeRenderer.Format(source, width: 20, IsKnownFunction);
		await Assert.That(formatted).IsNotEqualTo(source);
		await Assert.That(await Eval(formatted)).IsEqualTo(await Eval(source))
			.Because($"breaking after '[' changed evaluation. Formatted:\n{formatted}");
	}

	/// <summary>
	/// Ruling 9, directly. An unresolved name is copied through as text with its delimiters' absorbed
	/// whitespace, so the layout must leave the call entirely alone at every width.
	/// </summary>
	[Test]
	public async Task UnresolvedFunctionName_ProducesNoBreakAndNoTextChange()
	{
		const string source = "notafunction(aaaaaaaaaa,bbbbbbbbbb,cccccccccc)";
		await Assert.That(IsKnownFunction("notafunction")).IsFalse();

		var tokens = TestLexer.Lex(source);
		foreach (var width in Widths)
		{
			await Assert.That(SoftcodeLayout.Compute(tokens, width, isKnownFunction: IsKnownFunction)).IsEmpty()
				.Because($"width {width} broke into a call the parser reproduces as text");
			await Assert.That(SoftcodeRenderer.Format(source, width, IsKnownFunction)).IsEqualTo(source);
		}

		await Assert.That(await Eval(source)).IsEqualTo(source);
	}

	/// <summary>
	/// Ruling 9's default. A caller that supplies no oracle gets the conservative reading — nothing
	/// resolves — rather than the optimistic one, so it cannot silently inherit the defect.
	/// </summary>
	[Test]
	public async Task WithoutAnOracle_NoCallIsBrokenInto()
	{
		var tokens = TestLexer.Lex("strcat(alpha,bravo,charlie,delta,echo,foxtrot,golf,hotel,india,juliet)");

		await Assert.That(SoftcodeLayout.Compute(tokens, width: 20)).IsEmpty();
		await Assert.That(SoftcodeLayout.Compute(tokens, width: 20, isKnownFunction: IsKnownFunction)).IsNotEmpty();
	}

	/// <summary>
	/// Ruling 9's propagation rule, asserted structurally so that a failure says which half is wrong.
	/// <c>LiteralFunctionCall</c> raises <c>_suppressFunctionEval</c> around its arguments, so a call
	/// nested in an unresolved one is text too; <c>VisitBracketPattern</c> clears it, so a bracketed
	/// call is dispatched normally however deeply it is buried.
	/// </summary>
	[Test]
	public async Task SuppressionPropagatesInwardButABracketClearsIt()
	{
		var suppressed = TestLexer.Lex("notafunction(aaaa,strcat(bbbbbbbbbb,cccccccccc),dddd)");
		await Assert.That(SoftcodeLayout.Compute(suppressed, width: 20, isKnownFunction: IsKnownFunction))
			.IsEmpty().Because("a call inside an unresolved call is reproduced as text as well");

		var bracketed = TestLexer.Lex("notafunction(aaaa,[strcat(bbbbbbbbbb,cccccccccc)],dddd)");
		var breaks = SoftcodeLayout.Compute(bracketed, width: 20, isKnownFunction: IsKnownFunction);

		var open = bracketed.Index().First(x => x.Item.Type == "OBRACK").Index;
		var close = bracketed.Index().First(x => x.Item.Type == "CBRACK").Index;

		await Assert.That(breaks).IsNotEmpty().Because("a bracket re-enables function recognition");
		await Assert.That(breaks.All(b => b.TokenIndex >= open && b.TokenIndex < close)).IsTrue()
			.Because("only the bracketed call may be broken into; the enclosing call is text");
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
			await Assert.That(SoftcodeLayout.Compute(tokens, width, isKnownFunction: IsKnownFunction)).IsEmpty()
				.Because($"width {width} broke at a prose comma");
			await Assert.That(SoftcodeRenderer.Format(source, width, IsKnownFunction)).IsEqualTo(source);
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
			await Assert.That(SoftcodeLayout.Compute(tokens, width, isKnownFunction: IsKnownFunction)).IsEmpty()
				.Because($"width {width} broke at a bare parenthesis");
			await Assert.That(SoftcodeRenderer.Format(source, width, IsKnownFunction)).IsEqualTo(source);
		}

		await Assert.That(await Eval(source)).IsEqualTo(source);
	}
}
