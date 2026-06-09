namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a named colour preset for the SharpMUSH portal UI theme.
/// All colour values are CSS hex strings (e.g. "#4caf50").
/// </summary>
public record ThemePreset(
	string Name,
	string PrimaryColor,
	string SecondaryColor,
	string TertiaryColor,
	string BackgroundColor,
	string SurfaceColor,
	string AppBarColor,
	string DrawerBackgroundColor,
	bool IsDarkMode);
