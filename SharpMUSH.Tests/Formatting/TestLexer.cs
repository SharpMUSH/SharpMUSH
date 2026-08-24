using Antlr4.Runtime;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// Lexes source to <see cref="TokenInfo"/> without standing up a parser fixture.
/// Mirrors the setup of <c>MUSHCodeParser.Tokenize</c>: the same generated lexer with its
/// <c>ConsoleErrorListener</c> removed, producing the same token types, text and offsets.
/// The span-optimised input stream and token factory used in production are internal, and
/// swapping them for ANTLR's stock equivalents does not change the token sequence.
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
