namespace SharpMUSH.Server.Logging;

/// <summary>
/// Makes a caller-controlled string safe to put in a log entry (CWE-117, log forging).
/// <para>
/// ASP.NET Core percent-decodes route values, so a request to <c>/http/foo%0AWARN:+forged</c>
/// hands the application a path with a real newline in it — verified end to end, not assumed. Logged
/// verbatim that would put an attacker's text on its own line, where it reads as a separate,
/// server-issued entry. Structured logging does not help: the message template's placeholders are
/// still rendered into whatever sink the operator configured.
/// </para>
/// </summary>
public static class SafeLogValue
{
	/// <summary>What a control character is replaced with: visible, and never a line break.</summary>
	private const char Replacement = '�';

	/// <summary>
	/// <paramref name="value"/> with every control character replaced, so the result occupies
	/// exactly one line however it is rendered. Length and every other character are preserved, so
	/// the entry still says what was actually requested.
	/// </summary>
	public static string OneLine(string? value)
		=> string.IsNullOrEmpty(value)
			? string.Empty
			: string.Create(value.Length, value, static (destination, original) =>
			{
				for (var index = 0; index < original.Length; index++)
				{
					var character = original[index];
					destination[index] = char.IsControl(character) ? Replacement : character;
				}
			});
}
