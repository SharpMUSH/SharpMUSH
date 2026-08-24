using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
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

	private Func<string, SoftcodeCallKind>? _classifier;

	/// <summary>
	/// The classifier Rulings 9 and 12 require, built by <see cref="SoftcodeLayout.ClassifierFor"/>
	/// from the real parser. Deliberately the shared factory rather than a local ladder: a test that
	/// resolved names its own way would be proving the corpus safe under a classifier no production
	/// caller uses.
	/// </summary>
	private SoftcodeCallKind ClassifyFunction(string name)
		=> (_classifier ??= SoftcodeLayout.ClassifierFor(OracleParser))(name);

	/// <summary>
	/// Ruling 20. Lexes through <see cref="IMUSHCodeParser.Tokenize"/> — the entry point
	/// <c>SoftcodeFormatter</c> feeds the layout engine from, and therefore what <c>@examine</c> really
	/// runs. It is <b>not</b> the stream the evaluator parses: four of the five lexing sites in
	/// <c>MUSHCodeParser</c> rewrite an orphaned <c>]</c>/<c>}</c> to literal text first and
	/// <c>Tokenize</c> does not (see <see cref="OrphanedClosers_LexDifferentlyForTheFormatterThanForTheEvaluator"/>).
	/// <para>
	/// Lexing the corpus any other way would prove safety over a stream production never lays out. So
	/// the pipeline under test is end-to-end genuine: production's <c>Tokenize</c>, production's
	/// <c>SoftcodeLayout</c>, compared against production's evaluator.
	/// </para>
	/// </summary>
	private IReadOnlyList<TokenInfo> Lex(string source) => OracleParser.Tokenize(MModule.single(source));

	/// <summary>Lays out and renders <paramref name="source"/> exactly as the formatter would.</summary>
	private string Format(string source, int width, ParseType parseType = ParseType.Function)
	{
		var tokens = Lex(source);

		return SoftcodeRenderer.Render(tokens,
			SoftcodeLayout.Compute(tokens, width, classifyFunction: ClassifyFunction, parseType: parseType));
	}

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

		// Ruling 12. lit is Literal | NoParse (StringFunctions.cs:53) and resolves perfectly well —
		// resolving is what makes it dangerous. LiteralArgumentText slices from just past the '(' to the
		// ')', so both the opener's and every comma's absorbed whitespace lands in the result.
		() => "lit(a,bbbbbbbbbbbbbbbbbbbb)",
		() => "lit(aaaaaaaaaa,bbbbbbbbbb,cccccccccc,dddddddddd)",

		// Ruling 12, and why a source-copying call must be atomic rather than merely unbroken at its own
		// delimiters: nothing visits this span, so the bracket inside is not safe either.
		() => "lit(aaaaaaaaaa,[strcat(bbbbbbbbbb,cccccccccc)],dddddddddd)",

		// Ruling 12. localize is NoParse with MaxArgs 1 (DbrefFunctions.cs:576), which returns
		// MModule.substring over the whole function context.
		() => "localize(strcat(aaaaaaaaaa,bbbbbbbbbb,cccccccccc))",

		// Ruling 12, nested: a source-copying call inside a call that does evaluate its arguments. The
		// outer breaks, the inner must not.
		() => "strcat(aaaaaaaaaa,lit(bbbbbbbbbb,cccccccccc),dddddddddd)",

		// Ruling 20. An orphaned ']' is literal text to the evaluator, which rewrites it before parsing,
		// but still a CBRACK to Tokenize, which the formatter lexes with. These entries are where those
		// two streams disagree, so they are the ones the corpus most needs.
		() => "strcat(aaaaaaaaaa,bbbbbbbbbb] and a tail,cccccccccc)",
		() => "[strcat(aaaaaaaaaa,bbbbbbbbbb)]] an orphaned closer after a real one",

		// Ruling 20, braces: an orphaned '}' likewise.
		() => "strcat(aaaaaaaaaa,bbbbbbbbbb} and a tail,cccccccccc)",

		// Ruling 20, channel 1, and the entry that matters most in this group: a **parseable** input on
		// which the formatter's stream and the evaluator's genuinely disagree. At width 20 Tokenize
		// yields one break (the opener) where the evaluator's rewritten stream would yield two, because
		// the ']' is closer-typed here and text there. Evaluating to `aaaaaaaaaaaaaaaaaaaa]` either way
		// is the proof that a divergence does not have to cost correctness.
		() => "strcat(aaaaaaaaaaaaaaaaaaaa,])",

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
		() => "add(1,add(1,add(1,add(1,5)))",

		// Ruling 20, channel 2 — the one that runs in the UNSAFE direction, and the reason deferring the
		// Tokenize fix is defensible. RewriteOrphanedBracketClosers counts depth flat and globally while
		// BuildGroupTree matches on a stack (SoftcodeLayout.cs:349), so here the first ']' is consumed by
		// the flat counter but ignored by the stack — leaving the second ']' an orphan to the evaluator
		// and a real CBRACK to Tokenize. Tokenize therefore pops the OBRACK and makes `,c` a strcat
		// separator: a break the evaluator's stream would not have. It is only ever reachable when a ']'
		// sits where the grammar — itself a stack machine — cannot accept one, which is a syntax error.
		// Hence unparseable, always. See OrphanedClosers_LexDifferentlyForTheFormatterThanForTheEvaluator.
		() => "strcat([strcat(a],b)],c)",
		() => "strcat([strcat(aaaaaaaaaa],bbbbbbbbbb)],cccccccccc)",

		// Ruling 20. The reviewer's brute-force case and a sibling. Both emit zero breaks — f is
		// unresolved, and the trailing closer takes the rest — so the per-width assertion re-checks one
		// unchanged string. They are here to pin their *classification*, that an orphan closing an
		// unclosed group is a parse failure, not to exercise formatting. The entry below them does break,
		// and OrphanedCloserAtTheEnd_CostsExactlyTheBreakTheEvaluatorsStreamWouldAllow carries the
		// structural claim.
		() => "aaaaaaaaaaf(aaaaaaaaaa]",
		() => "aaaaaaaaaastrcat(aaaaaaaaaa,bbbbbbbbbb}",
		() => "strcat(aaaaaaaaaaaaaaaaaaaa,bbbbbbbbbbbbbbbbbbbb]"
	];

	/// <summary>
	/// Ruling 11. The command-list dialect, where a root <c>;</c> genuinely separates commands and so is
	/// a break position. Evaluated through <see cref="IMUSHCodeParser.CommandListParse"/> — the entry
	/// point that selects <c>startCommandString</c>, the one rule that sets <c>inCommandList</c>.
	/// <para>
	/// Every entry uses <c>think</c>, which returns its evaluated argument in the resulting
	/// <c>CallState</c>, so a newline inserted anywhere in a command shows up in the output rather than
	/// being swallowed by an unknown-command error.
	/// </para>
	/// </summary>
	public static IEnumerable<Func<string>> CommandListCorpus() =>
	[
		// The plain case: a root ';' between two commands, each long enough to force the break.
		() => "think aaaaaaaaaaaaaaaaaaaa;think bbbbbbbbbbbbbbbbbbbb",

		// Prose commas inside commands are still text, even where the ';' is structural.
		() => "think a long line of prose, with commas in it;think and a second command here",

		// A bracketed call inside a command: breaks at three positions in one line.
		() => "think [strcat(alpha bravo,charlie delta,echo foxtrot,golf hotel)];think a tail command",

		// An unresolved call inside a command list — Rulings 9 and 11 interacting.
		() => "think notafunction(aaaaaaaaaa,bbbbbbbbbb);think a second command goes here",

		// Three commands, so a middle semicolon is neither first nor last.
		() => "think aaaaaaaaaaaaaaaa;think bbbbbbbbbbbbbbbb;think cccccccccccccccc"
	];

	private async Task<string?> Eval(string code)
		=> (await Parser.FunctionParse(MModule.single(code)))?.Message?.ToString();

	private async Task<string?> EvalCommandList(string code)
		=> (await Parser.CommandListParse(MModule.single(code)))?.Message?.ToString();

	private static bool IsParseFailure(string? result) =>
		result?.StartsWith(ErrorMessages.Returns.ParserFailure[..^3], StringComparison.Ordinal) == true;

	private async Task AssertFormattingPreservesEvaluation(string source, string? expected)
	{
		foreach (var width in Widths)
		{
			var formatted = Format(source, width);
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
			var formatted = Format(source, width);

			await Assert.That(IsParseFailure(await Eval(formatted))).IsTrue()
				.Because($"width {width} turned a parse failure into something else for [{source}]. "
								 + $"Formatted:\n{formatted}");
		}
	}

	/// <summary>
	/// Ruling 11. The command-list half of the semicolon axis: laid out as a command list, evaluated as
	/// a command list, output unchanged. The other half — that no semicolon break is emitted for the
	/// function dialect at all — is <see cref="RootSemicolon_IsABreakPositionOnlyInTheCommandListDialect"/>.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(CommandListCorpus))]
	public async Task CommandListFormatting_PreservesEvaluatedOutput(string source)
	{
		var expected = await EvalCommandList(source);

		await Assert.That(string.IsNullOrEmpty(expected)).IsFalse()
			.Because($"[{source}] produces no command output, so it proves nothing");
		await Assert.That(expected!.Contains("#-1", StringComparison.Ordinal)).IsFalse()
			.Because($"[{source}] failed to run, so it proves nothing: {expected}");

		foreach (var width in Widths)
		{
			var formatted = Format(source, width, ParseType.CommandList);
			var actual = await EvalCommandList(formatted);

			await Assert.That(actual).IsEqualTo(expected)
				.Because($"width {width} changed what [{source}] does. Formatted:\n{formatted}");
		}
	}

	[Test]
	[MethodDataSource(nameof(CommandListCorpus))]
	public async Task CommandListCorpusEntry_EvaluatesDeterministically(string source)
	{
		var first = await EvalCommandList(source);
		var second = await EvalCommandList(source);

		await Assert.That(second).IsEqualTo(first).Because($"[{source}] is not deterministic");
	}

	/// <summary>
	/// Ruling 11, and the regression guard for finding 3. A root <c>;</c> is a command separator only
	/// under <c>startCommandString</c>; in the function dialect <c>beginGenericText</c>
	/// (<c>SharpMUSHParser.g4:158</c>) claims it as text and its absorbed whitespace is emitted.
	/// <para>
	/// The last assertion is what makes this bite: it shows the command-list layout of the very same
	/// text really does change the result when evaluated as a function expression, so the first half is
	/// preventing something real rather than asserting the absence of a break nobody wanted.
	/// </para>
	/// </summary>
	[Test]
	public async Task RootSemicolon_IsABreakPositionOnlyInTheCommandListDialect()
	{
		const string source = "aaaaaaaaaaaaaaaaaaaa;bbbbbbbbbbbbbbbbbbbb";
		var tokens = Lex(source);

		foreach (var width in Widths)
		{
			await Assert.That(SoftcodeLayout.Compute(tokens, width, classifyFunction: ClassifyFunction)).IsEmpty()
				.Because($"width {width} broke at a semicolon that is literal text in this dialect");
			await Assert.That(Format(source, width)).IsEqualTo(source);
		}

		await Assert.That(await Eval(source)).IsEqualTo(source);

		var commandBreaks = SoftcodeLayout.Compute(tokens, width: 20, classifyFunction: ClassifyFunction,
			parseType: ParseType.CommandList);

		await Assert.That(commandBreaks).Count().IsEqualTo(1);
		await Assert.That(tokens[commandBreaks[0].TokenIndex].Type).IsEqualTo("SEMICOLON");

		var asCommandList = Format(source, 20, ParseType.CommandList);
		await Assert.That(await Eval(asCommandList)).IsNotEqualTo(await Eval(source))
			.Because("if the command-list layout round-tripped as a function too, this guard would be idle");
	}

	/// <summary>
	/// Ruling 11 across every member of <see cref="ParseType"/>, so a new dialect cannot be added
	/// without someone deciding which side of this line it falls on. Only <c>startCommandString</c>
	/// (<c>SharpMUSHParser.g4:29</c>) sets <c>inCommandList</c>, and <c>MUSHCodeParser</c> selects it
	/// for <see cref="ParseType.CommandList"/> alone — <see cref="ParseType.Command"/> is
	/// <c>startSingleCommandString</c>, which is <c>command EOF</c> and never enters <c>commandList</c>.
	/// </summary>
	[Test]
	public async Task OnlyTheCommandListDialectBreaksAtASemicolon()
	{
		var tokens = Lex("aaaaaaaaaaaaaaaaaaaa;bbbbbbbbbbbbbbbbbbbb");

		foreach (var parseType in Enum.GetValues<ParseType>())
		{
			var breaks = SoftcodeLayout.Compute(tokens, width: 20, classifyFunction: ClassifyFunction,
				parseType: parseType);

			await Assert.That(breaks.Count > 0).IsEqualTo(parseType == ParseType.CommandList)
				.Because($"{parseType} is on the wrong side of the semicolon rule");
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
	/// The classifier is what decides whether a call is broken into at all, so a broken one would make
	/// the whole suite pass by emitting almost no breaks. This pins all three of its answers against the
	/// real function library, including the two source-copying declarations Ruling 12 is about.
	/// </summary>
	[Test]
	public async Task Classifier_AnswersAllThreeKindsFromTheRealLibrary()
	{
		string[] evaluating = ["add", "strcat", "switch", "iter", "ansi", "ucstr"];

		foreach (var name in evaluating)
		{
			await Assert.That(ClassifyFunction(name)).IsEqualTo(SoftcodeCallKind.EvaluatesArguments)
				.Because($"the corpus calls {name}(), which evaluates its arguments");
		}

		// lit: Literal | NoParse (StringFunctions.cs:53). localize: NoParse, MaxArgs 1
		// (DbrefFunctions.cs:576). Both reach a raw-source branch of CallFunction.
		foreach (var name in (string[])["lit", "localize"])
		{
			await Assert.That(ClassifyFunction(name)).IsEqualTo(SoftcodeCallKind.CopiesArgumentSource)
				.Because($"{name}() copies the source between its parentheses instead of evaluating it");
		}

		await Assert.That(ClassifyFunction("notafunction")).IsEqualTo(SoftcodeCallKind.Unresolved);

		// switch and iter are NoParse with MaxArgs > 1, the branch that slices each argument from its
		// own start index. If that were ever reclassified, most of the corpus would stop breaking.
		await Assert.That(ClassifyFunction("switch")).IsEqualTo(SoftcodeCallKind.EvaluatesArguments);
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
			["COMMAWS"] = [],
			["SEMICOLON"] = []
		};

		// Each corpus is measured in the dialect it is evaluated in, since that is the layout whose
		// safety the corresponding test proves.
		(IEnumerable<Func<string>> Entries, ParseType Dialect)[] corpora =
		[
			(ParseableEchoingCorpus(), ParseType.Function),
			(ParseableWeakOutputCorpus(), ParseType.Function),
			(UnparseableCorpus(), ParseType.Function),
			(CommandListCorpus(), ParseType.CommandList)
		];

		foreach (var (entries, dialect) in corpora)
		{
			foreach (var source in entries.Select(entry => entry()))
			{
				var tokens = Lex(source);
				foreach (var width in Widths)
				{
					var breaks = SoftcodeLayout.Compute(tokens, width, classifyFunction: ClassifyFunction,
						parseType: dialect);

					foreach (var b in breaks)
					{
						if (exercised.TryGetValue(tokens[b.TokenIndex].Type, out var sites))
						{
							sites.Add($"{source} @ {width}");
						}
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
		var tokens = Lex(source);
		var breaks = SoftcodeLayout.Compute(tokens, width: 20, classifyFunction: ClassifyFunction);

		await Assert.That(breaks).Count().IsEqualTo(1);
		await Assert.That(tokens[breaks[0].TokenIndex].Type).IsEqualTo("OBRACK");

		var formatted = Format(source, 20);
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
		await Assert.That(ClassifyFunction("notafunction")).IsEqualTo(SoftcodeCallKind.Unresolved);

		var tokens = Lex(source);
		foreach (var width in Widths)
		{
			await Assert.That(SoftcodeLayout.Compute(tokens, width, classifyFunction: ClassifyFunction)).IsEmpty()
				.Because($"width {width} broke into a call the parser reproduces as text");
			await Assert.That(Format(source, width)).IsEqualTo(source);
		}

		await Assert.That(await Eval(source)).IsEqualTo(source);
	}

	/// <summary>
	/// Ruling 12, structurally. A call that copies its argument source is atomic: <c>Compute</c> emits
	/// nothing at all inside its span, including at a bracket, because no visitor ever runs over that
	/// span to discard a delimiter's absorbed whitespace.
	/// </summary>
	[Test]
	public async Task SourceCopyingCalls_AreAtomic()
	{
		string[] sources =
		[
			"lit(a,bbbbbbbbbbbbbbbbbbbb)",
			"lit(aaaaaaaaaa,bbbbbbbbbb,cccccccccc,dddddddddd)",
			"lit(aaaaaaaaaa,[strcat(bbbbbbbbbb,cccccccccc)],dddddddddd)",
			"localize(strcat(aaaaaaaaaa,bbbbbbbbbb,cccccccccc))"
		];

		foreach (var source in sources)
		{
			var tokens = Lex(source);
			foreach (var width in Widths)
			{
				await Assert.That(SoftcodeLayout.Compute(tokens, width, classifyFunction: ClassifyFunction))
					.IsEmpty().Because($"width {width} broke inside [{source}], whose source is copied verbatim");
				await Assert.That(Format(source, width)).IsEqualTo(source);
			}
		}

		// The outer call still breaks; only the source-copying one is left alone.
		const string nested = "strcat(aaaaaaaaaa,lit(bbbbbbbbbb,cccccccccc),dddddddddd)";
		var nestedTokens = Lex(nested);
		var breaks = SoftcodeLayout.Compute(nestedTokens, width: 20, classifyFunction: ClassifyFunction);
		var litOpen = nestedTokens.Index().First(x => x.Item.Text.StartsWith("lit(", StringComparison.Ordinal)).Index;
		var litClose = nestedTokens.Index().First(x => x.Index > litOpen && x.Item.Type == "CPAREN").Index;

		await Assert.That(breaks).IsNotEmpty();
		await Assert.That(breaks.Any(b => b.TokenIndex >= litOpen && b.TokenIndex <= litClose)).IsFalse()
			.Because("nothing inside lit() may be broken, however deeply the enclosing call is broken");
	}

	/// <summary>
	/// Ruling 12's sharpest case, by evaluation. Without the fix this is the exact input that goes red:
	/// <c>lit</c> resolves, so the name-resolution oracle alone would have permitted an opener break and
	/// two comma breaks, and <c>LiteralArgumentText</c> would have copied all three newlines out.
	/// </summary>
	[Test]
	public async Task LiteralCall_WouldLeakItsDelimiterWhitespaceIfBroken()
	{
		const string source = "lit(aaaaaaaaaa,bbbbbbbbbb,cccccccccc)";

		// What the pre-Ruling-12 layout would have produced: classify lit as an ordinary function.
		var litTokens = Lex(source);
		var asIfEvaluating = SoftcodeRenderer.Render(litTokens, SoftcodeLayout.Compute(litTokens, width: 20,
			classifyFunction: name =>
				name == "lit" ? SoftcodeCallKind.EvaluatesArguments : ClassifyFunction(name)));

		await Assert.That(asIfEvaluating).IsNotEqualTo(source);
		await Assert.That(await Eval(asIfEvaluating)).IsNotEqualTo(await Eval(source))
			.Because("if lit() round-tripped under the old classification, Ruling 12 would be idle");

		// What it produces now.
		await Assert.That(Format(source, 20)).IsEqualTo(source);
		await Assert.That(await Eval(source)).IsEqualTo("aaaaaaaaaa,bbbbbbbbbb,cccccccccc");
	}

	/// <summary>
	/// Ruling 20, pinned. <c>Tokenize</c> is the only lexing site in <c>MUSHCodeParser</c> that does
	/// <b>not</b> rewrite an orphaned <c>]</c>/<c>}</c> to literal text — <c>ParseInternal</c>
	/// (<c>:353-354</c>), <c>CommandListParseVisitor</c> (<c>:531-532</c>),
	/// <c>ValidateAndGetErrors</c> (<c>:694-695</c>) and <c>GetSemanticTokens</c> (<c>:795-796</c>) all
	/// do. This test states that difference rather than assuming it away, because the doc comment that
	/// previously claimed the two streams differed only in the EOF token is what let the corpus lex the
	/// wrong stream for four rounds.
	/// <para>
	/// <b>The divergence has two channels, and only one of them is conservative.</b> An earlier version
	/// of this comment claimed the effect was always fewer breaks; that was wrong, and it was wrong in
	/// the direction that matters, so it is spelled out properly here.
	/// </para>
	/// <para>
	/// <b>Channel 1 — conservative.</b> Where the rewrite's flat depth count and <c>BuildGroupTree</c>'s
	/// stack agree that a closer closes nothing, the only consumer that notices is <c>Layout</c>'s
	/// trailing-closer scan, which walks <c>lastContent</c> back past closer-typed tokens. Every break
	/// condition is <c>… &lt; lastContent</c>, so a smaller <c>lastContent</c> yields strictly
	/// <b>fewer</b> breaks. Reachable on parseable input — <c>strcat(aaaa…,])</c> is in
	/// <see cref="ParseableEchoingCorpus"/> for that reason, and evaluates identically either way.
	/// </para>
	/// <para>
	/// <b>Channel 2 — not conservative.</b> The two counters can desynchronise:
	/// <c>RewriteOrphanedBracketClosers</c> counts <c>[</c>/<c>]</c> globally, while
	/// <c>BuildGroupTree</c> ignores a closer whose opener is not on top of its stack
	/// (<c>SoftcodeLayout.cs:349</c>). In <c>strcat([strcat(a],b)],c)</c> the first <c>]</c> is consumed
	/// by the flat count but ignored by the stack, so the second is an orphan to the evaluator and a live
	/// <c>CBRACK</c> here — this engine pops the <c>OBRACK</c>, and <c>,c</c> becomes a <c>strcat</c>
	/// separator and a break the evaluator's stream would never permit. <b>More</b> breaks, unsafe.
	/// </para>
	/// <para>
	/// What rescues it is not monotonicity but the grammar: channel 2 needs a <c>]</c> in a position a
	/// stack machine cannot accept — <c>bracketPattern</c> requires a complete <c>evaluationString</c>
	/// before its <c>CBRACK</c>, and <c>function</c> a <c>CPAREN</c> before that — so such input is a
	/// syntax error. Both channel-2 entries sit in <see cref="UnparseableCorpus"/>, and the assertion
	/// below records that each takes the divergent path here while failing to parse there.
	/// </para>
	/// </summary>
	[Test]
	public async Task OrphanedClosers_LexDifferentlyForTheFormatterThanForTheEvaluator()
	{
		// What the formatter sees: still a closer token.
		await Assert.That(Lex("a]b").Select(t => t.Type)).Contains("CBRACK");
		await Assert.That(Lex("a}b").Select(t => t.Type)).Contains("CBRACE");

		// A matched pair is a closer for everyone, so those inputs say nothing either way.
		await Assert.That(Lex("[a]").Select(t => t.Type)).Contains("CBRACK");
		await Assert.That(Lex("{a}").Select(t => t.Type)).Contains("CBRACE");

		// TestLexer, which the Task 2 unit tests use, must agree with Tokenize token for token —
		// otherwise those tests drift onto a third stream nobody produces.
		string[] probes =
		[
			"a]b", "a}b", "[a]", "{a}", "[a]]", "aaaaaaaaaaf(aaaaaaaaaa]",
			"strcat(aaaaaaaaaa,bbbbbbbbbb] and a tail,cccccccccc)"
		];

		foreach (var probe in probes)
		{
			await Assert.That(TestLexer.Lex(probe).Select(t => $"{t.Type}:{t.Text}"))
				.IsEquivalentTo(Lex(probe).Select(t => $"{t.Type}:{t.Text}"))
				.Because($"TestLexer and MUSHCodeParser.Tokenize disagree on [{probe}]");
		}

		// Channel 2, pinned: this engine really does take the divergent path — it pops the OBRACK the
		// evaluator's stream leaves open, so the second comma becomes a strcat separator and breaks —
		// and the input really is a parse failure, which is what keeps the unsafe channel unreachable.
		// If a future grammar change made this parse, this assertion fails and Tokenize must be fixed
		// before the formatter can be trusted on it.
		const string channelTwo = "strcat([strcat(a],b)],c)";
		var channelTwoTokens = Lex(channelTwo);
		var channelTwoBreaks = SoftcodeLayout.Compute(channelTwoTokens, width: 20,
			classifyFunction: ClassifyFunction);
		var lastBracket = channelTwoTokens.Index().Last(x => x.Item.Type == "CBRACK").Index;

		await Assert.That(channelTwoBreaks.Any(b => b.TokenIndex > lastBracket
			&& channelTwoTokens[b.TokenIndex].Type == "COMMAWS")).IsTrue()
			.Because("the desynchronised stack pops the bracket group and promotes the trailing comma");
		await Assert.That(IsParseFailure(await Eval(channelTwo))).IsTrue()
			.Because("channel 2 needs a ']' the grammar's own stack cannot accept, so it never parses");
	}

	/// <summary>
	/// Ruling 20, <b>channel 1</b> — the conservative one — isolated to a single variable. The
	/// evaluator's rewrite makes the trailing orphan text, so it counts as content; to <c>Tokenize</c> it
	/// stays closer-typed and <c>Layout</c>'s trailing-closer scan walks past it, lowering
	/// <c>lastContent</c>. Every break condition is <c>… &lt; lastContent</c>, so <b>within this
	/// channel</b> the formatter's stream can only produce fewer breaks.
	/// <para>
	/// That monotonicity argument covers channel 1 and nothing else — see
	/// <see cref="OrphanedClosers_LexDifferentlyForTheFormatterThanForTheEvaluator"/> for channel 2,
	/// where the two counters desynchronise and the layout engine emits <em>more</em> breaks than the
	/// evaluator's stream would allow. It is not monotonicity that makes the divergence tolerable
	/// overall; it is that channel 2 is always a syntax error.
	/// </para>
	/// <para>
	/// The two sources here differ in exactly one character, chosen so the token counts match and only
	/// the final token's type differs — <c>X</c> lexes as ordinary text, which is precisely what the
	/// evaluator's rewrite turns the <c>]</c> into. A resolved name so the classifier is not also
	/// suppressing — it has to start the input, because <c>FUNCHAR</c> is <c>[0-9a-zA-Z_~@`]+ '('</c>
	/// and would otherwise swallow any preceding word into a name that resolves to nothing — and an
	/// argument long enough that the call does not simply fit flat.
	/// </para>
	/// <para>
	/// These two particular sources are asserted structurally rather than by evaluation because both
	/// leave the call unclosed at end of input, which is a parse error. That is a property of <em>these
	/// inputs</em>, not of the divergence: <c>strcat(aaaa…,])</c> in
	/// <see cref="ParseableEchoingCorpus"/> is the same channel-1 divergence on input that parses, and
	/// it carries the evaluation claim across it.
	/// </para>
	/// </summary>
	[Test]
	public async Task OrphanedCloserAtTheEnd_CostsExactlyTheBreakTheEvaluatorsStreamWouldAllow()
	{
		const string withOrphan = "strcat(aaaaaaaaaaaaaaaaaaaa,]";
		const string asEvaluatorSeesIt = "strcat(aaaaaaaaaaaaaaaaaaaa,X";

		var orphanTokens = Lex(withOrphan);
		await Assert.That(orphanTokens[^1].Type).IsEqualTo("CBRACK")
			.Because("Tokenize does not rewrite the orphan; that is the whole finding");
		await Assert.That(Lex(asEvaluatorSeesIt)[^1].Type).IsEqualTo("OTHER");
		await Assert.That(Lex(asEvaluatorSeesIt)).Count().IsEqualTo(orphanTokens.Count);

		var withOrphanBreaks = SoftcodeLayout.Compute(orphanTokens, width: 20, classifyFunction: ClassifyFunction);
		var asTextBreaks = SoftcodeLayout.Compute(Lex(asEvaluatorSeesIt), width: 20,
			classifyFunction: ClassifyFunction);

		// One break lost: the comma, whose `i < lastContent` now fails. The opener break survives.
		await Assert.That(withOrphanBreaks).Count().IsEqualTo(1);
		await Assert.That(asTextBreaks).Count().IsEqualTo(2);
		await Assert.That(orphanTokens[withOrphanBreaks[0].TokenIndex].Type).IsEqualTo("FUNCHAR");
		await Assert.That(withOrphanBreaks.Count).IsLessThan(asTextBreaks.Count)
			.Because("the formatter's stream must be the conservative one, never the other way round");
	}

	/// <summary>
	/// Ruling 9's default. A caller that supplies no oracle gets the conservative reading — nothing
	/// resolves — rather than the optimistic one, so it cannot silently inherit the defect.
	/// </summary>
	[Test]
	public async Task WithoutAnOracle_NoCallIsBrokenInto()
	{
		var tokens = Lex("strcat(alpha,bravo,charlie,delta,echo,foxtrot,golf,hotel,india,juliet)");

		await Assert.That(SoftcodeLayout.Compute(tokens, width: 20)).IsEmpty();
		await Assert.That(SoftcodeLayout.Compute(tokens, width: 20, classifyFunction: ClassifyFunction)).IsNotEmpty();
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
		var suppressed = Lex("notafunction(aaaa,strcat(bbbbbbbbbb,cccccccccc),dddd)");
		await Assert.That(SoftcodeLayout.Compute(suppressed, width: 20, classifyFunction: ClassifyFunction))
			.IsEmpty().Because("a call inside an unresolved call is reproduced as text as well");

		var bracketed = Lex("notafunction(aaaa,[strcat(bbbbbbbbbb,cccccccccc)],dddd)");
		var breaks = SoftcodeLayout.Compute(bracketed, width: 20, classifyFunction: ClassifyFunction);

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
		var tokens = Lex(source);

		foreach (var width in Widths)
		{
			await Assert.That(SoftcodeLayout.Compute(tokens, width, classifyFunction: ClassifyFunction)).IsEmpty()
				.Because($"width {width} broke at a prose comma");
			await Assert.That(Format(source, width)).IsEqualTo(source);
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
		var tokens = Lex(source);

		await Assert.That(tokens.Select(t => t.Type)).Contains("OPAREN");

		foreach (var width in Widths)
		{
			await Assert.That(SoftcodeLayout.Compute(tokens, width, classifyFunction: ClassifyFunction)).IsEmpty()
				.Because($"width {width} broke at a bare parenthesis");
			await Assert.That(Format(source, width)).IsEqualTo(source);
		}

		await Assert.That(await Eval(source)).IsEqualTo(source);
	}
}
