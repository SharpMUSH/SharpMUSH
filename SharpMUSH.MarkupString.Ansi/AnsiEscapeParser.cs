namespace MarkupString.Ansi;

/// <summary>
/// Reads text that already carries ANSI escape sequences — a PennMUSH database dump, a pasted
/// transcript — into a <see cref="MarkupText"/>. Sequences this does not understand are dropped
/// rather than left in the text, so the result renders cleanly in every format.
/// </summary>
public static class AnsiEscapeParser
{
	private const char Escape = '\u001b';
	private const char Bell = '\u0007';

	/// <summary>
	/// Parses <paramref name="text"/>, turning SGR sequences and OSC 8 hyperlinks into markup and
	/// dropping everything else. Supports SGR 0-9, 22-29, 55, 30-37, 39, 40-47, 49, 90-97, 100-107,
	/// <c>38;5;n</c>, <c>48;5;n</c>, <c>38;2;r;g;b</c> and <c>48;2;r;g;b</c>.
	/// </summary>
	public static MarkupText Parse(string? text)
	{
		if (string.IsNullOrEmpty(text)) return MarkupText.Empty;

		var segments = new List<MarkupText>();
		var style = AnsiStyle.None;
		var position = 0;
		var segmentStart = 0;

		while (position < text.Length)
		{
			if (text[position] != Escape)
			{
				position++;
				continue;
			}

			if (position > segmentStart) segments.Add(Segment(text[segmentStart..position], style));
			position += ReadEscapeSequence(text, position, ref style);
			segmentStart = position;
		}

		if (segmentStart < text.Length) segments.Add(Segment(text[segmentStart..], style));

		return MarkupText.Concat(segments);
	}

	private static MarkupText Segment(string text, in AnsiStyle style) =>
		style.IsNone ? MarkupText.Plain(text) : MarkupText.Wrap(new AnsiMarkup(style), text);

	/// <summary>Applies the sequence starting at <paramref name="position"/> and returns its length.</summary>
	private static int ReadEscapeSequence(string text, int position, ref AnsiStyle style)
	{
		if (position + 1 >= text.Length) return 1;

		return text[position + 1] switch
		{
			'[' => ReadControlSequence(text, position, ref style),
			']' => ReadOperatingSystemCommand(text, position, ref style),
			_ => 2   // Some other two-character escape: dropped.
		};
	}

	/// <summary>
	/// Reads a CSI sequence. Only <c>m</c> (SGR) carries formatting; anything else — a cursor move,
	/// a screen clear — is consumed and dropped.
	/// </summary>
	private static int ReadControlSequence(string text, int position, ref AnsiStyle style)
	{
		var end = position + 2;
		while (end < text.Length && !char.IsLetter(text[end])) end++;

		// Unterminated: the rest of the text is part of the sequence.
		if (end >= text.Length) return text.Length - position;

		if (text[end] == 'm') ApplySgr(text.AsSpan(position + 2, end - position - 2), ref style);

		return end - position + 1;
	}

	/// <summary>Reads an OSC sequence, terminated by BEL or <c>ESC \</c>.</summary>
	private static int ReadOperatingSystemCommand(string text, int position, ref AnsiStyle style)
	{
		var end = position + 2;
		while (end < text.Length)
		{
			if (text[end] == Bell)
			{
				end++;
				break;
			}

			if (text[end] == Escape && end + 1 < text.Length && text[end + 1] == '\\')
			{
				end += 2;
				break;
			}

			end++;
		}

		ApplyHyperlink(text.AsSpan(position + 2, Math.Min(end, text.Length) - position - 2), ref style);
		return end - position;
	}

	/// <summary>
	/// Applies an OSC 8 hyperlink, <c>8;params;url</c>. An empty URL is the closing half of the
	/// pair and clears the link.
	/// </summary>
	private static void ApplyHyperlink(ReadOnlySpan<char> content, ref AnsiStyle style)
	{
		var firstSeparator = content.IndexOf(';');
		if (firstSeparator < 0 || !content[..firstSeparator].SequenceEqual("8")) return;

		var secondSeparator = content[(firstSeparator + 1)..].IndexOf(';');
		if (secondSeparator < 0) return;

		var url = content[(firstSeparator + 1 + secondSeparator + 1)..];

		// The terminator is still on the end when the sequence ran to the end of the text.
		while (url.Length > 0 && (url[^1] == Bell || url[^1] == '\\' || url[^1] == Escape))
			url = url[..^1];

		style = url.IsEmpty
			? style with { LinkUrl = null, LinkText = null, LinkKind = LinkKind.Url }
			: style with { LinkUrl = url.ToString(), LinkKind = LinkKind.Url };
	}

