using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.CodeAnalysis;

/// <summary>
/// Unit tests for <see cref="MushCodeAnalyzer.FormatIndented"/> — the indenting reflow built on
/// <see cref="SoftcodeLayout.Compute"/>, the same engine <c>@examine</c>/<c>@grep</c> use. These
/// use a real <see cref="MUSHCodeParser"/> (not a mock of <see cref="IMUSHCodeParser"/>) so that
/// <c>Tokenize</c> lexes for real, wired to a small in-memory <see cref="FunctionLibraryService"/>
/// fixture that stands in for the live world's library — mirroring
/// <c>MushCodeAnalyzerIntelligenceTests</c>'s <c>AnalyzerWithLibraries</c> pattern. Nothing here
/// needs a database: <see cref="MUSHCodeParser.Tokenize"/> only builds an ANTLR lexer over the
/// input text and never touches <see cref="IOptionsWrapper{T}"/>, the logger, or the service
/// provider, so those three constructor dependencies are mocked and never exercised.
/// <para>
/// Also guards <see cref="MushCodeAnalyzer.Format"/>'s line-preserving contract
/// (<see cref="Format_StillPreservesLineCount"/>) — <c>FormatIndented</c> is new and additive,
/// never a replacement, and the MCP <c>format</c> tool depends on that contract holding.
/// </para>
/// </summary>
public class MushCodeAnalyzerIndentTests
{
	/// <summary>
	/// Registers the real flags of the three functions these tests exercise, not stand-ins:
	/// <c>switch</c> is <c>NoParse</c> with <c>MaxArgs = int.MaxValue</c> (so it evaluates its
	/// arguments — see <see cref="SoftcodeLayout.Classify"/>'s doc comment on that exact case),
	/// <c>words</c> is <c>Regular</c>, and <c>lit</c> is <c>Literal</c> — the call whose contents
	/// must never be broken into.
	/// </summary>
	private static MushCodeAnalyzer AnalyzerWithLibraries()
	{
		var functions = new FunctionLibraryService
		{
			{
				"switch",
				(new FunctionDefinition(
					new SharpFunctionAttribute
					{
						Name = "switch", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse
					},
					_ => default), true)
			},
			{
				"words",
				(new FunctionDefinition(
					new SharpFunctionAttribute
					{
						Name = "words", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular
					},
					_ => default), true)
			},
			{
				"lit",
				(new FunctionDefinition(
					new SharpFunctionAttribute
					{
						Name = "lit", MinArgs = 0, MaxArgs = int.MaxValue,
						Flags = FunctionFlags.Literal | FunctionFlags.NoParse
					},
					_ => default), true)
			}
		};

		// MUSHCodeParser's constructor eagerly resolves seven collaborator services from the
		// provider (MUSHCodeParser.cs:46-52), none of which Tokenize (the only method
		// FormatIndented drives) ever calls — Tokenize only builds an ANTLR lexer over the input
		// text. Registered here purely to satisfy construction.
		var serviceProvider = Substitute.For<IServiceProvider>();
		serviceProvider.GetService(typeof(IMediator)).Returns(Substitute.For<IMediator>());
		serviceProvider.GetService(typeof(INotifyService)).Returns(Substitute.For<INotifyService>());
		serviceProvider.GetService(typeof(IConnectionService)).Returns(Substitute.For<IConnectionService>());
		serviceProvider.GetService(typeof(ILocateService)).Returns(Substitute.For<ILocateService>());
		serviceProvider.GetService(typeof(ICommandDiscoveryService)).Returns(Substitute.For<ICommandDiscoveryService>());
		serviceProvider.GetService(typeof(IAttributeService)).Returns(Substitute.For<IAttributeService>());
		serviceProvider.GetService(typeof(IHookService)).Returns(Substitute.For<IHookService>());

		var parser = new MUSHCodeParser(
			Substitute.For<ILogger<MUSHCodeParser>>(),
			functions,
			new CommandLibraryService(),
			Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
			serviceProvider);

		return new MushCodeAnalyzer(parser);
	}

	[Test]
	public async Task FormatIndented_BreaksLongCalls()
	{
		var analyzer = AnalyzerWithLibraries();
		var result = analyzer.FormatIndented(
			"switch(words(%0),0,nothing at all,1,just one,many words here)", width: 30);

		await Assert.That(result).Contains("\n");
	}

