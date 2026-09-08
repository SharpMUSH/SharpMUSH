using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.Library.Utilities;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Implementation of output transformation service
/// </summary>
public partial class OutputTransformService : IOutputTransformService
{
	private readonly ILogger<OutputTransformService> _logger;

	// Regex for ANSI escape sequences (ESC[ followed by parameters and a command letter)
	[GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]")]
	private static partial Regex AnsiEscapeSequenceRegex();

	// Regex for OSC 8 hyperlink sequences: ESC]8;;url BEL text ESC]8;; BEL
	// Captures the display text (group 1) so we can preserve it when stripping
	[GeneratedRegex(@"\x1b\]8;;[^\x07]*\x07(.*?)\x1b\]8;;\x07", RegexOptions.Singleline)]
	private static partial Regex Osc8HyperlinkRegex();

	/// <summary>Any SGR sequence, with its parameter list captured for per-parameter filtering.</summary>
	[GeneratedRegex(@"\x1b\[([0-9;]*)m")]
	private static partial Regex SgrRegex();

	public OutputTransformService(ILogger<OutputTransformService> logger)
	{
		_logger = logger;
	}

	public ValueTask<byte[]> TransformAsync(
		byte[] rawOutput,
		ProtocolCapabilities capabilities,
		PlayerOutputPreferences? preferences,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var result = Transform(rawOutput, capabilities, preferences);
		return ValueTask.FromResult(result);
	}

	public byte[] Transform(
		byte[] rawOutput,
		ProtocolCapabilities capabilities,
		PlayerOutputPreferences? preferences)
	{
		try
		{
			var text = Encoding.UTF8.GetString(rawOutput);

			text = ApplyAnsiTransformations(text, capabilities, preferences);
			text = ApplyCharsetTransformations(text, capabilities);

			var targetEncoding = GetTargetEncoding(capabilities.Charset);
			return targetEncoding.GetBytes(text);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error transforming output");
			return rawOutput;
		}
	}

	private string ApplyAnsiTransformations(
		string text,
		ProtocolCapabilities capabilities,
		PlayerOutputPreferences? preferences)
	{
		// Always strip OSC 8 hyperlinks - telnet clients don't support them
		text = StripOsc8Hyperlinks(text);

		var style = ResolveColorStyle(capabilities, preferences);

		if (style == ColorStyles.Plain)
		{
			return StripAnsiCodes(text, capabilities.Format == OutputFormat.Mxp);
		}

		if (style == ColorStyles.Hilite)
		{
			return StripColorParameters(text);
		}

		return DowngradeColors(text, style);
	}

	/// <summary>
	/// The depth this connection is rendered at. The calculation itself lives in
	/// <see cref="TerminalCapabilityReader.ResolveColorStyle"/>, shared with the engine so that what
	/// goes on the wire and what <c>terminfo()</c> and <c>SOCKSET</c> report cannot drift apart.
	/// <para>
	/// A player flag and a negotiated terminal capability are both claims that the client can display
	/// something, so they are unioned: whichever says yes wins, and the deepest rung either of them
	/// reaches is the one used. Neither can veto the other, because a flag that is <i>not</i> set is
	/// indistinguishable from one nobody has thought about — reading absence as "no colour" is what
	/// left every character without both ANSI and COLOR seeing plain text on telnet, MTTS
	/// notwithstanding. Refusing colour is <c>SOCKSET colorstyle</c>'s job, and a pin from it overrides
	/// everything, including the screen-reader default.
	/// </para>
	/// </summary>
	private static string ResolveColorStyle(ProtocolCapabilities capabilities, PlayerOutputPreferences? preferences) =>
		TerminalCapabilityReader.ResolveColorStyle(
			capabilities.ColorStylePin,
			new TerminalCapabilities(
				Ansi: capabilities.SupportsAnsi,
				Xterm256: capabilities.SupportsXterm256,
				Truecolor: capabilities.SupportsTruecolor,
				Utf8: capabilities.SupportsUtf8,
				ScreenReader: capabilities.ScreenReader),
			preferences is null
				? null
				: new PlayerColorFlags(preferences.AnsiEnabled, preferences.ColorEnabled,
					preferences.Xterm256Enabled, preferences.TruecolorEnabled));

	private string ApplyCharsetTransformations(string text, ProtocolCapabilities capabilities)
	{
		// Character set transformations are handled by the encoding conversion
		// when we convert the final text to bytes using GetTargetEncoding()
		return text;
	}

