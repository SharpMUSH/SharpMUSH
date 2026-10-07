namespace SharpMUSH.Library.API;

/// <summary>
/// A portal theme: a named set of the editable design tokens (<see cref="Models.Portal.ThemeTokens.Editable"/>),
/// each a <c>#rrggbb</c> colour. Everything else the portal paints with (the accent's dim and on-accent shades,
/// the glow, the OOC band, MudBlazor's palette) is derived from these by <see cref="Models.Portal.ThemeResolver"/>.
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

/// <summary>The themes a viewer may use, and the one a character that chose none gets.</summary>
public sealed record PortalThemesResponse(IReadOnlyList<PortalTheme> Themes, string DefaultThemeId);

/// <summary>Creates or replaces a theme. The id comes from the route on a replace and from the name on a create.</summary>
public sealed record PortalThemeRequest(string Name, bool Dark, bool Published, Dictionary<string, string> Tokens);

/// <summary>Makes <paramref name="ThemeId"/> the theme of every character that chose none.</summary>
public sealed record DefaultThemeRequest(string ThemeId);

/// <summary>
/// A character's own look in the portal, stored with the character so it follows the player to every device.
/// </summary>
/// <param name="ThemeId">A published theme, or null for the game's default.</param>
/// <param name="Accent">A <c>#rrggbb</c> accent laid over the theme's, or null for the theme's own.</param>
public sealed record CharacterAppearance(string? ThemeId, string? Accent);
