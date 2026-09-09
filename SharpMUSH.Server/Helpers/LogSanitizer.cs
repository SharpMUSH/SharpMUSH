using System.Text.RegularExpressions;

namespace SharpMUSH.Server.Helpers;

/// <summary>
/// Provides utilities for sanitizing user input before logging to prevent log injection attacks.
/// </summary>
public static partial class LogSanitizer
{
	/// <summary>
	/// Maximum length for sanitized log values before truncation.
	/// </summary>
	private const int MaxLogLength = 200;

	/// <summary>The C0 and C1 controls — what <see cref="char.IsControl(char)"/> answers to.</summary>
	[GeneratedRegex(@"\p{Cc}")]
	private static partial Regex ControlCharacter();

	/// <summary>A line break becomes a visible <c>\n</c>, a tab a space, and any other control vanishes.</summary>
	private static string Replacement(Match control) => control.ValueSpan[0] switch
	{
		'\n' or '\r' => @"\n",
		'\t' => " ",
		_ => string.Empty,
	};

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

		// Most values are short and carry nothing to strip; those go back as they came.
		if (input.Length <= MaxLogLength && !ControlCharacter().IsMatch(input))
			return input;

		var result = ControlCharacter().Replace(input, Replacement);

		return result.Length > MaxLogLength
			? result[..MaxLogLength] + "... [truncated]"
			: result;
	}

	/// <summary>
	/// Sanitizes multiple values for logging.
	/// </summary>
	/// <param name="inputs">The values to sanitize.</param>
	/// <returns>An array of sanitized strings.</returns>
	public static string[] Sanitize(params string?[] inputs)
		=> inputs.Select(Sanitize).ToArray();
}
