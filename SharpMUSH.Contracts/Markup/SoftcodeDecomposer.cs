using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// <c>decompose()</c>: the softcode that, evaluated once, gives back the text it was handed, colours and
/// all — PennMUSH's <c>escape_marked_str</c> (src/markup.c). The game's function and the portal's pose
/// editor both write it here, so a pose taken into the editor comes back the way <c>decompose()</c> says.
/// </summary>
/// <remarks>
/// The text is escaped first and a coloured stretch is wrapped after: <c>[ansi(hr,red\, white)]</c>. The
/// other order escaped the brackets and commas of the <c>ansi()</c> call itself, so it evaluated to the
/// words "ansi(hr,red)" and the colour was gone.
/// </remarks>
public static partial class SoftcodeDecomposer
{
	/// <inheritdoc cref="SoftcodeDecomposer"/>
	public static string Decompose(MarkupText text)
	{
		var builder = new StringBuilder(text.Length + 16);
		var position = 0;

		foreach (var run in text.Runs)
		{
			if (run.Start > position)
			{
				builder.Append(Escape(text.Text[position..run.Start]));
			}

			var segment = text.Text.Substring(run.Start, run.Length);
			var codes = run.Markups.OfType<AnsiMarkup>().Select(a => AnsiCodes(a.Style)).Where(c => c.Length > 0).ToList();
			if (codes.Count == 0)
			{
				builder.Append(Escape(segment));
			}
			else
			{
				var inner = Escape(segment);
				// The first markup is the innermost.
				foreach (var code in codes) inner = $"[ansi({code},{inner})]";
				builder.Append(inner);
			}
			position = run.End;
		}

		if (position < text.Length)
		{
			builder.Append(Escape(text.Text[position..]));
		}

		return builder.ToString();
	}

	/// <summary>
	/// PennMUSH's <c>escaped_chars</c> table (src/tables.c): the characters the parser gives meaning
	/// to, which escape() backslashes and secure() blanks.
	/// </summary>
	[GeneratedRegex(@"[$%(),;\[\\\]^{}]")]
	public static partial Regex SoftcodeSpecial();

	[GeneratedRegex(" +")]
	private static partial Regex Spaces();

	/// <summary>
	/// Plain text as softcode: specials backslashed, spaces the parser would eat spelled <c>%b</c>, line
	/// breaks and tabs as <c>%r</c> and <c>%t</c>.
	/// </summary>
	/// <remarks>
	/// The space rule is <c>escape_marked_str</c>'s, applied to each stretch between markup changes: five or
	/// more become <c>[space(N)]</c>; fewer alternate a literal space and <c>%b</c>, opening with <c>%b</c> at
	/// the start of the stretch, and a run that ends the stretch ends in <c>%b</c>. So a space beside a colour
	/// is <c>%b</c>: <c>a%b[ansi(r,b)]%bc</c>.
	/// </remarks>
	private static string Escape(string text)
	{
		var result = SoftcodeSpecial().Replace(text, @"\$0");
		result = Spaces().Replace(result, m =>
		{
			var spaces = m.Length;
			if (spaces >= 5) return $"[space({spaces})]";

			var sb = new StringBuilder();
			var dospace = m.Index == 0;
			var trailing = m.Index + m.Length == result.Length;

			if (trailing) spaces--; // the last one is the closing %b
			if (spaces > 0 && dospace)
			{
				spaces--;
				sb.Append("%b");
			}
			while (spaces > 0)
			{
				sb.Append(' ');
				spaces--;
				if (spaces > 0)
				{
					spaces--;
					sb.Append("%b");
				}
			}
			if (trailing) sb.Append("%b");
			return sb.ToString();
		});

		return result.Replace("\r\n", "%r").Replace("\r", "%r").Replace("\n", "%r").Replace("\t", "%t");
	}

	/// <summary>
	/// The <c>ansi()</c> codes that produce <paramref name="style"/>, comma-separated; empty when it sets
	/// nothing. A single-letter foreground carries the attribute letters with it (<c>ub</c>, not <c>u,b</c>).
	/// </summary>
	public static string AnsiCodes(AnsiStyle style)
	{
		var attributes = new List<string>();
		var format = (style.Bold ? "h" : "") + (style.Underlined ? "u" : "") + (style.Blink ? "f" : "")
			+ (style.Inverted ? "i" : "");

		var foreground = ColorCode(style.Foreground);
		if (foreground.Length == 1 && format.Length > 0)
		{
			attributes.Add(format + foreground);
		}
		else
		{
			if (format.Length > 0) attributes.Add(format);
			if (foreground.Length > 0) attributes.Add(foreground);
		}

		var background = ColorCode(style.Background, isBackground: true);
		if (background.Length > 0) attributes.Add(background);

		return string.Join(",", attributes);
	}

	/// <summary>The <c>ansi()</c> letter for each standard palette index, foreground and background.</summary>
	private const string ForegroundLetters = "xrgybmcw";
	private const string BackgroundLetters = "XRGYBMCW";

	/// <summary>
	/// Converts an <see cref="AnsiColor"/> back to the PennMUSH <c>ansi()</c> code that produces it.
	/// </summary>
	public static string ColorCode(AnsiColor? color, bool isBackground = false) => color switch
	{
		null => string.Empty,
		AnsiColor.Default => isBackground ? "D" : "d",
		// The leading '#' is what makes this an ansi() hex code; without it the code came back as a
		// letter sequence ("FF0000" reads as bright white, bright magenta, …), so decompose() did not
		// round-trip through ansi(). Lower case to match the syntax help and ansi()'s own output.
		AnsiColor.Rgb rgb => isBackground
			? $"/#{rgb.R:x2}{rgb.G:x2}{rgb.B:x2}"
			: $"#{rgb.R:x2}{rgb.G:x2}{rgb.B:x2}",
		AnsiColor.Standard standard =>
			(standard.Bright ? "h" : string.Empty)
			+ (isBackground ? BackgroundLetters[standard.Index] : ForegroundLetters[standard.Index]),
		AnsiColor.Xterm xterm => isBackground ? $"/+xterm{xterm.Index}" : $"+xterm{xterm.Index}",
		// AnsiColor is a closed hierarchy (Default/Standard/Xterm/Rgb, private constructor); the
		// compiler cannot see that, so this arm exists only to satisfy exhaustiveness.
		_ => throw new UnreachableException($"Unhandled {nameof(AnsiColor)} subtype {color.GetType()}.")
	};
}
