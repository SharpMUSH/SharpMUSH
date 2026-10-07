using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The portal's look: the themes the game offers, and the one in use, resolved for the acting character's
/// own theme and accent.
/// </summary>
public interface IThemeService
{
	/// <summary>Raised when <see cref="Current"/> changes, so the theme can be applied again.</summary>
	event Action? OnThemeChanged;

	/// <summary>The theme the portal is painted with: a preview while one is shown, else the acting character's.</summary>
	ResolvedTheme Current { get; }

	/// <summary>The themes this viewer may use (staff with the editing scope also get the unpublished ones).</summary>
	IReadOnlyList<PortalTheme> Themes { get; }

	/// <summary>The theme a character that chose none sees.</summary>
	string DefaultThemeId { get; }

	/// <summary>Paints the portal with <paramref name="theme"/> until called again with null, without storing it.</summary>
	void Preview(ResolvedTheme? theme);

	/// <summary>Reads the themes from the server again, after they were edited.</summary>
	Task ReloadAsync();
}
