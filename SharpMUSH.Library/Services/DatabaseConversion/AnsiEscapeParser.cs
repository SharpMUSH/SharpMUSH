using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using ANSILibrary;
using MarkupString;
using static MarkupString.MarkupImplementation;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Parses ANSI escape sequences from PennMUSH database text and converts them to MarkupStrings.
/// </summary>
public static class AnsiEscapeParser
{
	// Standard ANSI 256-color palette (indices 0-255)
	private static readonly Color[] Ansi256ColorPalette = BuildAnsi256ColorPalette();

	/// <summary>
	/// Converts text containing ANSI escape sequences to a MarkupString.
	/// Unrecognized escape sequences are stripped from the output.
	/// </summary>
	/// <param name="text">Text potentially containing ANSI escape sequences</param>
	/// <returns>MarkupString with ANSI formatting applied</returns>
	public static MarkupStringModule.MarkupString ConvertAnsiToMarkupString(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return MarkupStringModule.single(string.Empty);
		}

		var segments = new List<MarkupStringModule.MarkupString>();
		var currentState = new AnsiState();
		var position = 0;
		var textBuilder = new StringBuilder();

		while (position < text.Length)
		{
			// Look for ESC character
			if (text[position] == '\x1b' && position + 1 < text.Length)
			{
				// Save accumulated text before escape sequence
				if (textBuilder.Length > 0)
				{
					segments.Add(CreateMarkupStringFromState(textBuilder.ToString(), currentState));
					textBuilder.Clear();
				}

				// Parse escape sequence
				var (newState, consumed) = ParseEscapeSequence(text, position, currentState);
				currentState = newState;
				position += consumed;
			}
			else
			{
				// Regular character
				textBuilder.Append(text[position]);
				position++;
			}
		}

		// Add final text segment
		if (textBuilder.Length > 0)
		{
			segments.Add(CreateMarkupStringFromState(textBuilder.ToString(), currentState));
		}

