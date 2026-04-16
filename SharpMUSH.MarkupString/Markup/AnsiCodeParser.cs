using System;
using System.Collections.Generic;
using System.Drawing;

namespace MarkupString.MarkupImplementation;

/// <summary>
/// Parses ANSI color/attribute code strings into <see cref="AnsiMarkup"/> instances.
///
/// <para>
/// Supports the same code syntax as the MUSHCode <c>ansi()</c> function:
/// single-letter codes, hex RGB (<c>#rrggbb</c>), xterm palette entries
/// (plain integer 0–255 or the <c>+xtermN</c> prefix form), and RGB triplets
/// (<c>&lt;r g b&gt;</c>).  Named colours (<c>+colorname</c>) are intentionally
/// not supported here because they require runtime configuration that is not
/// available at the markup layer.
/// </para>
///
/// <para>
/// Codes are space-separated.  Within each token, attribute letters are
/// processed left-to-right; the <c>h</c> (highlight) modifier raises the
/// following colour code to its bright variant for the remainder of that token.
/// </para>
///
/// <para>
/// A leading <c>/</c> on any token targets the <em>background</em> colour.
/// </para>
/// </summary>
public static class AnsiCodeParser
{
	private static ANSILibrary.AnsiColor AnsiBytes(bool highlight, byte baseCode) =>
		highlight
			? new ANSILibrary.AnsiColor.ANSI([1, baseCode])
			: new ANSILibrary.AnsiColor.ANSI([baseCode]);

	private static ANSILibrary.AnsiColor AnsiByte(byte code) =>
		new ANSILibrary.AnsiColor.ANSI([code]);

	/// <summary>
	/// Splits <paramref name="input"/> on spaces, but treats <c>&lt;…&gt;</c> groups
	/// (e.g. <c>&lt;255 0 0&gt;</c>) as a single token so that RGB triplets are not
	/// fragmented.
	/// </summary>
	private static IEnumerable<string> Tokenize(string input)
	{
		int i = 0;
		while (i < input.Length)
		{
			while (i < input.Length && input[i] == ' ') i++;
			if (i >= input.Length) break;

			if (input[i] == '<')
			{
				int end = input.IndexOf('>', i);
				if (end >= 0)
				{
					yield return input[i..(end + 1)];
					i = end + 1;
					continue;
				}
				// No closing '>' — fall through and consume as a plain token.
			}

			int start = i;
			while (i < input.Length && input[i] != ' ') i++;
			yield return input[start..i];
		}
	}

	/// <summary>
	/// Parses a space-separated string of ANSI codes and returns an
	/// <see cref="AnsiMarkup"/> that represents the combined formatting.
	/// </summary>
	/// <param name="codeString">
	/// Space-separated tokens using the same syntax as the <c>ansi()</c>
	/// MUSHCode function: single-letter codes (<c>r</c>, <c>g</c>, …),
	/// hex RGB (<c>#rrggbb</c>), xterm integers (<c>200</c> or
	/// <c>+xterm200</c>), or RGB triplets (<c>&lt;r g b&gt;</c>).
	/// </param>
	public static AnsiMarkup ParseCodes(string codeString)
	{
		ANSILibrary.AnsiColor foreground = ANSILibrary.AnsiColor.NoAnsi.Instance;
		ANSILibrary.AnsiColor background = ANSILibrary.AnsiColor.NoAnsi.Instance;
		var blink = false;
		var bold = false;
		var clear = false;
		var invert = false;
		var underline = false;

		foreach (var token in Tokenize(codeString))
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
				try
				{
					var color = new ANSILibrary.AnsiColor.RGB(ColorTranslator.FromHtml(code.ToString()));
					if (isBackground) background = color;
					else foreground = color;
				}
				catch { /* ignore invalid hex colour */ }
				continue;
			}

			if (code.StartsWith("<") && code.EndsWith(">"))
			{
				var inner = code[1..^1].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (inner.Length == 3 &&
					int.TryParse(inner[0], out var r) && r >= 0 && r <= 255 &&
					int.TryParse(inner[1], out var g) && g >= 0 && g <= 255 &&
					int.TryParse(inner[2], out var b) && b >= 0 && b <= 255)
				{
					var color = new ANSILibrary.AnsiColor.RGB(Color.FromArgb(r, g, b));
					if (isBackground) background = color;
					else foreground = color;
				}
				continue;
			}

			if ((int.TryParse(code, out var xterm) && xterm >= 0 && xterm < 256) ||
				(code.StartsWith("+xterm") && int.TryParse(code[6..], out xterm) && xterm >= 0 && xterm < 256))
			{
				var xtermByte = (byte)xterm;
				ANSILibrary.AnsiColor color = isBackground
					? new ANSILibrary.AnsiColor.ANSI([48, 5, xtermByte])
					: new ANSILibrary.AnsiColor.ANSI([38, 5, xtermByte]);
				if (isBackground) background = color;
				else foreground = color;
				continue;
			}

			// Single-letter attribute codes — h acts as a per-token modifier
			var curHighlight = false;
			foreach (var chr in code)
			{
				switch (chr)
				{
					case 'h': curHighlight = true;  break;
					case 'H': curHighlight = false; break;
					case 'i': invert = true;        break;
					case 'I': invert = false;       break;
					case 'f': blink = true;         break;
					case 'F': blink = false;        break;
					case 'u': underline = true;     break;
					case 'U': underline = false;    break;
					case 'n':
						clear = true;
						foreground = ANSILibrary.AnsiColor.NoAnsi.Instance;
						background = ANSILibrary.AnsiColor.NoAnsi.Instance;
						blink = false;
						bold = false;
						invert = false;
						underline = false;
						curHighlight = false;
						break;
					case 'd': foreground = AnsiBytes(curHighlight, 39); break;
					case 'x': foreground = AnsiBytes(curHighlight, 30); break;
					case 'r': foreground = AnsiBytes(curHighlight, 31); break;
					case 'g': foreground = AnsiBytes(curHighlight, 32); break;
					case 'y': foreground = AnsiBytes(curHighlight, 33); break;
					case 'b': foreground = AnsiBytes(curHighlight, 34); break;
					case 'm': foreground = AnsiBytes(curHighlight, 35); break;
					case 'c': foreground = AnsiBytes(curHighlight, 36); break;
					case 'w': foreground = AnsiBytes(curHighlight, 37); break;
					case 'D': background = AnsiByte(49);                break;
					case 'X': background = AnsiBytes(curHighlight, 40); break;
					case 'R': background = AnsiBytes(curHighlight, 41); break;
					case 'G': background = AnsiBytes(curHighlight, 42); break;
					case 'Y': background = AnsiBytes(curHighlight, 43); break;
					case 'B': background = AnsiBytes(curHighlight, 44); break;
					case 'M': background = AnsiBytes(curHighlight, 45); break;
					case 'C': background = AnsiBytes(curHighlight, 46); break;
					case 'W': background = AnsiBytes(curHighlight, 47); break;
				}
			}
		}

		return AnsiMarkup.Create(
			foreground: foreground,
			background: background,
			blink: blink,
			bold: bold,
			clear: clear,
			inverted: invert,
			underlined: underline);
	}
}
