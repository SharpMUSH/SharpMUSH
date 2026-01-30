using System.Text;
using System.Text.RegularExpressions;
using SharpMUSH.ConnectionServer.Models;

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
			// Convert to string for processing
			var text = Encoding.UTF8.GetString(rawOutput);

			// Apply transformations in order
			text = ApplyAnsiTransformations(text, capabilities, preferences);
			text = ApplyCharsetTransformations(text, capabilities);

			// Convert back to bytes with appropriate encoding
			var targetEncoding = GetTargetEncoding(capabilities.Charset);
			return targetEncoding.GetBytes(text);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error transforming output");
			// Return original output on error
			return rawOutput;
		}
	}

	private string ApplyAnsiTransformations(
		string text,
		ProtocolCapabilities capabilities,
		PlayerOutputPreferences? preferences)
	{
		// If player preferences exist and either ANSI or COLOR is disabled, strip all ANSI
		if (preferences is { AnsiEnabled: false } or { ColorEnabled: false })
		{
			return StripAnsiCodes(text);
		}

		// If client doesn't support ANSI, strip all codes
		if (!capabilities.SupportsAnsi)
		{
			return StripAnsiCodes(text);
		}

		// If XTERM256 is disabled (either by preference or capability), downgrade to 16-color
		if ((preferences != null && !preferences.Xterm256Enabled) || !capabilities.SupportsXterm256)
		{
			return DowngradeXterm256To16Color(text);
		}

		// No transformation needed
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
		// Remove all ANSI escape sequences
		return AnsiEscapeSequenceRegex().Replace(text, string.Empty);
	}

	private string DowngradeXterm256To16Color(string text)
	{
		// Convert 256-color codes to closest 16-color equivalent
		return Xterm256ColorRegex().Replace(text, match =>
		{
			var fgOrBg = match.Groups[1].Value; // "3" for foreground, "4" for background
			var colorCode = int.Parse(match.Groups[2].Value);

			// Map 256-color code to 16-color code (0-15)
			var basicColor = Map256ColorTo16Color(colorCode);

			// Return ANSI 16-color code
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
			// Direct mapping for standard colors
			return color256;
		}

		if (color256 >= 232)
		{
			// Grayscale: map to black (0), white (7), or bright white (15)
			var gray = color256 - 232;
			if (gray < 8) return 0;      // Black
			if (gray < 20) return 7;     // White
			return 15;                    // Bright white
		}

		// Color cube: extract RGB components and map to nearest 16-color
		var cubeIndex = color256 - 16;
		var r = (cubeIndex / 36) % 6;
		var g = (cubeIndex / 6) % 6;
		var b = cubeIndex % 6;

		// Map to 16-color by checking brightness
		var bright = (r + g + b) > 6;
		
		// Determine primary color
		if (r > g && r > b) return bright ? 9 : 1;   // Red (bright: 9, normal: 1)
		if (g > r && g > b) return bright ? 10 : 2;  // Green (bright: 10, normal: 2)
		if (b > r && b > g) return bright ? 12 : 4;  // Blue (bright: 12, normal: 4)
		if (r == g && r > b) return bright ? 11 : 3; // Yellow (bright: 11, normal: 3)
		if (r == b && r > g) return bright ? 13 : 5; // Magenta (bright: 13, normal: 5)
		if (g == b && g > r) return bright ? 14 : 6; // Cyan (bright: 14, normal: 6)
		
		// Grayscale
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
