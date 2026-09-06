using System.Text.Json;
namespace MarkupString.Ansi;

/// <summary>
/// Reads and writes <see cref="AnsiMarkup"/> in the serializer's envelope, under kind
/// <c>"ansi"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Keys are two characters because a game database holds one of these objects per distinct style
/// per attribute: <c>f</c>/<c>g</c> are the foreground and background, <c>lt</c>/<c>lu</c>/<c>lk</c>
/// the link's hint, target and kind, and the remaining flags (<c>bl bo cl fa in it ov un st</c>)
/// are written as <c>1</c> only when set and read back by presence.
/// </para>
/// <para>
/// A colour is <c>"d"</c> (the terminal default), an integer 0-255 (a palette index: 0-15 is the
/// standard palette, its bright half offset by 8) or <c>"#rrggbb"</c>. Rows written before the
/// colour model became semantic carry the raw SGR bytes as an array instead — <c>[31]</c>,
/// <c>[1,34]</c>, <c>[38,5,200]</c> and friends — and are still read.
/// </para>
/// </remarks>
public sealed class AnsiMarkupCodec : IMarkupCodec
{
	/// <inheritdoc/>
	public string Kind => "ansi";

	/// <inheritdoc/>
	public Type MarkupType => typeof(AnsiMarkup);

	/// <inheritdoc/>
	public void Write(Utf8JsonWriter writer, IMarkup markup)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(markup);

		var style = ((AnsiMarkup)markup).Style;

		WriteColor(writer, "f", style.Foreground);
		WriteColor(writer, "g", style.Background);

		if (style.LinkText is { Length: > 0 }) writer.WriteString("lt", style.LinkText);
		if (style.LinkUrl is { Length: > 0 }) writer.WriteString("lu", style.LinkUrl);
		if (style.LinkKind != LinkKind.Url) writer.WriteNumber("lk", (int)style.LinkKind);

		if (style.Blink) writer.WriteNumber("bl", 1);
		if (style.Bold) writer.WriteNumber("bo", 1);
		if (style.Clear) writer.WriteNumber("cl", 1);
		if (style.Faint) writer.WriteNumber("fa", 1);
		if (style.Inverted) writer.WriteNumber("in", 1);
		if (style.Italic) writer.WriteNumber("it", 1);
		if (style.Overlined) writer.WriteNumber("ov", 1);
		if (style.Underlined) writer.WriteNumber("un", 1);
		if (style.StrikeThrough) writer.WriteNumber("st", 1);
	}

	/// <inheritdoc/>
	public IMarkup Read(JsonElement element) =>
		new AnsiMarkup(new AnsiStyle
		{
			Foreground = ReadColor(element, "f"),
			Background = ReadColor(element, "g"),
			LinkText = ReadString(element, "lt"),
			LinkUrl = ReadString(element, "lu"),
			LinkKind = element.TryGetProperty("lk", out var kind) && kind.ValueKind == JsonValueKind.Number
				&& kind.TryGetInt32(out var value) && value == (int)LinkKind.Command
					? LinkKind.Command
					: LinkKind.Url,
			Blink = element.TryGetProperty("bl", out _),
			Bold = element.TryGetProperty("bo", out _),
			Clear = element.TryGetProperty("cl", out _),
			Faint = element.TryGetProperty("fa", out _),
			Inverted = element.TryGetProperty("in", out _),
			Italic = element.TryGetProperty("it", out _),
			Overlined = element.TryGetProperty("ov", out _),
			Underlined = element.TryGetProperty("un", out _),
			StrikeThrough = element.TryGetProperty("st", out _),
		});

	private static string? ReadString(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static void WriteColor(Utf8JsonWriter writer, string name, AnsiColor? color)
	{
		switch (color)
		{
			case null:
				break;
			case AnsiColor.Default:
				writer.WriteString(name, "d");
				break;
			case AnsiColor.Standard standard:
				writer.WriteNumber(name, standard.Bright ? standard.Index + 8 : standard.Index);
				break;
			case AnsiColor.Xterm xterm:
				writer.WriteNumber(name, xterm.Index);
				break;
			case AnsiColor.Rgb rgb:
				writer.WriteString(name, rgb.ToHex());
				break;
		}
	}

	/// <summary>
	/// Reads a colour in any form the wire has ever carried. Anything unrecognised reads as unset
	/// rather than throwing: losing a colour on one corrupt row beats failing the read that carries
	/// it.
	/// </summary>
	private static AnsiColor? ReadColor(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value)) return null;

		return value.ValueKind switch
		{
			JsonValueKind.String => FromHex(value.GetString()),
			JsonValueKind.Number => value.TryGetInt32(out var index) ? FromPaletteIndex(index) : null,
			JsonValueKind.Array => FromLegacyCodes(value),
			_ => null
		};
	}

	private static AnsiColor? FromHex(string? text)
	{
		if (string.IsNullOrEmpty(text)) return null;
		if (text == "d") return AnsiColor.Default.Instance;

		// Legacy "#rrggbbaa": the alpha channel had no terminal meaning and is dropped.
		var hex = text.Length == 9 && text[0] == '#' ? text.AsSpan(0, 7) : text.AsSpan();

		return AnsiColor.TryParseHex(hex, out var rgb) ? rgb : null;
	}

	/// <summary>
	/// 0-15 names a standard colour (8-15 being its bright half), which keeps the sixteen colours
	/// every client agrees on out of the <c>38;5;n</c> form; everything above is an xterm index.
	/// </summary>
	private static AnsiColor? FromPaletteIndex(int index) => index switch
	{
		< 0 or > 255 => null,
		< 8 => new AnsiColor.Standard((byte)index, false),
		< 16 => new AnsiColor.Standard((byte)(index - 8), true),
		_ => new AnsiColor.Xterm((byte)index)
	};

	private static AnsiColor? FromLegacyCodes(JsonElement array)
	{
		Span<int> codes = stackalloc int[5];
		var count = 0;

		foreach (var entry in array.EnumerateArray())
		{
			if (count == codes.Length) return null;
			if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetInt32(out var code)) return null;
			codes[count++] = code;
		}

		return count switch
		{
			1 => FromSgrCode(codes[0], bright: false),

			// [1,n]: a PennMUSH highlight riding on the colour that follows it.
			2 when codes[0] == 1 => FromSgrCode(codes[1], bright: true),

			3 when codes[0] is 38 or 48 && codes[1] == 5 && codes[2] is >= 0 and <= 255 =>
				new AnsiColor.Xterm((byte)codes[2]),

			5 when codes[0] is 38 or 48 && codes[1] == 2
				&& codes[2] is >= 0 and <= 255 && codes[3] is >= 0 and <= 255 && codes[4] is >= 0 and <= 255 =>
				new AnsiColor.Rgb((byte)codes[2], (byte)codes[3], (byte)codes[4]),

			_ => null
		};
	}

	private static AnsiColor? FromSgrCode(int code, bool bright) => code switch
	{
		>= 30 and <= 37 => new AnsiColor.Standard((byte)(code - 30), bright),
		>= 40 and <= 47 => new AnsiColor.Standard((byte)(code - 40), bright),
		>= 90 and <= 97 when !bright => new AnsiColor.Standard((byte)(code - 90), true),
		>= 100 and <= 107 when !bright => new AnsiColor.Standard((byte)(code - 100), true),
		39 or 49 when !bright => AnsiColor.Default.Instance,
		_ => null
	};
}
