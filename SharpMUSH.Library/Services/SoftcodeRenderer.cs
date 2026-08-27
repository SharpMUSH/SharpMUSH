using System.Text;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Renders a token list plus a <see cref="SoftcodeLayout.Compute"/> break list back to plain text,
/// so that a caller with no markup to preserve — <c>MushCodeAnalyzer.FormatIndented</c> in
/// production, <c>SoftcodeLayoutTests</c> and <c>SoftcodeLayoutEquivalenceTests</c> in the
/// equivalence corpus — has exactly one implementation of "how a break is rendered," rather than
/// parallel copies that could silently drift apart.
/// <para>
/// That sharing is load-bearing, not tidiness: <c>SoftcodeLayoutEquivalenceTests</c> proves that
/// re-lexing this method's output evaluates identically to the un-broken source. That proof only
/// covers what production actually ships if production calls this same method — a second,
/// independently-written renderer would run untethered from the corpus that vouches for it.
/// </para>
/// <para>
/// A break trims the trailing whitespace the token absorbed, emits <paramref name="newline"/>, then
/// the indent. The re-lex absorbs that newline and indent back into the same token, which is the
/// premise the whole feature rests on. <paramref name="newline"/> defaults to <c>"\n"</c> — the
/// equivalence corpus and the unit-level layout tests always want that — but
/// <c>MushCodeAnalyzer.FormatIndented</c> passes <c>"\r\n"</c> for a CRLF document, mirroring
/// <c>Format</c>'s own newline-style preservation.
/// </para>
/// </summary>
public static class SoftcodeRenderer
{
	public static string Render(IReadOnlyList<TokenInfo> tokens, IReadOnlyList<SoftcodeBreak> breaks,
		string newline = "\n")
	{
		var byIndex = breaks.ToDictionary(b => b.TokenIndex, b => b.Indent);
		var sb = new StringBuilder();

		for (var i = 0; i < tokens.Count; i++)
		{
			if (byIndex.TryGetValue(i, out var indent))
			{
				sb.Append(tokens[i].Text.TrimEnd());
				sb.Append(newline).Append(' ', indent);
			}
			else
			{
				sb.Append(tokens[i].Text);
			}
		}

		return sb.ToString();
	}
}
