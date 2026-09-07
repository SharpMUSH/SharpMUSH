namespace MarkupString.Ansi;

/// <summary>
/// A terminal colour, described semantically rather than as raw SGR bytes: the terminal's own
/// default, one of the sixteen standard palette entries, an xterm-256 palette index, or a 24-bit
/// RGB triple. The concrete cases are records, so two separately built colours that say the same
/// thing compare equal.
/// </summary>
/// <remarks>
/// The hierarchy is closed — the constructor is private, so <see cref="Default"/>,
/// <see cref="Standard"/>, <see cref="Xterm"/> and <see cref="Rgb"/> are the only cases and a
/// <c>switch</c> over them is exhaustive in practice.
/// </remarks>
public abstract record AnsiColor
{
	private AnsiColor() { }

	/// <summary>The terminal's default foreground/background colour (SGR 39 / 49).</summary>
	public sealed record Default : AnsiColor
	{
		/// <summary>The shared instance. Any <see cref="Default"/> is value-equal to it.</summary>
		public static readonly Default Instance = new();
	}

	/// <summary>
	/// One of the sixteen standard colours. <paramref name="Index"/> is 0–7 (black, red, green,
	/// yellow, blue, magenta, cyan, white); <paramref name="Bright"/> selects the 90–97 / 100–107
	/// row rather than 30–37 / 40–47.
	/// </summary>
	public sealed record Standard(byte Index, bool Bright) : AnsiColor
	{
		private readonly byte _index = ValidateIndex(Index);

		/// <summary>
		/// The palette slot, 0–7. Validated in the setter rather than in an initialiser, because a
		/// <c>with</c> expression copies the backing field and then runs the <c>init</c> accessor —
		/// an initialiser would check the constructor's argument and let <c>colour with { Index = 200 }</c>
		/// through.
		/// </summary>
		public byte Index
		{
			get => _index;
			init => _index = ValidateIndex(value);
		}

		private static byte ValidateIndex(byte index) =>
			index <= 7
				? index
				: throw new ArgumentOutOfRangeException(nameof(index), index, "A standard colour index must be 0-7.");
	}

	/// <summary>An xterm-256 palette index.</summary>
	public sealed record Xterm(byte Index) : AnsiColor;

	/// <summary>A 24-bit colour.</summary>
	public sealed record Rgb(byte R, byte G, byte B) : AnsiColor;

	private static readonly Rgb Black = new(0, 0, 0);

	/// <summary>
	/// Resolves this colour to 24-bit RGB, looking the palette entry up where needed.
	/// Returns <see langword="null"/> for <see cref="Default"/>, whose value only the terminal knows.
	/// </summary>
	public Rgb? ToRgb() => this switch
	{
		Rgb rgb => rgb,
		Standard standard => AnsiPalette.Standard(standard.Index, standard.Bright),
		Xterm xterm => AnsiPalette.Xterm(xterm.Index),
		_ => null
	};

	/// <summary>
	/// This colour as a lowercase <c>#rrggbb</c> string, or the empty string for
	/// <see cref="Default"/>, which has no fixed value.
	/// </summary>
	public string ToHex()
	{
		if (ToRgb() is not { } rgb)
		{
			return string.Empty;
		}

		Span<char> buffer = stackalloc char[7];
		buffer[0] = '#';
		WriteHexByte(rgb.R, buffer[1..]);
		WriteHexByte(rgb.G, buffer[3..]);
		WriteHexByte(rgb.B, buffer[5..]);
		return new string(buffer);
	}

