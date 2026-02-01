using System.Text;

namespace SharpMUSH.Server.Helpers;

/// <summary>
/// Provides utilities for sanitizing user input before logging to prevent log injection attacks.
/// </summary>
public static class LogSanitizer
{
	/// <summary>
	/// Maximum length for sanitized log values before truncation.
	/// </summary>
	private const int MaxLogLength = 200;

	/// <summary>
	/// Sanitizes a string value for safe inclusion in log messages.
	/// Removes control characters, newlines, and truncates to a reasonable length
	/// to prevent log injection and log flooding attacks.
	/// </summary>
	/// <param name="input">The user input to sanitize.</param>
	/// <returns>A sanitized string safe for logging, or "[null]" if input is null.</returns>
	public static string Sanitize(string? input)
	{
		if (input == null)
			return "[null]";

		if (string.IsNullOrWhiteSpace(input))
			return "[empty]";

		// Remove control characters and normalize whitespace
		var sanitized = new StringBuilder(input.Length);
		foreach (var c in input)
		{
			switch (c)
			{
				case '\n':
				case '\r':
					// Replace newlines with escaped representation to prevent log forging
					sanitized.Append("\\n");
					break;
				case '\t':
					// Replace tabs with spaces
					sanitized.Append(' ');
					break;
				default:
					// Include only printable characters
					if (!char.IsControl(c))
						sanitized.Append(c);
					break;
			}
		}

		var result = sanitized.ToString();

		// Truncate if too long and add ellipsis
		if (result.Length > MaxLogLength)
		{
			return result[..MaxLogLength] + "... [truncated]";
		}

		return result;
	}

	/// <summary>
	/// Sanitizes multiple values for logging.
	/// </summary>
	/// <param name="inputs">The values to sanitize.</param>
	/// <returns>An array of sanitized strings.</returns>
	public static string[] Sanitize(params string?[] inputs)
		=> inputs.Select(Sanitize).ToArray();
}
