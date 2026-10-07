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
public sealed record PortalTheme(
	string Id,
	string Name,
	bool Dark,
	bool Published,
	IReadOnlyDictionary<string, string> Tokens,
	bool BuiltIn = false);

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
public sealed record PortalThemeRequest(string Name, bool Dark, bool Published, Dictionary<string, string> Tokens);

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
public sealed record CharacterAppearance(string? ThemeId, string? Accent);
