using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests;

/// <summary>
/// Reads assertions off a rendered ANSI byte stream.
/// </summary>
/// <remarks>
/// The renderer emits ONE SGR per run carrying every parameter that changes there, so bold
/// underlined white arrives as <c>ESC[1;4;38;2;255;255;255m</c> and there is no standalone
/// <c>ESC[1m</c> to search the string for. Substring assertions on individual sequences therefore
/// say nothing about whether the attribute is set; these helpers parse the parameter lists instead,
/// which is what those assertions always meant.
/// </remarks>
internal static partial class AnsiStream
{
	[GeneratedRegex("\\u001b\\[([0-9;]*)m")]
	private static partial Regex SgrSequence();

	/// <summary>Whether some SGR in the stream carries <paramref name="parameter"/> as an attribute.</summary>
	public static bool Sets(string ansi, int parameter)
	{
		var wanted = parameter.ToString(CultureInfo.InvariantCulture);
		return SgrSequence().Matches(ansi)
			.Any(m => Attributes(m.Groups[1].Value.Split(';')).Contains(wanted));
	}

	/// <summary>Whether some SGR sets the 24-bit foreground to this colour.</summary>
	public static bool SetsForeground(string ansi, byte r, byte g, byte b) => SetsColor(ansi, "38", r, g, b);

	/// <summary>Whether some SGR sets the 24-bit background to this colour.</summary>
	public static bool SetsBackground(string ansi, byte r, byte g, byte b) => SetsColor(ansi, "48", r, g, b);

	private static bool SetsColor(string ansi, string selector, byte r, byte g, byte b)
	{
		var wanted = new[]
		{
			selector, "2",
			r.ToString(CultureInfo.InvariantCulture),
			g.ToString(CultureInfo.InvariantCulture),
			b.ToString(CultureInfo.InvariantCulture)
		};

		foreach (Match match in SgrSequence().Matches(ansi))
		{
			var codes = match.Groups[1].Value.Split(';');
			for (var i = 0; i + wanted.Length <= codes.Length; i++)
			{
				if (codes.Skip(i).Take(wanted.Length).SequenceEqual(wanted)) return true;
			}
		}

		return false;
	}

	/// <summary>
	/// The plain attribute parameters of one SGR, with the operands of an extended colour
	/// (<c>38;5;n</c>, <c>38;2;r;g;b</c> and the background equivalents) skipped — otherwise a red
	/// component of 1 would read as the bold attribute.
	/// </summary>
	private static IEnumerable<string> Attributes(string[] codes)
	{
		for (var i = 0; i < codes.Length; i++)
		{
			if (codes[i] is "38" or "48" && i + 1 < codes.Length)
			{
				if (codes[i + 1] == "5") { i += 2; continue; }
				if (codes[i + 1] == "2") { i += 4; continue; }
			}

			yield return codes[i];
		}
	}
}
