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

	// Regex for 256-color ANSI codes (38;5;N for foreground, 48;5;N for background)
	[GeneratedRegex(@"\x1b\[([34])8;5;(\d+)m")]
	private static partial Regex Xterm256ColorRegex();

	/// <summary>24-bit RGB SGR: <c>ESC[38;2;r;g;b m</c> for foreground, <c>48</c> for background.</summary>
	[GeneratedRegex(@"\x1b\[([34])8;2;(\d+);(\d+);(\d+)m")]
	private static partial Regex TruecolorRegex();

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

		// Colour depth is a ladder, and the rungs have to be walked in order. The renderer emits
		// 24-bit RGB freely — every hex ansi() code and every syntax-highlighted help block does —
		// so a client that stops at 256 needs those mapped into the palette before the palette is
		// mapped into the basic sixteen. Skipping a rung leaves sequences the client cannot read.
		if (style != ColorStyles.Truecolor)
		{
			text = DowngradeTruecolorToXterm256(text);
		}

		if (style != ColorStyles.Truecolor && style != ColorStyles.Xterm256)
		{
			text = DowngradeXterm256To16Color(text);
		}

		return text;
	}

	/// <summary>
	/// The depth this connection is rendered at, as one of the <see cref="ColorStyles"/> values.
	/// <para>
	/// A player flag and a negotiated terminal capability are both claims that the client can display
	/// something, so they are unioned: whichever says yes wins, and the deepest rung either of them
	/// reaches is the one used. Neither can veto the other, because a flag that is <i>not</i> set is
	/// indistinguishable from one nobody has thought about — reading absence as "no colour" is what
	/// left every character without both ANSI and COLOR seeing plain text on telnet, MTTS notwithstanding.
	/// Refusing colour is therefore <c>SOCKSET colorstyle</c>'s job, and a pin from it overrides
	/// everything here, including the screen-reader default.
	/// </para>
	/// </summary>
	private static string ResolveColorStyle(ProtocolCapabilities capabilities, PlayerOutputPreferences? preferences)
	{
		if (!string.IsNullOrEmpty(capabilities.ColorStylePin))
		{
			return capabilities.ColorStylePin;
		}

		// Colour means nothing to a screen reader, and it reads the escape bytes aloud.
		if (capabilities.ScreenReader)
		{
			return ColorStyles.Plain;
		}

		var truecolor = capabilities.SupportsTruecolor || preferences?.TruecolorEnabled == true;
		var xterm256 = truecolor || capabilities.SupportsXterm256 || preferences?.Xterm256Enabled == true;
		var color = xterm256 || capabilities.SupportsAnsi || preferences?.ColorEnabled == true;

		// PennMUSH's split: ANSI is "this client can highlight", COLOR is "this client can colour".
		// A player with only ANSI set, on a terminal that claims nothing, gets the attributes and none
		// of the hues.
		var hilite = color || preferences?.AnsiEnabled == true;

		return (truecolor, xterm256, color, hilite) switch
		{
			(true, _, _, _) => ColorStyles.Truecolor,
			(_, true, _, _) => ColorStyles.Xterm256,
			(_, _, true, _) => ColorStyles.SixteenColor,
			(_, _, _, true) => ColorStyles.Hilite,
			_ => ColorStyles.Plain
		};
	}

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
	/// arguments that belong to them. An SGR left with no parameters at all is dropped rather than
	/// emitted as a bare <c>ESC[m</c>, which would read as a reset the sender never asked for.
	/// Sequences that are not SGR — the MXP line modes end in <c>z</c> — are not touched.
	/// </summary>
	private static string StripColorParameters(string text) =>
		SgrRegex().Replace(text, match =>
		{
			var parameters = match.Groups[1].Value;

			// ESC[m is ESC[0m; it carries no colour and has to survive as the reset it is.
			if (parameters.Length == 0)
			{
				return match.Value;
			}

			var parts = parameters.Split(';');
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

				kept.Add(code.ToString(CultureInfo.InvariantCulture));
			}

			return kept.Count == 0 ? string.Empty : $"\x1b[{string.Join(';', kept)}m";
		});

	/// <summary>Foreground, background, their defaults, and the bright aixterm ranges.</summary>
	private static bool IsColorParameter(int code) =>
		code is (>= 30 and <= 39) or (>= 40 and <= 49) or (>= 90 and <= 97) or (>= 100 and <= 107);

	private string DowngradeXterm256To16Color(string text)
	{
		return Xterm256ColorRegex().Replace(text, match =>
		{
			var fgOrBg = match.Groups[1].Value; // "3" for foreground, "4" for background
			var colorCode = int.Parse(match.Groups[2].Value);

			var basicColor = Map256ColorTo16Color(colorCode);

			return $"\x1b[{fgOrBg}{basicColor}m";
		});
	}

	/// <summary>
	/// Rewrites <c>ESC[38;2;r;g;b m</c> as its nearest xterm-256 palette entry, so a client that never
	/// claimed 24-bit colour sees an approximation rather than a sequence it cannot parse.
	/// </summary>
	private string DowngradeTruecolorToXterm256(string text)
	{
		return TruecolorRegex().Replace(text, match =>
		{
			var fgOrBg = match.Groups[1].Value; // "3" for foreground, "4" for background

			// A malformed component is left alone rather than guessed at: dropping the sequence would
			// leave the rest of the line coloured by whatever came before it.
			if (!byte.TryParse(match.Groups[2].Value, out var r)
					|| !byte.TryParse(match.Groups[3].Value, out var g)
					|| !byte.TryParse(match.Groups[4].Value, out var b))
			{
				return match.Value;
			}

			return $"\x1b[{fgOrBg}8;5;{MapRgbTo256Color(r, g, b)}m";
		});
	}

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
