namespace SharpMUSH.Library.API;

/// <summary>
/// A portal theme: a named set of the editable design tokens (<see cref="Models.Portal.ThemeTokens.Editable"/>),
/// each a <c>#rrggbb</c> colour, and its style (<see cref="Models.Portal.ThemeStyles"/>: typefaces, corners,
/// texture, ornament, card frame, title lettering), each one of a fixed set of choices. Everything else the portal
/// paints with is derived from these by <see cref="Models.Portal.ThemeResolver"/>.
/// </summary>
/// <param name="Id">Stable key, chosen from the name at creation and never changed after.</param>
/// <param name="Dark">Whether the theme is dark: picks MudBlazor's dark or light palette and the derived shades.</param>
/// <param name="Published">Whether players may choose it. An unpublished theme is visible to theme editors only.</param>
/// <param name="BuiltIn">One that SharpMUSH ships. It cannot be edited or deleted; duplicate it instead.</param>
/// <param name="Stylesheet">
/// Staff-written CSS laid over the tokens (<see cref="Models.Portal.ThemeStylesheet"/>): checked on save, so it loads
/// nothing from another site and cannot leave its <c>&lt;style&gt;</c>. Colour tokens it sets in <c>:root</c> count as
/// the theme's own, so contrast is checked against them.
/// </param>
public sealed record PortalTheme(
	string Id,
	string Name,
	bool Dark,
	bool Published,
	IReadOnlyDictionary<string, string> Tokens,
	bool BuiltIn = false,
	string? Stylesheet = null);

/// <summary>
/// The themes a viewer may use, and the game's defaults: what a visitor, or a character that chose none, sees when
/// the browser prefers dark (<paramref name="DefaultThemeId"/>) or light (<paramref name="DefaultLightThemeId"/>).
/// </summary>
public sealed record PortalThemesResponse(IReadOnlyList<PortalTheme> Themes, string DefaultThemeId, string DefaultLightThemeId)
{
	public PortalThemeDefaults Defaults => new(DefaultThemeId, DefaultLightThemeId);
}

/// <summary>The game's default theme for a browser that prefers dark, and for one that prefers light.</summary>
public sealed record PortalThemeDefaults(string DarkThemeId, string LightThemeId)
{
	public string For(bool prefersLight) => prefersLight ? LightThemeId : DarkThemeId;
}

/// <summary>Creates or replaces a theme. The id comes from the route on a replace and from the name on a create.</summary>
public sealed record PortalThemeRequest(string Name, bool Dark, bool Published, Dictionary<string, string> Tokens, string? Stylesheet = null);

/// <summary>
/// Makes <paramref name="ThemeId"/> the default for its own mode: a dark theme for browsers that prefer dark, a light
/// one for browsers that prefer light.
/// </summary>
public sealed record DefaultThemeRequest(string ThemeId);

/// <summary>
/// A character's own look in the portal, stored with the character so it follows the player to every device.
/// </summary>
/// <param name="ThemeId">A published theme, or null for the game's default, which follows the browser's light or dark preference.</param>
/// <param name="Accent">A <c>#rrggbb</c> accent laid over the theme's, or null for the theme's own.</param>
/// <param name="Vision">
/// The player's colour vision (<see cref="SharpMUSH.Library.Models.Portal.ThemeVision"/>), or null for typical. With no
/// <paramref name="ThemeId"/> it picks that vision's theme for the browser's light or dark preference; with any theme
/// it swaps the status and code colours for ones that stay apart for that vision.
/// </param>
public sealed record CharacterAppearance(string? ThemeId, string? Accent, string? Vision = null);