	/// <summary>
	/// Parses <c>#rgb</c> or <c>#rrggbb</c> (either case) into 24-bit RGB. Never throws; anything
	/// else — including a missing <c>#</c> — returns <see langword="false"/> with
	/// <paramref name="rgb"/> set to black.
	/// </summary>
	public static bool TryParseHex(ReadOnlySpan<char> hex, out Rgb rgb)
	{
		rgb = Black;

		if (hex.Length is not (4 or 7) || hex[0] != '#')
		{
			return false;
		}

		var body = hex[1..];

		if (body.Length == 3)
		{
			if (!TryReadNibble(body[0], out var r) ||
				!TryReadNibble(body[1], out var g) ||
				!TryReadNibble(body[2], out var b))
			{
				return false;
			}

			// #abc means #aabbcc: each nibble is doubled, i.e. multiplied by 0x11.
			rgb = new Rgb((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
			return true;
		}

		if (!TryReadHexByte(body[0], body[1], out var red) ||
			!TryReadHexByte(body[2], body[3], out var green) ||
			!TryReadHexByte(body[4], body[5], out var blue))
		{
			return false;
		}

		rgb = new Rgb(red, green, blue);
		return true;
	}

	/// <summary>The standard-palette entry closest to <paramref name="color"/> by redmean distance.</summary>
	public static Standard NearestStandard(Rgb color)
	{
		ArgumentNullException.ThrowIfNull(color);

		var bestIndex = (byte)0;
		var bestBright = false;
		var bestDistance = double.MaxValue;

		for (var bright = 0; bright < 2; bright++)
		{
			for (byte index = 0; index < 8; index++)
			{
				var distance = RedmeanDistance(color, AnsiPalette.Standard(index, bright == 1));
				if (distance >= bestDistance)
				{
					continue;
				}

				bestDistance = distance;
				bestIndex = index;
				bestBright = bright == 1;
			}
		}

		return new Standard(bestIndex, bestBright);
	}

	/// <summary>The xterm-256 palette index closest to <paramref name="color"/> by redmean distance.</summary>
	public static Xterm NearestXtermIndex(Rgb color)
	{
		ArgumentNullException.ThrowIfNull(color);

		var bestIndex = (byte)0;
		var bestDistance = double.MaxValue;

		for (var index = 0; index < 256; index++)
		{
			var distance = RedmeanDistance(color, AnsiPalette.Xterm((byte)index));
			if (distance >= bestDistance)
			{
				continue;
			}

			bestDistance = distance;
			bestIndex = (byte)index;
		}

		return new Xterm(bestIndex);
	}

	/// <summary>
	/// The RGB value of the xterm-256 entry closest to <paramref name="color"/> — what a client
	/// limited to the 256-colour palette will actually show.
	/// </summary>
	public static Rgb NearestXterm(Rgb color) => AnsiPalette.Xterm(NearestXtermIndex(color).Index);

	/// <summary>
	/// Weighted "redmean" distance — a cheap approximation of perceived colour difference that is
	/// much better than plain RGB distance near the red end. Returned squared; only the ordering
	/// matters to the callers.
	/// </summary>
	private static double RedmeanDistance(Rgb left, Rgb right)
	{
		var redMean = (left.R + right.R) / 2.0;
		double deltaR = left.R - right.R;
		double deltaG = left.G - right.G;
		double deltaB = left.B - right.B;

		return (2.0 + redMean / 256.0) * deltaR * deltaR
			+ 4.0 * deltaG * deltaG
			+ (2.0 + (255.0 - redMean) / 256.0) * deltaB * deltaB;
	}

	private static bool TryReadNibble(char c, out int value)
	{
		value = c switch
		{
			>= '0' and <= '9' => c - '0',
			>= 'a' and <= 'f' => c - 'a' + 10,
			>= 'A' and <= 'F' => c - 'A' + 10,
			_ => -1
		};

		return value >= 0;
	}

	private static bool TryReadHexByte(char high, char low, out byte value)
	{
		value = 0;

		if (!TryReadNibble(high, out var h) || !TryReadNibble(low, out var l))
		{
			return false;
		}

		value = (byte)((h << 4) | l);
		return true;
	}

	private static void WriteHexByte(byte value, Span<char> destination)
	{
		const string Digits = "0123456789abcdef";
		destination[0] = Digits[value >> 4];
		destination[1] = Digits[value & 0xF];
	}
}
