using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// PennMUSH's colour arithmetic for <c>colors()</c> (<c>src/markup.c</c>): <c>define_ansi_data</c> reading a colour
/// code into a foreground, a background and styles, and the <c>color_to_hex</c>, <c>ansi_map_16</c> and
/// <c>ansi_map_256</c> downgrades, over the colour table both servers load from <c>colors.json</c>.
/// </summary>
/// <remarks>
/// Penn keeps that table in SQLite keyed by name, so every lookup by RGB value answers the row whose name sorts
/// first (<c>xterm11</c> before <c>yellow</c>), and the nearest-xterm search walks the <c>xterm</c> rows in name
/// order. The name column is <c>COLLATE UINT</c>, which reads a run of digits as a number, so <c>xterm2</c> comes
/// before <c>xterm10</c>. <see cref="Palette"/> keeps the same order.
/// </remarks>
public static partial class PennColors
{
	[Flags]
	public enum Styles
	{
		None = 0,
		Flash = 1,
		Hilite = 2,
		Invert = 4,
		Underscore = 8
	}

	/// <summary>A parsed colour code: <c>ansi_data</c>'s <c>fg</c>, <c>bg</c> and <c>bits</c>.</summary>
	public sealed record Definition(string Foreground, string Background, Styles Bits);

	/// <summary>One row of the colour table, with its RGB value as a number.</summary>
	public sealed record Color(string Name, int Rgb, int Ansi, int Xterm);

	/// <summary>The colour table in Penn's order, and its lookups.</summary>
	public sealed class Palette
	{
		private readonly Dictionary<string, Color> _byName;
		private readonly Dictionary<int, Color> _firstByRgb;

		public Palette(ColorsOptions options)
		{
			Colors = options.Colors
				.Select(c => new Color(c.name, int.Parse(c.rgb.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), c.ansi, c.xterm))
				.OrderBy(c => c.Name, UintCollation.Instance)
				.ToArray();
			_byName = Colors.ToDictionary(c => c.Name, StringComparer.Ordinal);
			_firstByRgb = Colors.GroupBy(c => c.Rgb).ToDictionary(g => g.Key, g => g.First());
			Xterms = Colors.Where(c => c.Name.StartsWith("xterm", StringComparison.Ordinal)).ToArray();
			Named = Colors.Where(c => !c.Name.StartsWith("xterm", StringComparison.Ordinal)).ToArray();
		}

		/// <summary>Every row, by name.</summary>
		public Color[] Colors { get; }

		/// <summary>The <c>named_colors</c> view: every row but <c>xterm0</c>-<c>xterm255</c>, by name.</summary>
		public Color[] Named { get; }

		/// <summary>The <c>xterm</c> rows, by name.</summary>
		public Color[] Xterms { get; }

		/// <summary><c>colorname_lookup</c>: the name lower-cased with its spaces removed.</summary>
		public bool TryFind(string name, out Color color)
			=> _byName.TryGetValue(new string(name.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray()), out color!);

		/// <summary><c>rgb_lookup</c>: the first row by name with this value.</summary>
		public bool TryFind(int rgb, out Color color) => _firstByRgb.TryGetValue(rgb, out color!);
	}

	private static readonly ConditionalWeakTable<ColorsOptions, Palette> Palettes = [];

	/// <summary>The palette for a loaded colour table, built once per table.</summary>
	public static Palette For(ColorsOptions options) => Palettes.GetValue(options, o => new Palette(o));

	/// <summary>Hot pink, which Penn answers for a colour it cannot read.</summary>
	private const int ErrorColor = 0xff69b4;

	/// <summary><c>colormap_16</c>: the eight colours, then their highlighted forms.</summary>
	private static readonly (char Letter, int Hex)[] ColorMap16 =
	[
		('x', 0x000000), ('r', 0xcd0000), ('g', 0x00cd00), ('y', 0xcdcd00),
		('b', 0x0000ee), ('m', 0xcd00cd), ('c', 0x00cdcd), ('w', 0xe5e5e5),
		('x', 0x7f7f7f), ('r', 0xff0000), ('g', 0x00ff00), ('y', 0xffff00),
		('b', 0x5c5cff), ('m', 0xff00ff), ('c', 0x00ffff), ('w', 0xffffff)
	];

