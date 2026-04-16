using ANSILibrary;
using System.Drawing;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	/// <summary>
	/// Converts an HTML tag name and attributes to an <see cref="Ansi"/> markup object.
	/// Returns <c>null</c> when the tag is not recognised.
	/// </summary>
	private Ansi? ConvertHtmlTagToAnsi(string tag, string tagName)
	{
		return tagName switch
		{
			"b" or "strong" => Ansi.Create(foreground: new AnsiColor.RGB(Color.White), bold: true),
			"i" or "em" => Ansi.Create(italic: true),
			"u" => Ansi.Create(underlined: true),
			"s" or "strike" or "del" => Ansi.Create(strikeThrough: true),
			"font" => ParseFontTagToAnsi(tag),
			"span" => ParseSpanTagToAnsi(tag),
			"color" => ParseColorTagToAnsi(tag),
			_ => null
		};
	}

	private string ExtractTagName(string tag)
	{
		var start = tag.StartsWith("</") ? 2 : 1;
		var end = tag.IndexOfAny([' ', '>', '/'], start);
		if (end == -1)
		{
			end = tag.Length;
		}
		return tag[start..end].ToLowerInvariant();
	}

	private Ansi? ParseFontTagToAnsi(string tag)
	{
		// Extract color attribute: <font color="red"> or <font color="#FF0000">
		var colorMatch = ColorAttributeRegex.Match(tag);
		if (colorMatch.Success)
		{
			var color = ParseColorValue(colorMatch.Groups[1].Value);
			if (color.HasValue)
			{
				return Ansi.Create(foreground: new AnsiColor.RGB(color.Value));
			}
		}
		return null;
	}

	private Ansi? ParseSpanTagToAnsi(string tag)
	{
		// Extract style attribute: <span style="color: red"> or <span style="background-color: blue">
		var styleMatch = StyleAttributeRegex.Match(tag);
		if (!styleMatch.Success)
			return null;

		var style = styleMatch.Groups[1].Value;

		var colorMatch = StyleColorRegex.Match(style);
		var bgColorMatch = StyleBackgroundColorRegex.Match(style);

		var fg = colorMatch.Success ? ParseColorValue(colorMatch.Groups[1].Value.Trim()) : null;
		var bg = bgColorMatch.Success ? ParseColorValue(bgColorMatch.Groups[1].Value.Trim()) : null;

		if (fg is null && bg is null)
			return null;

		if (fg.HasValue && bg.HasValue)
			return Ansi.Create(
				foreground: new AnsiColor.RGB(fg.Value),
				background: new AnsiColor.RGB(bg.Value));
		if (fg.HasValue)
			return Ansi.Create(foreground: new AnsiColor.RGB(fg.Value));
		return Ansi.Create(background: new AnsiColor.RGB(bg!.Value));
	}

	private Ansi? ParseColorTagToAnsi(string tag)
	{
		// Extract color value: <color red> or <color #FF0000>
		var match = ColorTagRegex.Match(tag);
		if (match.Success)
		{
			var color = ParseColorValue(match.Groups[1].Value.Trim());
			if (color.HasValue)
				return Ansi.Create(foreground: new AnsiColor.RGB(color.Value));
		}
		return null;
	}

	private Color? ParseColorValue(string colorStr)
	{
		colorStr = colorStr.Trim();

		// Hex color: #RRGGBB or #RGB
		if (colorStr.StartsWith("#"))
		{
			if (colorStr.Length == 7) // #RRGGBB
			{
				if (byte.TryParse(colorStr.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
						byte.TryParse(colorStr.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
						byte.TryParse(colorStr.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
				{
					return Color.FromArgb(r, g, b);
				}
			}
			else if (colorStr.Length == 4) // #RGB - expand each digit
			{
				if (byte.TryParse(colorStr.AsSpan(1, 1), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
						byte.TryParse(colorStr.AsSpan(2, 1), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
						byte.TryParse(colorStr.AsSpan(3, 1), System.Globalization.NumberStyles.HexNumber, null, out var b))
				{
					return Color.FromArgb((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
				}
			}

			return null;
		}

		// Named colors - Color.FromName doesn't throw, but returns invalid color if name doesn't exist
		var namedColor = Color.FromName(colorStr);
		return namedColor.IsKnownColor ? namedColor : null;
	}
}
