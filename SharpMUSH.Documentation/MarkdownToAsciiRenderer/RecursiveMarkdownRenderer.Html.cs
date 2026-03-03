using ANSILibrary;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	/// <summary>
	/// Parses HTML tags and converts them to ANSI markup.
	/// Supports basic color tags, bold, italic, underline, etc.
	/// </summary>
	private MString ParseHtmlToAnsi(string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
			return MModule.empty();

		var tagName = ExtractTagName(tag);
		var ansiCode = ConvertHtmlTagToAnsiCode(tag, tagName);

		return string.IsNullOrEmpty(ansiCode)
			? MModule.empty()
			: MModule.single(ansiCode);
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

	private string ConvertHtmlTagToAnsiCode(string tag, string tagName)
	{
		// TODO: This should work on MStrings, as such, this is a bad method.
		// This should use recursion instead, and be aware of its contents.
		return tagName switch
		{
			"b" or "strong" => ANSI.Bold + ANSI.Foreground(ANSI.AnsiColor.NewRGB(Color.White)),
			"i" or "em" => ANSI.Italic,
			"u" => ANSI.Underlined,
			"s" or "strike" or "del" => ANSI.StrikeThrough,
			"font" => ParseFontTagToAnsiCode(tag),
			"span" => ParseSpanTagToAnsiCode(tag),
			"color" => ParseColorTagToAnsiCode(tag),
			_ => ""
		};
	}

	private string ParseFontTagToAnsiCode(string tag)
	{
		// Extract color attribute: <font color="red"> or <font color="#FF0000">
		var colorMatch = ColorAttributeRegex.Match(tag);
		if (colorMatch.Success)
		{
			var color = ParseColorValue(colorMatch.Groups[1].Value);
			if (color.HasValue)
			{
				return ANSI.Foreground(ANSI.AnsiColor.NewRGB(color.Value));
			}
		}
		return "";
	}

	private string ParseSpanTagToAnsiCode(string tag)
	{
		// Extract style attribute: <span style="color: red"> or <span style="background-color: blue">
		var styleMatch = StyleAttributeRegex.Match(tag);
		if (styleMatch.Success)
		{
			var style = styleMatch.Groups[1].Value;

			// Parse color
			var colorMatch = StyleColorRegex.Match(style);
			var bgColorMatch = StyleBackgroundColorRegex.Match(style);

			var result = new StringBuilder();

			if (colorMatch.Success)
			{
				var fg = ParseColorValue(colorMatch.Groups[1].Value.Trim());
				if (fg.HasValue)
					result.Append(ANSI.Foreground(ANSI.AnsiColor.NewRGB(fg.Value)));
			}

			if (bgColorMatch.Success)
			{
				var bg = ParseColorValue(bgColorMatch.Groups[1].Value.Trim());
				if (bg.HasValue)
					result.Append(ANSI.Background(ANSI.AnsiColor.NewRGB(bg.Value)));
			}

			return result.ToString();
		}
		return "";
	}

	private string ParseColorTagToAnsiCode(string tag)
	{
		// Extract color value: <color red> or <color #FF0000>
		var match = ColorTagRegex.Match(tag);
		if (match.Success)
		{
			var color = ParseColorValue(match.Groups[1].Value.Trim());
			if (color.HasValue)
				return ANSI.Foreground(ANSI.AnsiColor.NewRGB(color.Value));
		}
		return "";
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
