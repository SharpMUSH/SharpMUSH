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

		if (preferences is { AnsiEnabled: false } or { ColorEnabled: false })
		{
			return StripAnsiCodes(text);
		}

		if (!capabilities.SupportsAnsi)
		{
			return StripAnsiCodes(text);
		}

		if ((preferences != null && !preferences.Xterm256Enabled) || !capabilities.SupportsXterm256)
		{
			return DowngradeXterm256To16Color(text);
		}

		return text;
	}

	private string ApplyCharsetTransformations(string text, ProtocolCapabilities capabilities)
	{
		// Character set transformations are handled by the encoding conversion
		// when we convert the final text to bytes using GetTargetEncoding()
		return text;
	}

	private string StripAnsiCodes(string text)
	{
		return AnsiEscapeSequenceRegex().Replace(text, string.Empty);
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