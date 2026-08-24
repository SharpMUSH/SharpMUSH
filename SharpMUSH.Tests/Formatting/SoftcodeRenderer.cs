using System.Text;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// Renders a token list plus its breaks back to text, so tests can read as before/after. Shared by
/// <see cref="SoftcodeLayoutTests"/> and <see cref="SoftcodeLayoutEquivalenceTests"/> deliberately:
/// the equivalence proof only means anything if the text it evaluates is produced exactly the way
/// the layout tests say a break is rendered.
/// </summary>
internal static class SoftcodeRenderer
{
	/// <summary>
	/// A break trims the trailing whitespace the token absorbed, emits a newline, then the indent.
	/// The re-lex absorbs that newline and indent back into the same token, which is the premise the
	/// whole feature rests on.
	/// </summary>
	public static string Render(IReadOnlyList<TokenInfo> tokens, IReadOnlyList<SoftcodeBreak> breaks)
	{
		var byIndex = breaks.ToDictionary(b => b.TokenIndex, b => b.Indent);
		var sb = new StringBuilder();
		for (var i = 0; i < tokens.Count; i++)
		{
			if (byIndex.TryGetValue(i, out var indent))
			{
				sb.Append(tokens[i].Text.TrimEnd());
				sb.Append('\n').Append(new string(' ', indent));
			}
			else
			{
				sb.Append(tokens[i].Text);
			}
		}

		return sb.ToString();
	}

	/// <summary>Lexes, lays out and renders <paramref name="source"/> at <paramref name="width"/>.</summary>
	public static string Format(string source, int width, Func<string, bool>? isKnownFunction = null)
	{
		var tokens = TestLexer.Lex(source);

		return Render(tokens, SoftcodeLayout.Compute(tokens, width, isKnownFunction: isKnownFunction));
	}
}
