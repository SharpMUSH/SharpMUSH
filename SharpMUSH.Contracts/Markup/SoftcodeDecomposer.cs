using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

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
/// <para>
/// Colour inside colour is written nested, as it was made: <c>[ansi(r,a[ansi(g,b)]c)]</c>. PennMUSH closes
/// and reopens a flat call at each change (<c>[ansi(r,a)][ansi(g,b)][ansi(r,c)]</c>) because its markup has
/// no tree to walk; this one does, and the nested form evaluates to the same text. The escapes, the space
/// rule and the code letters are PennMUSH's.
/// </para>
/// </remarks>
public static partial class SoftcodeDecomposer
{
	/// <inheritdoc cref="SoftcodeDecomposer"/>
	public static string Decompose(MarkupText text)
	{
		var builder = new StringBuilder(text.Length + 16);
		// The calls open around the text written so far, outermost first, with what closes each.
		var open = new List<(IMarkup Markup, string Close)>();
		var position = 0;

		void Write(IReadOnlyList<IMarkup> markups, string segment)
		{
			// The first markup is the innermost.
			var layers = markups.Reverse().Select(m => (Markup: m, Call: Call(m))).Where(l => l.Call is not null).ToList();
			var kept = 0;
			while (kept < open.Count && kept < layers.Count && open[kept].Markup.Equals(layers[kept].Markup)) kept++;
			for (var i = open.Count - 1; i >= kept; i--) builder.Append(open[i].Close);
			open.RemoveRange(kept, open.Count - kept);
			foreach (var (markup, call) in layers.Skip(kept))
			{
				builder.Append(call!.Value.Open);
				open.Add((markup, call.Value.Close));
			}
			builder.Append(Escape(segment));
		}

		foreach (var run in text.Runs)
		{
			if (run.Start > position) Write([], text.Text[position..run.Start]);
			Write(run.Markups, text.Text.Substring(run.Start, run.Length));
			position = run.End;
		}

		Write([], position < text.Length ? text.Text[position..] : string.Empty);
		return builder.ToString();
	}

	/// <summary>
	/// The call that makes <paramref name="markup"/>, split around the text it covers; <c>null</c> for markup
	/// no call writes.
	/// </summary>
	/// <remarks>
	/// Colour is <c>ansi()</c>. A command link (it rides on the colour layer) is <c>cmdlink(text,command[,hint])</c>,
	/// inside the <c>ansi()</c> when the layer has colour too. An address link is <c>tagwrap(a,href="…",text)</c>,
	/// the one call that writes an address. A tag is <c>tagwrap(name[,attributes],text)</c>.
	/// </remarks>
	private static (string Open, string Close)? Call(IMarkup markup)
	{
		switch (markup)
		{
			case AnsiMarkup ansi:
				{
					var style = ansi.Style;
					var codes = AnsiCodes(style);
					var (open, close) = style.LinkUrl is { Length: > 0 } url
						? style.LinkKind == LinkKind.Command
							? ("[cmdlink(", "," + Escape(url)
								+ (style.LinkText is { Length: > 0 } hint && hint != url ? "," + Escape(hint) : "") + ")]")
							: ("[tagwrap(a," + Escape("href=\"" + url.Replace("\"", "%22") + "\"") + ",", ")]")
						: ("", "");
					if (codes.Length > 0)
					{
						open = $"[ansi({codes}," + open;
						close += ")]";
					}
					return open.Length > 0 ? (open, close) : null;
				}
			case HtmlMarkup html:
				return (html.Attributes is { Length: > 0 } attributes
					? $"[tagwrap({html.TagName},{Escape(attributes)},"
					: $"[tagwrap({html.TagName},", ")]");
			default:
				return null;
		}
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
	/// The <c>ansi()</c> codes that produce <paramref name="style"/>, written as PennMUSH's
	/// <c>write_ansi_letters</c> writes them: the attribute letters <c>f h i u</c>, the ones turning an
	/// attribute off <c>F H I U</c>, a palette background
	/// letter, the foreground (a letter, <c>#rrggbb</c> or <c>+xtermN</c>), and a background that is not a
	/// letter after <c>!</c>. <c>[ansi(hBr,x)]</c>, <c>[ansi(#ff0000!#0000ff,x)]</c>. Empty when it sets nothing.
	/// </summary>
	public static string AnsiCodes(AnsiStyle style)
	{
		var codes = new StringBuilder();
		var bright = style.Foreground is AnsiColor.Standard { Bright: true };
		if (style.Blink) codes.Append('f');
		if (style.Bold || bright) codes.Append('h');
		if (style.Inverted) codes.Append('i');
		if (style.Underlined) codes.Append('u');
		if (style.BlinkOff) codes.Append('F');
		if (style.BoldOff) codes.Append('H');
		if (style.InvertedOff) codes.Append('I');
		if (style.UnderlinedOff) codes.Append('U');

		var background = ColorCode(style.Background, isBackground: true);
		if (background.Length == 1) codes.Append(background);
		codes.Append(ColorCode(style.Foreground));
		if (background.Length > 1) codes.Append('!').Append(background);

		// n alone: a span that starts from a clean slate and sets nothing of its own.
		if (codes.Length == 0 && style.Clear) codes.Append('n');
		return codes.ToString();
	}

	/// <summary>The <c>ansi()</c> letter for each standard palette index, foreground and background.</summary>
	private const string ForegroundLetters = "xrgybmcw";
	private const string BackgroundLetters = "XRGYBMCW";

	/// <summary>
	/// One colour as <c>write_ansi_letters</c> writes it: a palette letter (lower case for a foreground, upper
	/// for a background, the terminal default <c>d</c>/<c>D</c>), <c>#rrggbb</c>, or <c>+xtermN</c>. A bright
	/// palette colour is its letter; the brightness is the <c>h</c> <see cref="AnsiCodes"/> writes.
	/// </summary>
	public static string ColorCode(AnsiColor? color, bool isBackground = false) => color switch
	{
		null => string.Empty,
		AnsiColor.Default => isBackground ? "D" : "d",
		// Lower case to match the syntax help and ansi()'s own output.
		AnsiColor.Rgb rgb => $"#{rgb.R:x2}{rgb.G:x2}{rgb.B:x2}",
		AnsiColor.Standard standard => (isBackground ? BackgroundLetters[standard.Index] : ForegroundLetters[standard.Index]).ToString(),
		AnsiColor.Xterm xterm => $"+xterm{xterm.Index}",
		// AnsiColor is a closed hierarchy (Default/Standard/Xterm/Rgb, private constructor); the
		// compiler cannot see that, so this arm exists only to satisfy exhaustiveness.
		_ => throw new UnreachableException($"Unhandled {nameof(AnsiColor)} subtype {color.GetType()}.")
	};
}
