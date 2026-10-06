using System.Diagnostics;
using Antlr4.Runtime.Atn;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// A bracketed expression in a function's second or later argument, full of nested calls, used to
/// take time exponential in the number of calls: <c>if(c,A,[setq(...)][json(object,...)])</c> of
/// a few hundred characters ran for minutes. ANTLR could not tell whether the comma before the
/// bracket was text or a separator until it had read past the bracket, and forked the same way
/// at every comma and parenthesis inside it, worst where a call's last argument is another call.
/// The SLL pass now settles the grammar's predicates where each decision starts
/// (<see cref="PredicateResolvingSimulator"/>). Unfixed, 12 calls took 13 seconds to parse and
/// each one more about tripled it; the 14 here take milliseconds.
/// </summary>
public class PredictionLookaheadTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

	/// <summary><c>if(0,A,[strcat(strlen(lcstr(ab)),...)])</c> with <paramref name="calls"/> of <c>strlen(lcstr(ab))</c>.</summary>
	private static string BracketInThirdArgument(int calls)
		=> $"if(0,A,[strcat({string.Join(",", Enumerable.Repeat("strlen(lcstr(ab))", calls))})])";

	[Test]
	public async Task BracketInLaterArgumentEvaluatesQuickly()
	{
		var stopwatch = Stopwatch.StartNew();
		var result = await Parser.FunctionParse(MarkupText.Plain(BracketInThirdArgument(14)));
		stopwatch.Stop();

		await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(new string('2', 14));
		await Assert.That(stopwatch.Elapsed).IsLessThan(Bound);
	}

	// The editor's highlighting and validation parse without evaluating; they take the same SLL pass.
	[Test]
	public async Task BracketInLaterArgumentAnalyzesQuickly()
	{
		var text = MarkupText.Plain(BracketInThirdArgument(14));
		var stopwatch = Stopwatch.StartNew();
		var errors = Parser.ValidateAndGetErrors(text);
		var tokens = Parser.GetSemanticTokens(text);
		stopwatch.Stop();

		await Assert.That(errors).IsEmpty();
		await Assert.That(tokens).IsNotEmpty();
		await Assert.That(stopwatch.Elapsed).IsLessThan(Bound);
	}

	/// <summary>
	/// Resolving the predicates early changes how far prediction looks, not which alternative it
	/// picks: ANTLR's own simulator evaluates the same predicates, against the same parser state,
	/// once its lookahead ends. The SLL pass succeeds exactly when the stock one does, and its tree
	/// is the one LL builds.
	/// </summary>
	[Test]
	[Arguments("if(c,A,[s(a(b(c)),a(b(c)),a(b(c)),a(b(c)))])", ParseType.Function, false)]
	[Arguments("[setq(0,%#)][json(object,key,[get(%q0/a)],other,json(array,1,2))]", ParseType.Function, false)]
	[Arguments("switch(%0,a,b(c,d),e(f),{g,h(i)},j)", ParseType.Function, false)]
	[Arguments("strcat(a,b) text, more (text) = x; y", ParseType.Function, false)]
	[Arguments("f(a (b, c) d, e)", ParseType.Function, true)]
	[Arguments("@switch %0=1,{@pemit %#=a(b,c);think x},2,think [y(z)]", ParseType.CommandList, false)]
	[Arguments("think [iter(1 2,add(##,1))]; @pemit me=%q<a>", ParseType.CommandList, false)]
	[Arguments("a,b(c,d),[e(f,g)],{g,h}", ParseType.CommandCommaArgs, false)]
	[Arguments("me=[name(%#)],second,third(x,y)", ParseType.CommandEqSplitArgs, false)]
	[Arguments("x=y=z(1,=)", ParseType.CommandEqSplit, false)]
	[Arguments("$<name>text$1>%q<a^b>", ParseType.Function, false)]
	public async Task TreeMatchesStockPrediction(string input, ParseType parseType, bool parenGroups)
	{
		var tokens = SoftcodeParsePipeline.Lex(input, nameof(TreeMatchesStockPrediction));

		string? Sll(bool resolvePredicates)
		{
			tokens.Seek(0);
			var parser = SoftcodeParsePipeline.CreateParser(tokens, parenGroups, PredictionMode.SLL, resolvePredicates: resolvePredicates);
			return SoftcodeParsePipeline.ParseClean(parser, p => SoftcodeParsePipeline.Enter(p, parseType),
				new ParserErrorListener(input))?.ToStringTree(parser);
		}

		var resolved = Sll(resolvePredicates: true);
		await Assert.That(resolved).IsEqualTo(Sll(resolvePredicates: false));

		tokens.Seek(0);
		var stock = SoftcodeParsePipeline.CreateParser(tokens, parenGroups, PredictionMode.LL);
		var errors = new ParserErrorListener(input);
		stock.AddErrorListener(errors);
		var llTree = SoftcodeParsePipeline.Enter(stock, parseType).ToStringTree(stock);

		await Assert.That(errors.HasErrors).IsFalse();
		await Assert.That(resolved).IsEqualTo(llTree);
	}
}