	/// <summary>
	/// <c>define_ansi_data</c> (<c>src/markup.c:1511</c>). Old-style letters set styles and the eight colours; after
	/// <c>+</c>, <c>#</c>, <c>&lt;</c>, a digit, <c>/</c> or <c>!</c> the code reads extended colours until the next
	/// space, with <c>/</c> and <c>!</c> moving on to the background.
	/// </summary>
	public static Result<Definition> Define(string code, Palette palette)
	{
		var foreground = string.Empty;
		var background = string.Empty;
		var bits = Styles.None;
		var toBackground = false;
		var extended = false;
		var i = 0;

		while (i < code.Length)
		{
			while (i < code.Length && char.IsWhiteSpace(code[i]))
			{
				i++;
				extended = false;
			}

			if (i >= code.Length)
			{
				break;
			}

			if (!extended)
			{
				switch (code[i])
				{
					case 'n':
						bits = Styles.None;
						foreground = "n";
						background = string.Empty;
						break;
					case 'f': bits |= Styles.Flash; break;
					case 'h': bits |= Styles.Hilite; break;
					case 'i': bits |= Styles.Invert; break;
					case 'u': bits |= Styles.Underscore; break;
					case 'F': bits &= ~Styles.Flash; break;
					case 'H': bits &= ~Styles.Hilite; break;
					case 'I': bits &= ~Styles.Invert; break;
					case 'U': bits &= ~Styles.Underscore; break;
					case 'b' or 'c' or 'g' or 'm' or 'r' or 'w' or 'x' or 'y' or 'd':
						foreground = code[i].ToString();
						break;
					case 'B' or 'C' or 'G' or 'M' or 'R' or 'W' or 'X' or 'Y' or 'D':
						background = code[i].ToString();
						break;
					case '#' or '+' or '/' or '<' or '!' or (>= '0' and <= '9'):
						extended = true;
						toBackground = false;
						continue;
				}

				i++;
				continue;
			}

			if (code[i] is '/' or '!')
			{
				toBackground = true;
				i++;
				continue;
			}

			if (ExtendedColor(code, ref i, palette) is not string color)
			{
				return new Error<string>(ErrorMessages.Returns.InvalidColor);
			}

			if (toBackground)
			{
				background = color;
			}
			else
			{
				foreground = color;
			}
		}

		return new Definition(foreground, background, bits);
	}

