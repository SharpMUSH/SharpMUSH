using System.Diagnostics;
using Antlr4.Runtime.Atn;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// A bracketed expression in a function's second or later argument, full of nested calls, used to
/// take time exponential in the number of calls: <c>if(c,A,[setq(...)][json(object,...)])</c> of
/// a few hundred characters ran for minutes. Whether a comma was text or a separator was a
/// predicate on parser state, which ANTLR settles only once its lookahead reaches a conflict, so it
/// read past the bracket and forked at every comma and parenthesis inside it. The grammar now has a
/// copy of each rule per context (see docs/design/softcode-grammar.md), so prediction settles each
/// token where it stands. Unfixed, 12 calls took 13 seconds to parse and each one more about tripled
/// it; the 14 here take milliseconds, and so does malformed input, which the LL pass reads.
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

	// An unclosed bracket fails SLL and goes to LL, which reports the error. With predicates, 20
	// calls took seconds there too.
	[Test]
	public async Task MalformedBracketInLaterArgumentIsReportedQuickly()
	{
		var text = MarkupText.Plain(BracketInThirdArgument(20)[..^2]);
		var stopwatch = Stopwatch.StartNew();
		var errors = Parser.ValidateAndGetErrors(text);
		stopwatch.Stop();

		await Assert.That(errors).IsNotEmpty();
		await Assert.That(stopwatch.Elapsed).IsLessThan(Bound);
	}

	/// <summary>
	/// The SLL pass is taken whenever it succeeds, which is only sound if its tree is the one LL
	/// builds.
	/// </summary>
	[Test]
	[Arguments("if(c,A,[s(a(b(c)),a(b(c)),a(b(c)),a(b(c)))])", ParseType.Function, false)]
	[Arguments("[setq(0,%#)][json(object,key,[get(%q0/a)],other,json(array,1,2))]", ParseType.Function, false)]
	[Arguments("switch(%0,a,b(c,d),e(f),{g,h(i)},j)", ParseType.Function, false)]
	[Arguments("strcat(a,b) text, more (text) = x; y", ParseType.Function, false)]
	[Arguments("f(a (b, c) d, e)", ParseType.Function, true)]
	[Arguments("(a, b) = c; (d", ParseType.CommandList, true)]
	[Arguments("@switch %0=1,{@pemit %#=a(b,c);think x},2,think [y(z)]", ParseType.CommandList, false)]
	[Arguments("think [iter(1 2,add(##,1))]; @pemit me=%q<a>", ParseType.CommandList, false)]
	[Arguments("a,b(c,d),[e(f,g)],{g,h}", ParseType.CommandCommaArgs, false)]
	[Arguments("me=[name(%#)],second,third(x,y)", ParseType.CommandEqSplitArgs, false)]
	[Arguments("x=y=z(1,=)", ParseType.CommandEqSplit, false)]
	[Arguments("$<name>text$1>%q<a^b>", ParseType.Function, false)]
	public async Task SllTreeIsTheLlTree(string input, ParseType parseType, bool parenGroups)
	{
		var tokens = SoftcodeParsePipeline.Lex(input, nameof(SllTreeIsTheLlTree));

		var sll = SoftcodeParsePipeline.CreateParser(tokens, parenGroups, PredictionMode.SLL);
		var sllTree = SoftcodeParsePipeline.ParseClean(sll, p => SoftcodeParsePipeline.Enter(p, parseType),
			new ParserErrorListener(input))?.ToStringTree(sll);

		tokens.Seek(0);
		var ll = SoftcodeParsePipeline.CreateParser(tokens, parenGroups, PredictionMode.LL);
		var errors = new ParserErrorListener(input);
		ll.AddErrorListener(errors);
		var llTree = SoftcodeParsePipeline.Enter(ll, parseType).ToStringTree(ll);

		await Assert.That(errors.HasErrors).IsFalse();
		await Assert.That(sllTree).IsEqualTo(llTree);
	}

	/// <summary>
	/// SharpMUSHParser.g4 and SharpMUSHParser.Contexts.cs are written by generate-parser.py; an edit
	/// to either by hand is lost the next time it runs.
	/// </summary>
	[Test]
	public async Task GeneratedGrammarIsUpToDate()
	{
		var start = new ProcessStartInfo("python3") { RedirectStandardError = true };
		start.ArgumentList.Add(Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Parser.Generated", "generate-parser.py"));
		start.ArgumentList.Add("--check");
		Process generator;
		try
		{
			generator = Process.Start(start)!;
		}
		catch (System.ComponentModel.Win32Exception)
		{
			Skip.Test("python3 is not installed");
			throw;
		}

		using (generator)
		{
			var error = await generator.StandardError.ReadToEndAsync();
			await generator.WaitForExitAsync();
			await Assert.That(generator.ExitCode).IsEqualTo(0).Because(error);
		}
	}
}
