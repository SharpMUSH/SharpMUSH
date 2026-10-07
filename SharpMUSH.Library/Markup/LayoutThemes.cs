using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// Themes as softcode names them: a preset by name (<c>nord</c>), or a palette written out as JSON
/// (<c>{"seed":"#7aa2f7","harmony":"triadic"}</c>), read by <see cref="ThemePalette.TryParse(string, out ThemePalette?, out string?)"/>.
/// </summary>
public static class LayoutThemes
{
	/// <summary>What an unknown theme name answers.</summary>
	public const string Unknown = "#-1 UNKNOWN THEME";

	/// <summary>The palette <paramref name="spec"/> names or writes out, or the error saying why not.</summary>
	public static Result<ThemePalette> Read(string spec)
	{
		spec = spec.Trim();
		if (spec.Length == 0) return new Error<string>(Unknown);
		if (ThemePalette.TryParse(spec, out var palette, out var error)) return palette!;
		var unknown = spec[0] is not ('{' or '[') && (error ?? string.Empty).Contains("theme name", StringComparison.Ordinal)
			|| (error ?? string.Empty).StartsWith("no theme named", StringComparison.Ordinal);
		return new Error<string>(unknown ? Unknown : $"#-1 INVALID THEME: {error}");
	}

	/// <summary>The preset names, in the order they are listed.</summary>
	public static IEnumerable<string> Names => ThemePalette.Presets.Select(preset => preset.Name);
}
