using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The portal's themes (the built-in ones and those staff made, stored as server data) and each character's own
/// choice of theme and accent (stored on the character).
/// </summary>
public interface IPortalThemeService
{
	/// <summary>Every theme, built-in ones first. Unpublished ones only when <paramref name="includeUnpublished"/>.</summary>
	ValueTask<PortalThemesResponse> GetThemesAsync(bool includeUnpublished);

	/// <summary>Adds a theme, with an id made from its name.</summary>
	ValueTask<Result<PortalTheme>> CreateAsync(PortalThemeRequest request);

	/// <summary>Replaces a staff-made theme. A built-in one is refused.</summary>
	ValueTask<FoundResult<PortalTheme>> UpdateAsync(string id, PortalThemeRequest request);

	/// <summary>Removes a staff-made theme. The default and the built-in ones are refused.</summary>
	ValueTask<FoundResult<PortalThemesResponse>> DeleteAsync(string id);

	/// <summary>Makes a published theme the one a character that chose none sees.</summary>
	ValueTask<FoundResult<PortalThemesResponse>> SetDefaultAsync(string id);

	/// <summary>The character's own theme and accent; both null when it chose neither.</summary>
	ValueTask<CharacterAppearance> GetAppearanceAsync(SharpObject character);

	/// <summary>Stores the character's theme and accent. A theme players may not choose, or a malformed accent, is refused.</summary>
	ValueTask<Result<CharacterAppearance>> SetAppearanceAsync(SharpObject character, CharacterAppearance appearance);
}