	private string StripAnsiCodes(string text, bool preserveMxp)
	{
		// MXP line modes share CSI syntax with ANSI, but are needed even with colour disabled.
		return AnsiEscapeSequenceRegex().Replace(text, match =>
			preserveMxp && match.Value.EndsWith('z') ? match.Value : string.Empty);
	}

	private string StripOsc8Hyperlinks(string text)
	{
		return Osc8HyperlinkRegex().Replace(text, "$1");
	}

	/// <summary>
	/// The "hilite" style: keep the SGR attributes — bold, underline, reverse and their cancels — and
	/// drop every hue, including the extended <c>38;5;n</c> and <c>38;2;r;g;b</c> forms and the
	/// arguments that belong to them. Sequences that are not SGR — the MXP line modes end in
	/// <c>z</c> — are not touched.
	/// <para>
	/// Stateful, because a reset is only worth sending when something is open. A line the renderer
	/// produced as colour alone leaves a reset behind that now closes nothing, and emitting it would
	/// put a stray escape on the wire of the one kind of client least able to cope with one — the
	/// clients that land on this rung are those that named no colour at all.
	/// </para>
	/// </summary>
	private static string StripColorParameters(string text)
	{
		var attributeOpen = false;

		return SgrRegex().Replace(text, match =>
		{
			var parameters = match.Groups[1].Value;

			// ESC[m is ESC[0m.
			var parts = parameters.Length == 0 ? ["0"] : parameters.Split(';');
			var kept = new List<string>(parts.Length);

			for (var index = 0; index < parts.Length; index++)
			{
				if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
				{
					continue;
				}

				if (code is 38 or 48)
				{
					// The selector says how many arguments follow: 5 is one palette index, 2 is an RGB
					// triple. Skipping them as a unit keeps a stray "5" from being emitted as blink.
					var selector = index + 1 < parts.Length
						&& int.TryParse(parts[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind)
						? kind
						: -1;
					index += selector switch { 5 => 2, 2 => 4, _ => 1 };
					continue;
				}

				if (IsColorParameter(code))
				{
					continue;
				}

				// A reset, or the cancel of one specific attribute: worth sending only if an attribute
				// is actually open. Everything else in range is an attribute being turned on.
				if (code is 0 or (>= 20 and <= 29))
				{
					if (!attributeOpen)
					{
						continue;
					}

					if (code == 0)
					{
						attributeOpen = false;
					}
				}
				else
				{
					attributeOpen = true;
				}

				kept.Add(code.ToString(CultureInfo.InvariantCulture));
			}

			return kept.Count == 0 ? string.Empty : $"\x1b[{string.Join(';', kept)}m";
		});
	}

	/// <summary>Foreground, background, their defaults, and the bright aixterm ranges.</summary>
	private static bool IsColorParameter(int code) =>
		code is (>= 30 and <= 39) or (>= 40 and <= 49) or (>= 90 and <= 97) or (>= 100 and <= 107);

	/// <summary>
	/// Maps every colour in the text down to what <paramref name="style"/> can display: 24-bit RGB
	/// becomes the nearest palette entry, and a palette entry becomes one of the basic sixteen. The
	/// rungs are walked in order, because a client that stops at the palette cannot read an RGB
	/// sequence and a client that stops at sixteen cannot read a palette one.
	/// <para>
	/// Parameter by parameter rather than sequence by sequence, because the renderer emits one SGR per
	/// run carrying everything that changes there — bold underlined white arrives as
	/// <c>ESC[1;4;38;2;255;255;255m</c>, not as three sequences. Matching only a sequence that begins
	/// at <c>38</c> therefore missed every colour that shared its SGR with an attribute, which is most
	/// of them: a player pinned to 16color, or a client that negotiated no more than that, still
	/// received the raw 24-bit escape.
	/// </para>
	/// </summary>
	private static string DowngradeColors(string text, string style)
	{
		if (style == ColorStyles.Truecolor)
		{
			return text;
		}

		var toBasic = style != ColorStyles.Xterm256;

		return SgrRegex().Replace(text, match =>
		{
			var parameters = match.Groups[1].Value;

			if (parameters.Length == 0)
			{
				return match.Value;
			}

			var parts = parameters.Split(';');
			var rewritten = new List<string>(parts.Length);

			for (var index = 0; index < parts.Length; index++)
			{
				if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
						|| code is not (38 or 48))
				{
					rewritten.Add(parts[index]);
					continue;
				}

				var layer = code == 38 ? 3 : 4;
				var selector = Parameter(parts, index + 1);
				var palette = selector switch
				{
					2 when Parameter(parts, index + 2) is >= 0 and <= 255 and var r
								 && Parameter(parts, index + 3) is >= 0 and <= 255 and var g
								 && Parameter(parts, index + 4) is >= 0 and <= 255 and var b
						=> MapRgbTo256Color((byte)r, (byte)g, (byte)b),
					5 => Parameter(parts, index + 1 + 1),
					_ => -1
				};

				// A malformed sequence is left exactly as it was rather than guessed at: dropping it
				// would leave the rest of the line coloured by whatever came before.
				if (palette is < 0 or > 255)
				{
					rewritten.Add(parts[index]);
					continue;
				}

				index += selector switch { 2 => 4, 5 => 2, _ => 0 };
				rewritten.AddRange(toBasic
					? [BasicColorParameter(layer, Map256ColorTo16Color(palette))]
					: new[] { code.ToString(CultureInfo.InvariantCulture), "5", palette.ToString(CultureInfo.InvariantCulture) });
			}

			return rewritten.Count == 0 ? string.Empty : $"\x1b[{string.Join(';', rewritten)}m";
		});

		static int Parameter(string[] parts, int index) =>
			index < parts.Length
			&& int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				? value
				: -1;
	}

