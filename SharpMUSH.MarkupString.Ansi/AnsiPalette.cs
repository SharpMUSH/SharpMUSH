namespace MarkupString.Ansi;

/// <summary>
/// The fixed colour tables behind <see cref="AnsiColor.ToRgb"/>: the sixteen standard VGA colours
/// and the xterm-256 palette.
/// </summary>
public static class AnsiPalette
{
	/// <summary>SGR 30–37 / 40–47, the VGA values every MU* client agrees on.</summary>
	private static readonly AnsiColor.Rgb[] NormalColors =
	[
		new(0, 0, 0),
		new(170, 0, 0),
		new(0, 170, 0),
		new(170, 85, 0),
		new(0, 0, 170),
		new(170, 0, 170),
		new(0, 170, 170),
		new(170, 170, 170)
	];

	/// <summary>SGR 90–97 / 100–107.</summary>
	private static readonly AnsiColor.Rgb[] BrightColors =
	[
		new(85, 85, 85),
		new(255, 85, 85),
		new(85, 255, 85),
		new(255, 255, 85),
		new(85, 85, 255),
		new(255, 85, 255),
		new(85, 255, 255),
		new(255, 255, 255)
	];

	/// <summary>The six intensity steps of the xterm 6x6x6 colour cube.</summary>
	private static ReadOnlySpan<byte> CubeLevels => [0, 95, 135, 175, 215, 255];

	/// <summary>
	/// The standard palette entry for <paramref name="index"/> (0–7), in its normal or bright row.
	/// </summary>
	public static AnsiColor.Rgb Standard(byte index, bool bright)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(index, (byte)7);
		return bright ? BrightColors[index] : NormalColors[index];
	}

	/// <summary>
	/// The xterm-256 palette entry for <paramref name="index"/>: 0–15 are the standard colours,
	/// 16–231 the 6x6x6 cube, 232–255 the 24-step grey ramp.
	/// </summary>
	public static AnsiColor.Rgb Xterm(byte index)
	{
		if (index < 16)
		{
			return Standard((byte)(index & 0x7), index >= 8);
		}

		if (index < 232)
		{
			var offset = index - 16;
			return new AnsiColor.Rgb(CubeLevels[offset / 36], CubeLevels[offset / 6 % 6], CubeLevels[offset % 6]);
		}

		var grey = (byte)(8 + 10 * (index - 232));
		return new AnsiColor.Rgb(grey, grey, grey);
	}
}
