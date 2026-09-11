using System.Text;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Encodes a block of composed prose so the game's parser hands it back unchanged.
/// </summary>
/// <remarks>
/// <para>
/// A composed pose travels to the game as the right-hand side of one command line, and MUSH
/// evaluation is not a transparent pipe. Three things happen to raw text on the way in, and each
/// loses something the author typed:
/// </para>
/// <list type="bullet">
/// <item>a run of spaces is compressed to one, and the spaces that open or close an argument are
/// eaten outright — so an indent, a hanging break, or the gap in an ASCII rule never arrives;</item>
/// <item>a newline would end the command, so it has to travel as a substitution;</item>
/// <item>a semicolon separates one command from the next, so "she paused; then spoke" is delivered
/// as a pose that stops at the semicolon and a nonsense command after it.</item>
/// </list>
/// <para>
/// The substitutions are the escape hatch MUSH provides for exactly this: <c>%b</c> is a space the
/// parser will not touch, <c>%t</c> a tab, <c>%r</c> a line break, <c>%;</c> a literal semicolon.
/// Only whitespace at risk is spelled out — a single space between two words survives on its own and
/// is left alone, which keeps the wire text readable in a log.
/// </para>
/// <para>
/// What is deliberately NOT escaped: <c>%</c>, <c>[</c>, <c>]</c> and the other evaluation
/// characters. Escaping those would also disable the markup a player may legitimately want in a
/// pose (<c>[ansi(r,...)]</c>), which is a policy choice for the game, not something the compose box
/// should decide.
/// </para>
/// </remarks>
public static class MushComposeEncoder
{
	/// <summary>
	/// <paramref name="text"/> with its whitespace and command separators protected. Line endings
	/// are normalised first, so CRLF and CR both become one <c>%r</c>.
	/// </summary>
	public static string Encode(string text)
	{
		if (string.IsNullOrEmpty(text)) return string.Empty;

		var builder = new StringBuilder(text.Length + 16);
		var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');

		var index = 0;
		while (index < normalised.Length)
		{
			var current = normalised[index];
			switch (current)
			{
				case '\n':
					builder.Append("%r");
					index++;
					break;
				case '\t':
					builder.Append("%t");
					index++;
					break;
				case ';':
					builder.Append("%;");
					index++;
					break;
				case ' ':
					var runEnd = index;
					while (runEnd < normalised.Length && normalised[runEnd] == ' ') runEnd++;
					var length = runEnd - index;
					// A lone space between two words is safe; one that opens or closes a line, or any
					// run of two or more, is not.
					var atEdge = index == 0
											 || normalised[index - 1] == '\n'
											 || runEnd == normalised.Length
											 || normalised[runEnd] == '\n';
					if (length == 1 && !atEdge)
					{
						builder.Append(' ');
					}
					else
					{
						builder.Insert(builder.Length, "%b", length);
					}

					index = runEnd;
					break;
				default:
					builder.Append(current);
					index++;
					break;
			}
		}

		return builder.ToString();
	}
}
