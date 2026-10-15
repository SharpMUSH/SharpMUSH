using System.Globalization;
using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// Turns the colour TinyMUX stores inside text into ANSI SGR escape sequences, which the importer then
/// reads into <see cref="MarkupString.MarkupText"/> the same way it reads a PennMUSH dump's colour.
/// </summary>
/// <remarks>
/// <para>A version 1 or 2 flatfile stores raw ANSI escapes already, so its text passes through
/// untouched. From version 3 on, TinyMUX keeps colour as Unicode private-use code points:</para>
/// <list type="bullet">
///   <item>U+F500 reset; U+F501 intense; U+F504 underline; U+F505 blink; U+F507 inverse.</item>
///   <item>U+F600 + n and U+F700 + n: foreground and background xterm colour n.</item>
///   <item>Version 3 and 4: U+F0000-U+F05FF set one channel of the colour just named (foreground red,
///   green, blue, then background red, green, blue), each an absolute 0-255 value replacing that
///   channel of the palette colour.</item>
///   <item>Version 5: a pair after the palette colour gives the exact colour,
///   U+F0000 + ((R &gt;&gt; 4) &lt;&lt; 8 | G) then U+F1000 + ((R &amp; 0xF) &lt;&lt; 8 | B) for the
///   foreground, U+F2000 and U+F3000 for the background.</item>
/// </list>
/// <para>Colours 0-15 become the classic 30-37/90-97 (40-47/100-107) codes, so a sixteen-colour client
/// still sees them; 16-255 become <c>38;5;n</c>; an exact colour <c>38;2;r;g;b</c>.</para>
/// </remarks>
public static class TinyMuxColour
{
	private const int Reset = 0xF500;
	private const int Intense = 0xF501;
	private const int Underline = 0xF504;
	private const int Blink = 0xF505;
	private const int Inverse = 0xF507;
	private const int AttributeLast = 0xF5FF;
	private const int ForegroundIndex = 0xF600;
	private const int BackgroundIndex = 0xF700;
	private const int IndexLast = 0xF7FF;
	private const int ChannelFirst = 0xF0000;
	private const int ChannelLast = 0xF3FFF;

	/// <summary>Whether the text holds any TinyMUX colour code point.</summary>
	public static bool HasColour(string text)
	{
		foreach (var rune in text.EnumerateRunes())
		{
			if (IsColour(rune.Value)) return true;
		}

		return false;
	}

	private static bool IsColour(int value)
		=> value is >= Reset and <= IndexLast or >= ChannelFirst and <= ChannelLast;

	/// <summary>The text without any colour, for a name or anything else that holds plain text.</summary>
	public static string Strip(string text)
	{
		if (!HasColour(text)) return text;

		var plain = new StringBuilder(text.Length);
		foreach (var rune in text.EnumerateRunes())
		{
			if (!IsColour(rune.Value)) plain.Append(rune.ToString());
		}

		return plain.ToString();
	}

	/// <summary>
	/// The text with each colour change written as an ANSI escape, for a flatfile of
	/// <paramref name="version"/>.
	/// </summary>
	public static string ToAnsi(string text, int version)
	{
		if (version < 3 || !HasColour(text)) return text;

		var output = new StringBuilder(text.Length + 16);
		var runes = text.EnumerateRunes().Select(rune => rune.Value).ToArray();
		for (var i = 0; i < runes.Length; i++)
		{
			var value = runes[i];
			switch (value)
			{
				case Reset:
					Sgr(output, "0");
					break;
				case Intense:
					Sgr(output, "1");
					break;
				case Underline:
					Sgr(output, "4");
					break;
				case Blink:
					Sgr(output, "5");
					break;
				case Inverse:
					Sgr(output, "7");
					break;
				case > Reset and <= AttributeLast:
					// A code TinyMUX does not use; nothing to show.
					break;
				case >= ForegroundIndex and <= IndexLast:
					var background = value >= BackgroundIndex;
					var index = value - (background ? BackgroundIndex : ForegroundIndex);
					i = ExactColour(runes, i + 1, index, background, version, out var rgb) - 1;
					Sgr(output, rgb is { } exact ? Truecolour(background, exact) : Indexed(background, index));
					break;
				case >= ChannelFirst and <= ChannelLast:
					// A channel with no palette colour before it: TinyMUX starts from black.
					i = ExactColour(runes, i, -1, value >= (version >= 5 ? 0xF2000 : 0xF0300), version, out var alone) - 1;
					if (alone is { } colour)
					{
						Sgr(output, Truecolour(value >= (version >= 5 ? 0xF2000 : 0xF0300), colour));
					}

					break;
				default:
					output.Append(char.ConvertFromUtf32(value));
					break;
			}
		}

		return output.ToString();
	}