		// Combine all segments
		if (segments.Count == 0)
		{
			return MarkupStringModule.single(string.Empty);
		}
		else if (segments.Count == 1)
		{
			return segments[0];
		}
		else
		{
			return MarkupStringModule.multiple(segments);
		}
	}

	/// <summary>
	/// Parses an ANSI escape sequence starting at the given position.
	/// Returns the new state and number of characters consumed.
	/// </summary>
	private static (AnsiState newState, int consumed) ParseEscapeSequence(string text, int position, AnsiState currentState)
	{
		// position points to ESC character
		if (position + 1 >= text.Length)
		{
			return (currentState, 1); // Just consume ESC
		}

		var nextChar = text[position + 1];

		// CSI sequences: ESC[
		if (nextChar == '[')
		{
			return ParseCsiSequence(text, position, currentState);
		}

		// OSC sequences: ESC]
		if (nextChar == ']')
		{
			return ParseOscSequence(text, position, currentState);
		}

		// Unknown escape sequence - consume ESC and next character
		return (currentState, 2);
	}

	/// <summary>
	/// Parses a CSI (Control Sequence Introducer) sequence like ESC[...m
	/// </summary>
	private static (AnsiState newState, int consumed) ParseCsiSequence(string text, int position, AnsiState currentState)
	{
		// Find the end of the CSI sequence (should end with 'm' for SGR)
		var endPos = position + 2; // Start after ESC[
		while (endPos < text.Length && !char.IsLetter(text[endPos]))
		{
			endPos++;
		}

		if (endPos >= text.Length)
		{
			return (currentState, text.Length - position); // Consume rest of string
		}

		var finalChar = text[endPos];
		if (finalChar != 'm')
		{
			// Not an SGR sequence - strip it
			return (currentState, endPos - position + 1);
		}

		// Extract parameters between ESC[ and m
		var parameters = text.Substring(position + 2, endPos - position - 2);
		var codes = ParseSgrParameters(parameters);

		// Apply SGR codes to create new state
		var newState = ApplySgrCodes(currentState, codes);

		return (newState, endPos - position + 1);
	}

	/// <summary>
	/// Parses an OSC (Operating System Command) sequence like ESC]...ESC\ or ESC]...BEL
	/// </summary>
	private static (AnsiState newState, int consumed) ParseOscSequence(string text, int position, AnsiState currentState)
	{
		// OSC sequences end with ESC\ or BEL (0x07)
		var endPos = position + 2; // Start after ESC]
		
		while (endPos < text.Length)
		{
			if (text[endPos] == '\x07') // BEL
			{
				endPos++;
				break;
			}
			else if (text[endPos] == '\x1b' && endPos + 1 < text.Length && text[endPos + 1] == '\\')
			{
				endPos += 2;
				break;
			}
			endPos++;
		}

		// Try to parse OSC 8 hyperlink: ESC]8;params;urlESC\ or ESC]8;params;urlBEL
		var oscContent = text.Substring(position + 2, Math.Min(endPos, text.Length) - position - 2);
		var newState = ParseOscHyperlink(oscContent, currentState);

		return (newState, endPos - position);
	}

	/// <summary>
	/// Parses OSC 8 hyperlink sequences
	/// </summary>
	private static AnsiState ParseOscHyperlink(string content, AnsiState currentState)
	{
		// OSC 8 format: 8;params;url
		var parts = content.Split(';');
		if (parts.Length >= 3 && parts[0] == "8")
		{
			var url = parts[2].TrimEnd('\x07', '\\'); // Remove BEL or backslash
			if (string.IsNullOrEmpty(url))
			{
				// Clear hyperlink
				return currentState with { LinkUrl = null };
			}
			else
			{
				// Set hyperlink
				return currentState with { LinkUrl = url };
			}
		}

		return currentState;
	}

	/// <summary>
	/// Parses SGR parameter string into individual codes
	/// </summary>
	private static int[] ParseSgrParameters(string parameters)
	{
		if (string.IsNullOrEmpty(parameters))
		{
			return new[] { 0 }; // Empty means reset
		}

		var parts = parameters.Split(';');
		var codes = new List<int>();

		foreach (var part in parts)
		{
			if (int.TryParse(part, out var code))
			{
				codes.Add(code);
			}
		}

		return codes.Count > 0 ? codes.ToArray() : new[] { 0 };
	}

	/// <summary>
	/// Applies SGR codes to the current state to create a new state
	/// </summary>
	private static AnsiState ApplySgrCodes(AnsiState state, int[] codes)
	{
		var newState = state;

		for (var i = 0; i < codes.Length; i++)
		{
			var code = codes[i];

			switch (code)
			{
				case 0: // Reset
					newState = new AnsiState();
					break;

				case 1: // Bold
					newState = newState with { Bold = true };
					break;

				case 2: // Faint
					newState = newState with { Faint = true };
					break;

				case 3: // Italic
					newState = newState with { Italic = true };
					break;

				case 4: // Underlined
					newState = newState with { Underlined = true };
					break;

				case 5: // Blink
					newState = newState with { Blink = true };
					break;

				case 7: // Inverted
					newState = newState with { Inverted = true };
					break;

				case 9: // StrikeThrough
					newState = newState with { StrikeThrough = true };
					break;

				case 53: // Overlined
					newState = newState with { Overlined = true };
					break;

				// Foreground colors (30-37)
				case >= 30 and <= 37:
					newState = newState with { Foreground = GetAnsiBasicColor(code - 30) };
					break;

				// Background colors (40-47)
				case >= 40 and <= 47:
					newState = newState with { Background = GetAnsiBasicColor(code - 40) };
					break;

				// 256-color or RGB color
				case 38: // Foreground
					if (i + 1 < codes.Length)
					{
						var colorType = codes[i + 1];
						if (colorType == 5 && i + 2 < codes.Length) // 256-color
						{
							var colorIndex = codes[i + 2];
							newState = newState with { Foreground = ANSILibrary.ANSI.AnsiColor.NewRGB(GetAnsi256Color(colorIndex)) };
							i += 2;
						}
						else if (colorType == 2 && i + 4 < codes.Length) // RGB
						{
							var r = (byte)codes[i + 2];
							var g = (byte)codes[i + 3];
							var b = (byte)codes[i + 4];
							newState = newState with { Foreground = ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(r, g, b)) };
							i += 4;
						}
					}
					break;

				case 48: // Background
					if (i + 1 < codes.Length)
					{
						var colorType = codes[i + 1];
						if (colorType == 5 && i + 2 < codes.Length) // 256-color
						{
							var colorIndex = codes[i + 2];
							newState = newState with { Background = ANSILibrary.ANSI.AnsiColor.NewRGB(GetAnsi256Color(colorIndex)) };
							i += 2;
						}
						else if (colorType == 2 && i + 4 < codes.Length) // RGB
						{
							var r = (byte)codes[i + 2];
							var g = (byte)codes[i + 3];
							var b = (byte)codes[i + 4];
							newState = newState with { Background = ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(r, g, b)) };
							i += 4;
						}
					}
					break;

				// 90-97: Bright foreground colors
				case >= 90 and <= 97:
					newState = newState with { Foreground = GetAnsiBrightColor(code - 90) };
					break;

				// 100-107: Bright background colors
				case >= 100 and <= 107:
					newState = newState with { Background = GetAnsiBrightColor(code - 100) };
					break;
			}
		}

		return newState;
	}

	/// <summary>
	/// Creates a MarkupString from text and ANSI state
	/// </summary>
	private static MarkupStringModule.MarkupString CreateMarkupStringFromState(string text, AnsiState state)
	{
		if (state.IsEmpty())
		{
			return MarkupStringModule.single(text);
		}

		// Build the markup based on state, including hyperlink support if present
		AnsiMarkup markup;
		
		if (state.LinkUrl != null)
		{
			// Create markup with hyperlink
			var linkText = Microsoft.FSharp.Core.FSharpOption<string>.Some(text);
			var linkUrl = Microsoft.FSharp.Core.FSharpOption<string>.Some(state.LinkUrl);
			
			markup = AnsiMarkup.Create(
				foreground: state.Foreground,
				background: state.Background,
				linkText: Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Core.FSharpOption<string>>.Some(linkText),
				linkUrl: Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Core.FSharpOption<string>>.Some(linkUrl),
				blink: state.Blink,
				bold: state.Bold,
				clear: false,
				faint: state.Faint,
				inverted: state.Inverted,
				italic: state.Italic,
				overlined: state.Overlined,
				underlined: state.Underlined,
				strikeThrough: state.StrikeThrough
			);
		}
		else
		{
			// Create markup without hyperlink
			markup = AnsiMarkup.Create(
				foreground: state.Foreground,
				background: state.Background,
				blink: state.Blink,
				bold: state.Bold,
				clear: false,
				faint: state.Faint,
				inverted: state.Inverted,
				italic: state.Italic,
				overlined: state.Overlined,
				underlined: state.Underlined,
				strikeThrough: state.StrikeThrough
			);
		}

		return MarkupStringModule.markupSingle(markup, text);
	}

	/// <summary>
	/// Gets basic ANSI color (0-7)
	/// </summary>
	private static ANSILibrary.ANSI.AnsiColor GetAnsiBasicColor(int index)
	{
		return ANSILibrary.ANSI.AnsiColor.NewRGB(GetAnsi256Color(index));
	}

	/// <summary>
	/// Gets bright ANSI color (8-15 in 256-color palette)
	/// </summary>
	private static ANSILibrary.ANSI.AnsiColor GetAnsiBrightColor(int index)
	{
		return ANSILibrary.ANSI.AnsiColor.NewRGB(GetAnsi256Color(index + 8));
	}

	/// <summary>
	/// Gets color from 256-color palette
	/// </summary>
	private static Color GetAnsi256Color(int index)
	{
		if (index < 0 || index >= 256)
		{
			return Color.White;
		}

		return Ansi256ColorPalette[index];
	}

	/// <summary>
	/// Builds the standard ANSI 256-color palette
	/// </summary>
	private static Color[] BuildAnsi256ColorPalette()
	{
		var palette = new Color[256];

		// Colors 0-15: Standard colors
		palette[0] = Color.FromArgb(0, 0, 0);       // Black
		palette[1] = Color.FromArgb(128, 0, 0);     // Red
		palette[2] = Color.FromArgb(0, 128, 0);     // Green
		palette[3] = Color.FromArgb(128, 128, 0);   // Yellow
		palette[4] = Color.FromArgb(0, 0, 128);     // Blue
		palette[5] = Color.FromArgb(128, 0, 128);   // Magenta
		palette[6] = Color.FromArgb(0, 128, 128);   // Cyan
		palette[7] = Color.FromArgb(192, 192, 192); // White

		// Bright colors
		palette[8] = Color.FromArgb(128, 128, 128); // Bright Black (Gray)
		palette[9] = Color.FromArgb(255, 0, 0);     // Bright Red
		palette[10] = Color.FromArgb(0, 255, 0);    // Bright Green
		palette[11] = Color.FromArgb(255, 255, 0);  // Bright Yellow
		palette[12] = Color.FromArgb(0, 0, 255);    // Bright Blue
		palette[13] = Color.FromArgb(255, 0, 255);  // Bright Magenta
		palette[14] = Color.FromArgb(0, 255, 255);  // Bright Cyan
		palette[15] = Color.FromArgb(255, 255, 255);// Bright White

		// Colors 16-231: 216-color cube (6x6x6)
		var index = 16;
		for (var r = 0; r < 6; r++)
		{
			for (var g = 0; g < 6; g++)
			{
				for (var b = 0; b < 6; b++)
				{
					var red = r == 0 ? 0 : 55 + r * 40;
					var green = g == 0 ? 0 : 55 + g * 40;
					var blue = b == 0 ? 0 : 55 + b * 40;
					palette[index++] = Color.FromArgb(red, green, blue);
				}
			}
		}

		// Colors 232-255: Grayscale
		for (var i = 0; i < 24; i++)
		{
			var gray = 8 + i * 10;
			palette[232 + i] = Color.FromArgb(gray, gray, gray);
		}

		return palette;
	}

	/// <summary>
	/// Represents the current ANSI formatting state
	/// </summary>
	private record AnsiState
	{
		public ANSILibrary.ANSI.AnsiColor Foreground { get; init; } = ANSILibrary.ANSI.AnsiColor.NoAnsi;
		public ANSILibrary.ANSI.AnsiColor Background { get; init; } = ANSILibrary.ANSI.AnsiColor.NoAnsi;
		public string? LinkUrl { get; init; }
		public bool Bold { get; init; }
		public bool Faint { get; init; }
		public bool Italic { get; init; }
		public bool Underlined { get; init; }
		public bool Blink { get; init; }
		public bool Inverted { get; init; }
		public bool StrikeThrough { get; init; }
		public bool Overlined { get; init; }

		public bool IsEmpty()
		{
			return Foreground == ANSILibrary.ANSI.AnsiColor.NoAnsi
				&& Background == ANSILibrary.ANSI.AnsiColor.NoAnsi
				&& LinkUrl == null
				&& !Bold && !Faint && !Italic && !Underlined
				&& !Blink && !Inverted && !StrikeThrough && !Overlined;
		}
	}
}
