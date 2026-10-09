using System.Globalization;

namespace SharpMUSH.Configuration;

/// <summary>
/// What an entry of the <c>ascii_translations</c> table may hold: one character outside ASCII, and the
/// printable ASCII text a client without Unicode is sent for it, which may be empty to leave it out.
/// </summary>
public static class AsciiTranslations
{
	/// <summary>Whether <paramref name="character"/> is one character (a grapheme cluster) with something outside ASCII.</summary>
	public static bool IsCharacter(string? character)
		=> !string.IsNullOrEmpty(character)
			&& new StringInfo(character).LengthInTextElements == 1
			&& !character.All(char.IsAscii);

	/// <summary>Whether <paramref name="text"/> is printable ASCII, which an empty text is.</summary>
	public static bool IsText(string? text) => text is not null && text.All(c => c is >= ' ' and <= '~');

	/// <summary>Why <paramref name="character"/> cannot stand for <paramref name="text"/>, or null when it can.</summary>
	public static string? Problem(string? character, string? text)
		=> !IsCharacter(character) ? $"'{character}' is not one character outside ASCII."
			: !IsText(text) ? $"The text for '{character}' is not plain ASCII."
			: null;

	/// <summary>The table as one string, the same for the same entries in any order: what tells two tables apart.</summary>
	public static string Fingerprint(IReadOnlyDictionary<string, string>? translations)
		=> translations is null or { Count: 0 }
			? string.Empty
			: string.Join('\n', translations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
				.Select(pair => pair.Key + '\t' + pair.Value));
}
