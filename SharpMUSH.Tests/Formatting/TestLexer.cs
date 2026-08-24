using Antlr4.Runtime;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// Lexes source to <see cref="TokenInfo"/> without standing up a parser fixture, for the pure-unit
/// layout tests. Mirrors <c>MUSHCodeParser.Tokenize</c> — the entry point <c>SoftcodeFormatter</c>
/// actually feeds the layout engine from — using the same generated lexer with its
/// <c>ConsoleErrorListener</c> removed. The span-optimised input stream and token factory used in
/// production are internal, and swapping them for ANTLR's stock equivalents does not change the token
/// sequence.
/// <para>
/// <b>No orphaned-closer rewrite here, deliberately.</b> Four of the five lexing sites in
/// <c>MUSHCodeParser</c> run <c>RewriteOrphanedBracketClosers</c>/<c>…BraceClosers</c> before parsing
/// (<c>:353-354</c>, <c>:531-532</c>, <c>:694-695</c>, <c>:795-796</c>), turning a <c>]</c> or
/// <c>}</c> that closes nothing into literal <c>OTHER</c>. <c>Tokenize</c> (<c>:648-681</c>) is the
/// one that does not, so a layout test lexing <em>with</em> the rewrite would be reasoning about a
/// stream the formatter never receives. <c>SoftcodeLayoutEquivalenceTests</c> lexes through the real
/// <c>Tokenize</c> and pins this file's agreement with it, so the two cannot drift.
/// </para>
/// </summary>
public static class TestLexer
{
	public static IReadOnlyList<TokenInfo> Lex(string source)
	{
		var lexer = new SharpMUSHLexer(new AntlrInputStream(source));
		lexer.RemoveErrorListeners();
		var stream = new CommonTokenStream(lexer);
		stream.Fill();

		return stream.GetTokens()
			.Where(t => t.Type != TokenConstants.EOF)
			.Select(t => new TokenInfo
			{
				Type = lexer.Vocabulary.GetSymbolicName(t.Type) ?? "UNKNOWN",
				StartIndex = t.StartIndex,
				EndIndex = t.StopIndex,
				Text = t.Text,
				Line = t.Line,
				Column = t.Column,
				Channel = t.Channel
			})
			.ToList();
	}
}
