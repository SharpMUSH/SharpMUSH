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

		var tokens = stream.GetTokens()
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

		RewriteOrphanedClosers(tokens, "OBRACK", "CBRACK");
		RewriteOrphanedClosers(tokens, "OBRACE", "CBRACE");

		return tokens;
	}

	/// <summary>
	/// Mirrors <c>MUSHCodeParser.RewriteOrphanedBracketClosers</c> / <c>…BraceClosers</c>, which every
	/// evaluation entry point runs over the token stream before parsing (<c>MUSHCodeParser.cs:353-354</c>,
	/// <c>:531-532</c>, <c>:694-695</c>): a closer at depth 0 has nothing to close and becomes a literal
	/// <c>OTHER</c>.
	/// <para>
	/// Reimplemented rather than called because both methods are <c>internal</c> and take a
	/// <c>BufferedTokenSpanStream</c>, which is also internal, and there is no
	/// <c>InternalsVisibleTo</c> for this assembly. Same algorithm, same depth counting, over the token
	/// type names. Without it a safety corpus would be proving something about a token stream nobody
	/// evaluates.
	/// </para>
	/// </summary>
	private static void RewriteOrphanedClosers(List<TokenInfo> tokens, string opener, string closer)
	{
		var depth = 0;
		for (var i = 0; i < tokens.Count; i++)
		{
			if (tokens[i].Type == opener)
			{
				depth++;
			}
			else if (tokens[i].Type == closer)
			{
				if (depth > 0)
				{
					depth--;
				}
				else
				{
					tokens[i] = tokens[i] with { Type = "OTHER" };
				}
			}
		}
	}
}
