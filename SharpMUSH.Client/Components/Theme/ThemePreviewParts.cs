using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Components.Theme;

/// <summary>The data-* attributes a theme picture carries, so the rules in css/themes/ draw its parts.</summary>
public static class ThemePreviewParts
{
	/// <summary>
	/// The theme's parts as data-* attributes. Standing still, its motion is "still" and Reduce motion is on, the two
	/// switches every moving part of a theme answers to (css/themes/motion.css, textures.css).
	/// </summary>
	public static Dictionary<string, object> For(ResolvedTheme theme, bool still)
	{
		var parts = theme.Parts.ToDictionary(p => "data-" + p.Key, p => (object)p.Value);
		if (!still) return parts;
		parts["data-" + ThemeStyles.Motion] = "still";
		parts["data-" + ThemeService.ReduceMotionPart] = "on";
		return parts;
	}
}