	[Test]
	public async Task FormatIndented_PreservesNonWhitespaceCharacters()
	{
		var analyzer = AnalyzerWithLibraries();
		const string src = "switch(words(%0),0,nothing at all,1,just one,many words here)";
		var result = analyzer.FormatIndented(src, width: 30);

		static string Strip(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
		await Assert.That(Strip(result)).IsEqualTo(Strip(src));
	}

	[Test]
	public async Task FormatIndented_NeverLeavesCloserAloneOnLine()
	{
		var analyzer = AnalyzerWithLibraries();
		const string src = "switch(words(%0),0,nothing at all,1,just one,many words here)";
		var result = analyzer.FormatIndented(src, width: 30);

		// The direct form of SoftcodeLayout's own invariant ("closers cuddle the last item"): a
		// break immediately before a closer would put a literal newline inside the final argument.
		// Checked directly rather than only via the line-level scan below, which only catches a
		// line that is *exactly* a closer and would miss one like ")foo" or one ending in a closer
		// after other content.
		await Assert.That(result).DoesNotContain("\n)");
		await Assert.That(result).DoesNotContain("\n]");
		await Assert.That(result).DoesNotContain("\n}");

		var closers = new[] { ")", "]", "}" };
		foreach (var line in result.Split('\n'))
		{
			var trimmed = line.Trim();
			await Assert.That(closers).DoesNotContain(trimmed);
		}
	}

	[Test]
	public async Task FormatIndented_PreservesCrlfLineEndingsAtBreaks()
	{
		var analyzer = AnalyzerWithLibraries();
		// The leading "\r\n" is what makes code.Contains("\r\n") true and so selects CRLF mode. It
		// sits before a '[' rather than glued onto "switch(" directly: FUNCHAR only matches when
		// the lexer's token-start position lands exactly on an identifier that runs uninterrupted
		// to '(', and a bare prose run (the lexer's catch-all "OTHER" token) is itself greedy
		// across whitespace/newlines — gluing "\r\nswitch(" straight onto prose swallows "switch"
		// into that same prose token, so it never becomes FUNCHAR at all and nothing breaks. A
		// single-character delimiter like '[' (OBRACK) forces a fresh token boundary immediately
		// before "switch", exactly as the equivalence corpus's own
		// "aaaa...\n[switch(1,1,matched,unmatched)]" entry relies on. What's under test is whether
		// the breaks inside switch() use "\r\n" (matching the document) or a bare "\n" (a regression).
		const string src =
			"aaaaaaaaaaaaaaaaaaaaaaaaaa\r\n[switch(words(%0),0,nothing at all,1,just one,many words here)]";
		var result = analyzer.FormatIndented(src, width: 30);

		await Assert.That(result).Contains("\n");
		await Assert.That(result).DoesNotContain("\r\r\n");
		// Removing every correctly-paired CRLF must leave no bare '\n' behind; one surviving would
		// mean some break used "\n" instead of "\r\n", mixing line-ending styles in the same document.
		await Assert.That(result.Replace("\r\n", "")).DoesNotContain("\n");
	}

	[Test]
	public async Task FormatIndented_BreaksAtRootSemicolonOnlyUnderCommandListMode()
	{
		var analyzer = AnalyzerWithLibraries();
		const string src = "@pemit #1=aaaaaaaaaaaaaaaaaaaa;@pemit #1=bbbbbbbbbbbbbbbbbbbb";

		// The default mode (Function) is the conservative dialect: a root ';' is literal text, so
		// this long command-list payload — the common Softcode Editor attribute — must come back
		// byte-identical rather than silently staying on one long line while claiming to format it.
		var asFunction = analyzer.FormatIndented(src, width: 30);
		await Assert.That(asFunction).IsEqualTo(src);

		// Passing MushAnalysisMode.CommandList — what DocumentFormattingHandler now derives via
		// MushParseMode.ForFileName for a .mushcmd/.mush document — makes the root ';' a genuine
		// break position.
		var asCommandList = analyzer.FormatIndented(src, width: 30, mode: MushAnalysisMode.CommandList);
		await Assert.That(asCommandList).Contains("\n");
		await Assert.That(asCommandList.Split('\n')).Count().IsEqualTo(2);
	}

	[Test]
	public async Task FormatIndented_DoesNotBreakInsideLitCall()
	{
		var analyzer = AnalyzerWithLibraries();
		// lit() copies its argument source verbatim (SoftcodeCallKind.CopiesArgumentSource) rather
		// than evaluating it, so SoftcodeLayout.Compute must render it flat unconditionally even
		// though it is far longer than the requested width — breaking inside it would insert a
		// literal newline into program output.
		// <para>
		// The comma below is deliberate, not decorative: a single-argument, comma-free call lexes
		// to exactly three tokens (opener, one contiguous text run, closer) with no candidate break
		// position at all, so a test built on comma-free content would pass even if the classifier
		// were wired to misclassify "lit" as evaluating its arguments — there would be nothing for
		// the bug to break at. With a comma present, the lexer still emits a COMMAWS token (lexing
		// has no notion of "this comma is inside a literal call"), so a misclassified "lit" really
		// would treat it as an argument separator and break there. Verified: swapping the
		// classifier for one that reports every name as EvaluatesArguments turns this test red.
		// </para>
		const string src =
			"lit(short first part, and a very long second part that would definitely need wrapping if evaluated)";
		var result = analyzer.FormatIndented(src, width: 20);

		await Assert.That(result).IsEqualTo(src);
	}

	/// <summary>
	/// Each line is deliberately far wider than <c>FormatIndented</c>'s 78-column default and full of
	/// genuine break positions, so <c>FormatIndented</c> really would reflow it — the assertion below
	/// goes red if <see cref="MushCodeAnalyzer.Format"/>'s body is ever swapped for the indenting one.
	/// A short input (which was here before) breaks nowhere at 78 and so passes either way, asserting
	/// nothing about the contract the MCP <c>format</c> tool depends on.
	/// </summary>
	[Test]
	public async Task Format_StillPreservesLineCount()
	{
		var analyzer = AnalyzerWithLibraries();
		const string wide =
			"switch(words(%0),0,you said absolutely nothing at all,1,you said just one word,many words here)";
		var src = $"{wide}\n{wide}\n{wide}";

		// Guard the guard: if this ever stopped reflowing, the assertion below would be idle again.
		await Assert.That(analyzer.FormatIndented(wide).Split('\n')).Count().IsGreaterThan(1);

		await Assert.That(analyzer.Format(src).Split('\n')).Count().IsEqualTo(3);
	}
}
