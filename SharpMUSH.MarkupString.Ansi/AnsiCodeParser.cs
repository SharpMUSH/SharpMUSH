using System.Globalization;

namespace MarkupString.Ansi;

/// <summary>
/// Parses ANSI colour/attribute code strings into <see cref="AnsiMarkup"/>.
///
/// <para>
/// Supports the same code syntax as the MUSHCode <c>ansi()</c> function: single-letter codes, hex
/// RGB (<c>#rgb</c> / <c>#rrggbb</c>), xterm palette entries (a bare integer 0–255 or the
/// <c>+xtermN</c> form), and RGB triplets (<c>&lt;r g b&gt;</c>). Named colours (<c>+colorname</c>)
/// are deliberately not handled here: they need runtime configuration the markup layer has no
/// access to.
/// </para>
///
/// <para>
/// Codes are space-separated. Within a token, attribute letters are processed left to right and the
/// <c>h</c> (highlight) modifier raises a following foreground letter to its bright variant, for the
/// rest of that token only. A following background letter has no bright variant — terminals have no
/// "bright background" SGR distinct from bold text — so <c>h</c> there sets
/// <see cref="AnsiStyle.Bold"/> instead and leaves the background colour at its normal intensity. A
/// leading <c>/</c> on a token targets the background. Unrecognised tokens and malformed colours are
/// ignored rather than throwing.
/// </para>
/// </summary>
public static class AnsiCodeParser
{
	private const string XtermPrefix = "+xterm";

	/// <summary>
	/// Splits <paramref name="input"/> on spaces, keeping a <c>&lt;…&gt;</c> group (e.g.
	/// <c>&lt;255 0 0&gt;</c>) together so an RGB triplet is not fragmented. The group may carry the
	/// background marker: <c>/&lt;0 0 255&gt;</c> is one token too.
	/// </summary>
	private static IEnumerable<string> Tokenize(string input)
	{
		var i = 0;
		while (i < input.Length)
		{
			while (i < input.Length && input[i] == ' ')
			{
				i++;
			}

			if (i >= input.Length)
			{
				break;
			}

			var open = input[i] == '/' ? i + 1 : i;
			if (open < input.Length && input[open] == '<')
			{
				var end = input.IndexOf('>', open);
				if (end >= 0)
				{
					yield return input[i..(end + 1)];
					i = end + 1;
					continue;
				}

				// No closing '>' — fall through and consume it as an ordinary token.
			}

			var start = i;
			while (i < input.Length && input[i] != ' ')
			{
				i++;
			}

			yield return input[start..i];
		}
	}

	/// <summary>
	/// Parses a space-separated string of ANSI codes into the markup they describe.
	/// </summary>
	/// <param name="codes">
	/// Tokens in <c>ansi()</c> syntax: single-letter codes (<c>r</c>, <c>hg</c>, <c>/R</c>, …), hex
	/// RGB, xterm integers, or RGB triplets.
	/// </param>
	public static AnsiMarkup Parse(string codes)
	{
		ArgumentNullException.ThrowIfNull(codes);

		AnsiColor? foreground = null;
		AnsiColor? background = null;
		var blink = false;
		var bold = false;
		var clear = false;
		var inverted = false;
		var underlined = false;

		foreach (var token in Tokenize(codes))
		{
			var code = token.AsSpan();
			var isBackground = false;

			if (code.StartsWith("/"))
			{
				isBackground = true;
				code = code[1..];
			}

			if (code.StartsWith("#"))
			{
				if (AnsiColor.TryParseHex(code, out var hex))
				{
					if (isBackground) background = hex;
					else foreground = hex;
				}

				continue;
			}

			if (code.StartsWith("<") && code.EndsWith(">"))
			{
				if (TryParseTriplet(code[1..^1], out var triplet))
				{
					if (isBackground) background = triplet;
					else foreground = triplet;
				}

				continue;
			}

			var isXtermPrefixed = code.StartsWith(XtermPrefix);
			if (isXtermPrefixed || int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
			{
				// A "+xterm…" token is always consumed here, even when its index is malformed or out
				// of range. Falling through to the letter branch would read the x/r/m of the literal
				// prefix as colour codes and paint "+xterm256" magenta.
				var digits = isXtermPrefixed ? code[XtermPrefix.Length..] : code;
				if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
					&& index is >= 0 and < 256)
				{
					if (isBackground) background = new AnsiColor.Xterm((byte)index);
					else foreground = new AnsiColor.Xterm((byte)index);
				}

				continue;
			}

			// Single-letter codes. 'h' is a per-token modifier, so it resets on every token.
			var highlight = false;
			foreach (var chr in code)
			{
				switch (chr)
				{
					case 'h': highlight = true; break;
					case 'H': highlight = false; break;
					case 'i': inverted = true; break;
					case 'I': inverted = false; break;
					case 'f': blink = true; break;
					case 'F': blink = false; break;
					case 'u': underlined = true; break;
					case 'U': underlined = false; break;
					case 'n':
						clear = true;
						foreground = null;
						background = null;
						blink = false;
						bold = false;
						inverted = false;
						underlined = false;
						highlight = false;
						break;
					case 'd': foreground = AnsiColor.Default.Instance; break;
					case 'D': background = AnsiColor.Default.Instance; break;
					case 'x': foreground = new AnsiColor.Standard(0, highlight); break;
					case 'r': foreground = new AnsiColor.Standard(1, highlight); break;
					case 'g': foreground = new AnsiColor.Standard(2, highlight); break;
					case 'y': foreground = new AnsiColor.Standard(3, highlight); break;
					case 'b': foreground = new AnsiColor.Standard(4, highlight); break;
					case 'm': foreground = new AnsiColor.Standard(5, highlight); break;
					case 'c': foreground = new AnsiColor.Standard(6, highlight); break;
					case 'w': foreground = new AnsiColor.Standard(7, highlight); break;
					// 'h' before a background letter is SGR 1 (bold), not a bright background —
					// terminals have no "bright background" attribute distinct from bold text.
					case 'X': background = new AnsiColor.Standard(0, false); bold |= highlight; break;
					case 'R': background = new AnsiColor.Standard(1, false); bold |= highlight; break;
					case 'G': background = new AnsiColor.Standard(2, false); bold |= highlight; break;
					case 'Y': background = new AnsiColor.Standard(3, false); bold |= highlight; break;
					case 'B': background = new AnsiColor.Standard(4, false); bold |= highlight; break;
					case 'M': background = new AnsiColor.Standard(5, false); bold |= highlight; break;
					case 'C': background = new AnsiColor.Standard(6, false); bold |= highlight; break;
					case 'W': background = new AnsiColor.Standard(7, false); bold |= highlight; break;
				}
			}
		}

		return AnsiMarkup.Create(
			foreground: foreground,
			background: background,
			blink: blink,
			bold: bold,
			clear: clear,
			inverted: inverted,
			underlined: underlined);
	}

	private static bool TryParseTriplet(ReadOnlySpan<char> inner, out AnsiColor.Rgb rgb)
	{
		rgb = new AnsiColor.Rgb(0, 0, 0);

		Span<byte> channels = stackalloc byte[3];
		var count = 0;

		foreach (var range in inner.Split(' '))
		{
			var part = inner[range];
			if (part.IsEmpty)
			{
				continue;
			}

			if (count == 3 || !byte.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
			{
				return false;
			}

			channels[count++] = value;
		}

		if (count != 3)
		{
			return false;
		}

		rgb = new AnsiColor.Rgb(channels[0], channels[1], channels[2]);
		return true;
	}
}
