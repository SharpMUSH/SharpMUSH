using SharpMUSH.Library.Models.Portal;
using ThemeColor = SharpMUSH.Library.Models.Portal.ThemeColor;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// A character's name colour from softcode, accepted only as <c>#rrggbb</c> so it can be written into a
/// style attribute. Anything else is dropped and the default colour applies.
/// </summary>
public static class NameColor
{
	internal static readonly ResolvedTheme DefaultTheme = ThemeResolver.Resolve(BuiltInThemes.Phosphor);

	/// <summary><paramref name="color"/> when it is <c>#rrggbb</c>, else null.</summary>
	public static string? Safe(string? color) =>
		color is { Length: 7 } c && c[0] == '#' && c.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") is false ? c : null;

	/// <summary>Preserves a safe name colour, moving its lightness only when it cannot be read on the supplied surface.</summary>
	public static string? Readable(string? color, params string[] backgrounds)
	{
		if (Safe(color) is not { } safe || backgrounds.Length == 0 || backgrounds.Any(b => !ThemeColor.TryParse(b, out _))) return null;
		var grounds = backgrounds.Select(ThemeColor.Parse).ToArray();
		var towardWhite = grounds.Min(b => ThemeColor.Contrast(ThemeColor.White, b)) > grounds.Min(b => ThemeColor.Contrast(ThemeColor.Black, b));
		return ThemeResolver.ReadableAgainst(ThemeColor.Parse(safe), towardWhite, ThemeTokens.TextContrast, grounds).Hex;
	}
}
