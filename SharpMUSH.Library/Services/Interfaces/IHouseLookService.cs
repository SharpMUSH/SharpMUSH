using MarkupString;
using MarkupString.Layout;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The game's look: <c>layout_border</c>'s border and <c>layout_theme</c>'s colours. Softcode layouts and the
/// server's own listings (<see cref="Markup.ServerLayout.Build"/>) are both drawn in it, under any look a
/// reader's <c>@theme</c> brings.
/// </summary>
public interface IHouseLookService
{
	/// <summary>The look: <c>layout_theme</c>'s colours, and its border or else <see cref="Border"/>.</summary>
	LayoutTheme Look { get; }

	/// <summary><c>layout_theme</c>'s palette, or null when it is unset or names no theme.</summary>
	ThemePalette? Palette { get; }

	/// <summary><c>layout_border</c>'s border, or double lines when it is unset or names no border.</summary>
	BorderStyle Border { get; }
}
