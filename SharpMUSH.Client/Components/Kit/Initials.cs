using System.Globalization;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Up to two initials for a no-image tile: the first text element of each of the first two words,
/// upper-cased. Text elements rather than chars, so an emoji or a decomposed accented letter stays
/// whole instead of becoming a lone surrogate or a bare base letter. Words split on any Unicode
/// whitespace, so a non-breaking space between names still yields two initials.
/// </summary>
public static class Initials
{
	public static string From(string? name)
	{
		var words = (name ?? string.Empty)
			.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(w => w.Length > 0)
			.ToArray();
		if (words.Length == 0) return "?";

		var s = First(words[0]);
		if (words.Length > 1) s += First(words[1]);
		return s;
	}

	private static string First(string word) =>
		StringInfo.GetNextTextElement(word, 0).ToUpperInvariant();
}
