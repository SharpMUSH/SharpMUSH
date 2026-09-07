using SharpMUSH.ConnectionServer.Models;
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

	public OutputTransformService(ILogger<OutputTransformService> logger)
	{
		_logger = logger;
	}

	public ValueTask<byte[]> TransformAsync(
		byte[] rawOutput,
		ProtocolCapabilities capabilities,
		PlayerOutputPreferences? preferences)
	{
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

		// Authenticated player flags are explicit preferences and therefore take precedence over
		// inferred terminal capabilities. Before login, terminal negotiation remains the only signal.
		var ansiAllowed = preferences is null
			? capabilities.SupportsAnsi
			: preferences.AnsiEnabled && preferences.ColorEnabled;

		if (!ansiAllowed)
		{
			return StripAnsiCodes(text, capabilities.Format == OutputFormat.Mxp);
		}

		// Colour depth is a ladder, and the rungs have to be walked in order. The renderer emits
		// 24-bit RGB freely — every hex ansi() code and every syntax-highlighted help block does —
		// so a client that stops at 256 needs those mapped into the palette before the palette is
		// mapped into the basic sixteen. Skipping a rung leaves sequences the client cannot read.
		var truecolorAllowed = preferences?.TruecolorEnabled ?? capabilities.SupportsTruecolor;
		var xterm256Allowed = preferences?.Xterm256Enabled ?? capabilities.SupportsXterm256;

		if (!truecolorAllowed)
		{
			text = DowngradeTruecolorToXterm256(text);
		}

		if (!xterm256Allowed)
		{
			text = DowngradeXterm256To16Color(text);
		}

		return text;
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