	/// <summary>Applies one SGR sequence's parameters. An empty parameter list means reset.</summary>
	private static void ApplySgr(ReadOnlySpan<char> parameters, ref AnsiStyle style)
	{
		Span<int> codes = stackalloc int[16];
		var count = ReadParameters(parameters, codes);
		if (count == 0)
		{
			style = AnsiStyle.None;
			return;
		}

		for (var i = 0; i < count; i++)
		{
			var code = codes[i];
			switch (code)
			{
				case 0: style = AnsiStyle.None; break;
				case 1: style = style with { Bold = true }; break;
				case 2: style = style with { Faint = true }; break;
				case 3: style = style with { Italic = true }; break;
				case 4: style = style with { Underlined = true }; break;
				case 5: style = style with { Blink = true }; break;
				case 7: style = style with { Inverted = true }; break;
				case 9: style = style with { StrikeThrough = true }; break;
				case 53: style = style with { Overlined = true }; break;

				// 22 clears both intensities, as ECMA-48 has it.
				case 22: style = style with { Bold = false, Faint = false }; break;
				case 23: style = style with { Italic = false }; break;
				case 24: style = style with { Underlined = false }; break;
				case 25: style = style with { Blink = false }; break;
				case 27: style = style with { Inverted = false }; break;
				case 29: style = style with { StrikeThrough = false }; break;
				case 55: style = style with { Overlined = false }; break;

				case >= 30 and <= 37: style = style with { Foreground = new AnsiColor.Standard((byte)(code - 30), false) }; break;
				case 39: style = style with { Foreground = AnsiColor.Default.Instance }; break;
				case >= 40 and <= 47: style = style with { Background = new AnsiColor.Standard((byte)(code - 40), false) }; break;
				case 49: style = style with { Background = AnsiColor.Default.Instance }; break;
				case >= 90 and <= 97: style = style with { Foreground = new AnsiColor.Standard((byte)(code - 90), true) }; break;
				case >= 100 and <= 107: style = style with { Background = new AnsiColor.Standard((byte)(code - 100), true) }; break;

				case 38 or 48:
					var colour = ReadExtendedColor(codes[..count], ref i);
					if (colour is null) break;
					style = code == 38
						? style with { Foreground = colour }
						: style with { Background = colour };
					break;
			}
		}
	}

	/// <summary>
	/// Reads the <c>5;n</c> or <c>2;r;g;b</c> tail of a 38/48 parameter, advancing
	/// <paramref name="index"/> past what it consumed.
	/// </summary>
	private static AnsiColor? ReadExtendedColor(ReadOnlySpan<int> codes, ref int index)
	{
		if (index + 1 >= codes.Length) return null;

		if (codes[index + 1] == 5 && index + 2 < codes.Length)
		{
			var value = codes[index + 2];
			index += 2;
			return value is >= 0 and <= 255 ? new AnsiColor.Xterm((byte)value) : null;
		}

		if (codes[index + 1] == 2 && index + 4 < codes.Length)
		{
			var (r, g, b) = (codes[index + 2], codes[index + 3], codes[index + 4]);
			index += 4;
			return r is >= 0 and <= 255 && g is >= 0 and <= 255 && b is >= 0 and <= 255
				? new AnsiColor.Rgb((byte)r, (byte)g, (byte)b)
				: null;
		}

		return null;
	}

	/// <summary>Splits a <c>;</c>-separated parameter list into <paramref name="codes"/>.</summary>
	private static int ReadParameters(ReadOnlySpan<char> parameters, Span<int> codes)
	{
		var count = 0;
		var start = 0;

		for (var i = 0; i <= parameters.Length && count < codes.Length; i++)
		{
			if (i != parameters.Length && parameters[i] != ';') continue;
			if (i > start && int.TryParse(parameters[start..i], out var code)) codes[count++] = code;
			start = i + 1;
		}

		return count;
	}
}