	/// <summary>
	/// Reads the channel code points after a palette colour, from <paramref name="at"/>, and returns
	/// where they end. <paramref name="rgb"/> is the exact colour they give, or none when there are none.
	/// </summary>
	private static int ExactColour(int[] runes, int at, int index, bool background, int version,
		out (int R, int G, int B)? rgb)
	{
		rgb = null;
		var (r, g, b) = index is >= 0 and < 256 ? Palette(index) : (0, 0, 0);

		if (version >= 5)
		{
			var first = background ? 0xF2000 : 0xF0000;
			if (at + 1 < runes.Length
				&& runes[at] is var cp1 && cp1 >= first && cp1 < first + 0x1000
				&& runes[at + 1] is var cp2 && cp2 >= first + 0x1000 && cp2 < first + 0x2000)
			{
				var high = cp1 - first;
				var low = cp2 - first - 0x1000;
				rgb = (((high >> 8) << 4) | (low >> 8), high & 0xFF, low & 0xFF);
				return at + 2;
			}

			return at;
		}

		var channelBase = background ? 0xF0300 : 0xF0000;
		var changed = false;
		while (at < runes.Length && runes[at] >= channelBase && runes[at] < channelBase + 0x300)
		{
			var offset = runes[at] - channelBase;
			var channelValue = offset & 0xFF;
			switch (offset >> 8)
			{
				case 0: r = channelValue; break;
				case 1: g = channelValue; break;
				default: b = channelValue; break;
			}

			changed = true;
			at++;
		}

		if (changed)
		{
			rgb = (r, g, b);
		}

		return at;
	}

	private static void Sgr(StringBuilder output, string parameters)
		=> output.Append('\u001b').Append('[').Append(parameters).Append('m');

	private static string Indexed(bool background, int index) => index switch
	{
		< 8 => (index + (background ? 40 : 30)).ToString(CultureInfo.InvariantCulture),
		< 16 => (index - 8 + (background ? 100 : 90)).ToString(CultureInfo.InvariantCulture),
		_ => $"{(background ? 48 : 38)};5;{index}"
	};

	private static string Truecolour(bool background, (int R, int G, int B) rgb)
		=> $"{(background ? 48 : 38)};2;{rgb.R};{rgb.G};{rgb.B}";

	/// <summary>TinyMUX's palette: its own sixteen, then the xterm colour cube and grey ramp.</summary>
	internal static (int R, int G, int B) Palette(int index)
	{
		switch (index)
		{
			case < 16:
				var level = index < 8 ? 187 : 255;
				var dim = index < 8 ? 0 : 85;
				return ((index & 1) != 0 ? level : dim, (index & 2) != 0 ? level : dim, (index & 4) != 0 ? level : dim);
			case < 232:
				int[] steps = [0, 95, 135, 175, 215, 255];
				var cube = index - 16;
				return (steps[cube / 36], steps[cube / 6 % 6], steps[cube % 6]);
			default:
				var grey = 8 + (index - 232) * 10;
				return (grey, grey, grey);
		}
	}
}