	/// <summary>
	/// The SGR parameter for one of the basic sixteen. The bright half is not "3" followed by the
	/// index — that spells 38 and 39, which are the extended-colour introducer and the default
	/// foreground — but the aixterm ranges 90-97 and 100-107.
	/// </summary>
	private static string BasicColorParameter(int layer, int color) =>
		(color < 8
			? (layer == 3 ? 30 : 40) + color
			: (layer == 3 ? 90 : 100) + color - 8)
		.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// The standard xterm-256 quantisation: the 6×6×6 colour cube for anything with a hue, and the
	/// 24-step grey ramp for anything close enough to neutral, which is visibly better than forcing a
	/// grey through the cube's coarse levels.
	/// </summary>
	private static int MapRgbTo256Color(byte r, byte g, byte b)
	{
		// The grey ramp's own spacing: entries 232..255 run 8, 18, 28 ... 238.
		if (Math.Abs(r - g) < 8 && Math.Abs(g - b) < 8 && Math.Abs(r - b) < 8)
		{
			var level = (r + g + b) / 3;

			if (level < 8) return 16; // Cube black; the ramp does not reach it.
			if (level > 238) return 231; // Cube white, likewise.

			return 232 + (level - 8) / 10;
		}

		return 16 + (36 * CubeIndex(r)) + (6 * CubeIndex(g)) + CubeIndex(b);

		// The cube's six levels are 0, 95, 135, 175, 215, 255 — unevenly spaced, so the boundaries are
		// the midpoints between them rather than a division.
		static int CubeIndex(byte component) => component switch
		{
			< 48 => 0,
			< 115 => 1,
			< 155 => 2,
			< 195 => 3,
			< 235 => 4,
			_ => 5
		};
	}

	private static int Map256ColorTo16Color(int color256)
	{
		// 256-color palette:
		// 0-15: Standard colors (map directly)
		// 16-231: 216 color cube (6x6x6)
		// 232-255: Grayscale

		if (color256 < 16)
		{
			return color256;
		}

		if (color256 >= 232)
		{
			// Grayscale: map to black (0), white (7), or bright white (15)
			var gray = color256 - 232;
			if (gray < 8) return 0; // Black
			if (gray < 20) return 7; // White
			return 15; // Bright white
		}

		// Color cube: extract RGB components and map to nearest 16-color
		var cubeIndex = color256 - 16;
		var r = (cubeIndex / 36) % 6;
		var g = (cubeIndex / 6) % 6;
		var b = cubeIndex % 6;

		var bright = (r + g + b) > 6;

		if (r > g && r > b) return bright ? 9 : 1; // Red (bright: 9, normal: 1)
		if (g > r && g > b) return bright ? 10 : 2; // Green (bright: 10, normal: 2)
		if (b > r && b > g) return bright ? 12 : 4; // Blue (bright: 12, normal: 4)
		if (r == g && r > b) return bright ? 11 : 3; // Yellow (bright: 11, normal: 3)
		if (r == b && r > g) return bright ? 13 : 5; // Magenta (bright: 13, normal: 5)
		if (g == b && g > r) return bright ? 14 : 6; // Cyan (bright: 14, normal: 6)

		return bright ? 7 : 0; // White or Black
	}

	private static Encoding GetTargetEncoding(string charset)
	{
		return charset.ToUpperInvariant() switch
		{
			"UTF-8" => Encoding.UTF8,
			"ASCII" => Encoding.ASCII,
			"LATIN-1" or "ISO-8859-1" => Encoding.Latin1,
			_ => Encoding.UTF8 // Default to UTF-8
		};
	}
}
