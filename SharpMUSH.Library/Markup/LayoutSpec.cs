using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// Reads the option lists the layout functions take: <c>key:value</c> pairs separated by spaces, a
/// value in double quotes when it holds a space (<c>open:"&lt;&lt; "</c>). A value keeps its markup,
/// so a border piece can be coloured.
/// </summary>
public static class LayoutSpec
{
	/// <summary>The keys that set a border, shared by <c>box()</c> and <c>rule()</c>.</summary>
	public static readonly IReadOnlySet<string> BorderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"border", "corner", "tl", "tr", "bl", "br", "top", "bottom", "side", "left", "right", "teel", "teer", "open", "close",
	};

	/// <summary>
	/// The pairs in <paramref name="text"/>, in order, keys lower-cased. A pair whose key is not in
	/// <paramref name="allowed"/>, or a token with no colon, is an error naming it.
	/// </summary>
	public static Result<IReadOnlyList<(string Key, MString Value)>> Options(MString text, IReadOnlySet<string> allowed)
	{
		var pairs = new List<(string, MString)>();
		var source = text.Text;
		var position = 0;
		while (position < source.Length)
		{
			while (position < source.Length && source[position] == ' ') position++;
			if (position >= source.Length) break;

			var start = position;
			var quoted = false;
			while (position < source.Length && (quoted || source[position] != ' '))
			{
				if (source[position] == '"') quoted = !quoted;
				position++;
			}

			var token = text.Substring(start, position - start);
			var colon = token.Text.IndexOf(':');
			var key = (colon < 0 ? token.Text : token.Text[..colon]).ToLowerInvariant();
			if (!allowed.Contains(key))
			{
				return new Error<string>($"#-1 UNKNOWN LAYOUT OPTION {key.ToUpperInvariant()}");
			}

			var value = colon < 0 ? MarkupText.Empty : token.Substring(colon + 1);
			if (value.Length >= 2 && value.Text[0] == '"' && value.Text[^1] == '"')
			{
				value = value.Substring(1, value.Length - 2);
			}

			pairs.Add((key, value));
		}

		return pairs;
	}

	/// <summary>
	/// <paramref name="fallback"/> with the border keys in <paramref name="options"/> applied: <c>border</c>
	/// picks a preset first, then each piece replaces its part.
	/// </summary>
	public static Result<BorderStyle> Border(IReadOnlyList<(string Key, MString Value)> options, BorderStyle fallback)
	{
		var style = fallback;
		foreach (var (key, value) in options)
		{
			if (key != "border") continue;
			if (BorderStyle.Preset(value.ToPlainText().Trim()) is not { } preset)
			{
				return new Error<string>("#-1 UNKNOWN BORDER STYLE");
			}
			style = preset;
		}

		foreach (var (key, value) in options)
		{
			style = key switch
			{
				"corner" => style with { TopLeft = value, TopRight = value, BottomLeft = value, BottomRight = value },
				"tl" => style with { TopLeft = value },
				"tr" => style with { TopRight = value },
				"bl" => style with { BottomLeft = value },
				"br" => style with { BottomRight = value },
				"top" => style with { Top = value },
				"bottom" => style with { Bottom = value },
				"side" => style with { Left = value, Right = value },
				"left" => style with { Left = value },
				"right" => style with { Right = value },
				"teel" => style with { TeeLeft = value },
				"teer" => style with { TeeRight = value },
				"open" => style with { TitleOpen = value },
				"close" => style with { TitleClose = value },
				_ => style,
			};
		}

		return style;
	}

	/// <summary><c>left</c>, <c>center</c> or <c>right</c>.</summary>
	public static Alignment? ParseAlignment(MString value) => value.ToPlainText().Trim().ToLowerInvariant() switch
	{
		"left" or "<" => Alignment.Left,
		"center" or "centre" or "-" => Alignment.Center,
		"right" or ">" => Alignment.Right,
		_ => null,
	};

	/// <summary>
	/// A flex item's width: <c>auto</c>, cells (<c>35</c>) or a percentage (<c>40%</c>), optionally after
	/// <c>align()</c>'s justification character (<c>&lt;</c> left, <c>-</c> centre, <c>&gt;</c> right).
	/// </summary>
	public static bool TryItemWidth(string text, out BlockSize size, out Alignment alignment)
	{
		text = text.Trim();
		alignment = Alignment.Left;
		if (text.Length > 0 && text[0] is '<' or '-' or '>')
		{
			alignment = text[0] switch
			{
				'-' => Alignment.Center,
				'>' => Alignment.Right,
				_ => Alignment.Left,
			};
			text = text[1..];
		}
		return BlockSize.TryParse(text, out size);
	}

	/// <summary>Whether <paramref name="value"/> reads as yes: empty (a bare key), <c>yes</c>, <c>on</c>, <c>true</c> or <c>1</c>.</summary>
	public static bool IsYes(MString value) => value.ToPlainText().Trim().ToLowerInvariant() is "" or "yes" or "on" or "true" or "1";
}
