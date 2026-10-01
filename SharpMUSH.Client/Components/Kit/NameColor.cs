namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// A character's name colour from softcode, accepted only as <c>#rrggbb</c> so it can be written into a
/// style attribute. Anything else is dropped and the default colour applies.
/// </summary>
public static class NameColor
{
	/// <summary><paramref name="color"/> when it is <c>#rrggbb</c>, else null.</summary>
	public static string? Safe(string? color) =>
		color is { Length: 7 } c && c[0] == '#' && c.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") is false ? c : null;
}
