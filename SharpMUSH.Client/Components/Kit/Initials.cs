namespace SharpMUSH.Client.Components.Kit;

/// <summary>Up to two initials for a no-image tile: the first letters of the first two words, upper-cased.</summary>
public static class Initials
{
	public static string From(string? name)
	{
		var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (words.Length == 0) return "?";
		var s = char.ToUpperInvariant(words[0][0]).ToString();
		if (words.Length > 1) s += char.ToUpperInvariant(words[1][0]);
		return s;
	}
}