	/// <summary>One extended colour, stored as Penn stores it in <c>ansi_data</c>; null when it is not one.</summary>
	private static string? ExtendedColor(string code, ref int i, Palette palette)
	{
		var start = i;
		switch (code[i])
		{
			case '+':
				{
					var name = Token(code, ref i, start + 1).TrimEnd();
					if (!palette.TryFind(name, out _))
					{
						return null;
					}

					return name.StartsWith("xterm", StringComparison.OrdinalIgnoreCase)
						? "+" + name[..Math.Min(8, name.Length)]
						: name.Length > 6
							? $"#{ColorToHex("+" + name, false, palette):x6}"
							: "+" + name;
				}
			case '#':
				{
					var digits = Token(code, ref i, start + 1).TrimEnd();
					return digits.Length == 6 && HexDigits().IsMatch(digits) ? "#" + digits : null;
				}
			case '<':
				{
					while (i < code.Length && code[i] is not ('/' or '!' or '>'))
					{
						i++;
					}

					if (i < code.Length && code[i] == '>')
					{
						i++;
					}

					var angle = code[start..i];
					if (AngleHex().Match(angle) is { Success: true } hex)
					{
						return "#" + hex.Groups[1].Value;
					}

					if (AngleTriple().Match(angle) is not { Success: true } triple)
					{
						return null;
					}

					var parts = Enumerable.Range(1, 3).Select(n => int.Parse(triple.Groups[n].Value, CultureInfo.InvariantCulture)).ToArray();
					return parts.Any(p => p > 255) ? null : $"#{parts[0]:x2}{parts[1]:x2}{parts[2]:x2}";
				}
			case '0' when i + 1 < code.Length && code[i + 1] is 'x' or 'X':
				{
					var digits = Token(code, ref i, start + 2).TrimEnd();
					if (digits.Length == 0 || !HexDigits().IsMatch(digits))
					{
						return null;
					}

					return digits.Length switch
					{
						1 or 2 => $"+xterm{int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture)}",
						// sscanf("%1x%1x%1x") then "#%02x%02x%02x": each digit is a whole channel, 0-15.
						3 => $"#0{digits[0]}0{digits[1]}0{digits[2]}".ToLowerInvariant(),
						6 => "#" + digits.ToLowerInvariant(),
						_ => null
					};
				}
			case >= '0' and <= '9':
				{
					var number = Token(code, ref i, start).TrimEnd();
					return int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var xterm) && xterm is >= 0 and <= 255
						? $"+xterm{xterm}"
						: null;
				}
			default:
				return null;
		}
	}

	/// <summary><c>find_end_of_color</c> without angle brackets: up to a <c>/</c>, <c>!</c> or space.</summary>
	private static string Token(string code, ref int i, int from)
	{
		i = from;
		while (i < code.Length && code[i] is not ('/' or '!') && !char.IsWhiteSpace(code[i]))
		{
			i++;
		}

		return code[from..i];
	}

	/// <summary><c>color_to_hex</c> (<c>src/markup.c:895</c>).</summary>
	public static int ColorToHex(string name, bool hilite, Palette palette) => name switch
	{
		"" => 0,
		['#', .. var digits] => int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : 0,
		['+', .. var colorName] => palette.TryFind(colorName, out var color) ? color.Rgb : ErrorColor,
		[var letter] => Array.FindIndex(ColorMap16, hilite ? 8 : 0, 8, c => c.Letter == char.ToLowerInvariant(letter)) is var index and >= 0
			? ColorMap16[index].Hex
			: ErrorColor,
		_ => ErrorColor
	};

	/// <summary>
	/// <c>ansi_map_16</c> (<c>src/markup.c:973</c>): the 16-colour letter for a colour, upper case for a background,
	/// <c>d</c>/<c>D</c> for the default, and whether a foreground is highlighted.
	/// </summary>
	public static (char Letter, bool Hilite) Map16(string name, bool background, Palette palette)
	{
		// ansi_codes: the eight letters, and nothing for d/D.
		if (name.Length == 1)
		{
			var code = "xrgybmcw".Contains(char.ToLowerInvariant(name[0])) ? char.ToLowerInvariant(name[0]) : 'd';
			return (background ? char.ToUpperInvariant(code) : code, false);
		}

		int ansi;
		if (name.StartsWith("+xter", StringComparison.OrdinalIgnoreCase))
		{
			var xterm = Math.Min(LeadingNumber(name.AsSpan(Math.Min(6, name.Length))), 255);
			ansi = palette.TryFind($"xterm{xterm}", out var row) ? row.Ansi : 0;
		}
		else if (palette.TryFind(ColorToHex(name, false, palette), out var row))
		{
			ansi = row.Ansi;
		}
		else
		{
			var hex = ColorToHex(name, false, palette);
			ansi = Enumerable.Range(0, 8).MinBy(n => Difference(ColorMap16[n].Hex, hex));
		}

		var letter = ColorMap16[ansi & 0xFF].Letter;
		return (background ? char.ToUpperInvariant(letter) : letter, !background && (ansi & 0x0100) != 0);
	}

	/// <summary><c>ansi_map_256</c> (<c>src/markup.c:1043</c>): the nearest xterm colour, the first by name on a tie.</summary>
	public static int Map256(string name, bool hilite, Palette palette)
	{
		if (name.StartsWith("+xterm", StringComparison.OrdinalIgnoreCase))
		{
			return Math.Min(LeadingNumber(name.AsSpan(6)), 255);
		}

		var hex = ColorToHex(name, hilite, palette);
		return palette.TryFind(hex, out var row)
			? row.Xterm
			: palette.Xterms.MinBy(x => Difference(x.Rgb, hex))?.Xterm ?? -1;
	}

	/// <summary>SQLite's <c>uint</c> collation (<c>ext/misc/uint.c</c>): runs of digits compare as numbers.</summary>
	private sealed class UintCollation : IComparer<string>
	{
		public static readonly UintCollation Instance = new();

		public int Compare(string? a, string? b)
		{
			a ??= string.Empty;
			b ??= string.Empty;
			int i = 0, j = 0;
			while (i < a.Length && j < b.Length)
			{
				if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
				{
					while (i < a.Length && a[i] == '0') i++;
					while (j < b.Length && b[j] == '0') j++;
					var k = 0;
					while (i + k < a.Length && char.IsAsciiDigit(a[i + k]) && j + k < b.Length && char.IsAsciiDigit(b[j + k])) k++;
					if (i + k < a.Length && char.IsAsciiDigit(a[i + k])) return 1;
					if (j + k < b.Length && char.IsAsciiDigit(b[j + k])) return -1;
					var digits = string.CompareOrdinal(a, i, b, j, k);
					if (digits != 0) return digits;
					i += k;
					j += k;
				}
				else if (a[i] != b[j])
				{
					return a[i] - b[j];
				}
				else
				{
					i++;
					j++;
				}
			}

			return (a.Length - i) - (b.Length - j);
		}
	}

	/// <summary><c>strtoul</c>: the digits at the start, 0 when there are none.</summary>
	private static int LeadingNumber(ReadOnlySpan<char> text)
	{
		var digits = 0;
		while (digits < text.Length && char.IsAsciiDigit(text[digits]))
		{
			digits++;
		}

		return digits == 0 ? 0 : int.TryParse(text[..digits], CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
	}

	/// <summary><c>hex_difference</c>: the sum of the squared channel differences.</summary>
	private static long Difference(int a, int b)
	{
		long Channel(int shift) => (long)(((a >> shift) & 0xFF) - ((b >> shift) & 0xFF)) * (((a >> shift) & 0xFF) - ((b >> shift) & 0xFF));
		return Channel(0) + Channel(8) + Channel(16);
	}

	[GeneratedRegex(@"\A[0-9A-Fa-f]+\z")]
	private static partial Regex HexDigits();

	[GeneratedRegex(@"\A<\s*#([0-9A-Fa-f]{6})\s*>\s*\z")]
	private static partial Regex AngleHex();

	[GeneratedRegex(@"\A<\s*(\d{1,3})\s+(\d{1,3})\s+(\d{1,3})\s*>\s*\z")]
	private static partial Regex AngleTriple();
}
