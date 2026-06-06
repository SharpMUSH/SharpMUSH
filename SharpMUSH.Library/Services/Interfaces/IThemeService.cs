using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Manages MudBlazor theme presets and the currently-active theme for the portal UI.
/// </summary>
public interface IThemeService
{
	/// <summary>Raised when the active theme changes so components can re-render.</summary>
	event Action? OnThemeChanged;

	/// <summary>Returns the currently-active preset.</summary>
	Task<ThemePreset> GetCurrentThemeAsync();

	/// <summary>Returns all built-in presets.</summary>
	Task<IReadOnlyList<ThemePreset>> GetAvailablePresetsAsync();

	/// <summary>Switches the active preset by name.  Throws if the name is unknown.</summary>
	Task ApplyPresetAsync(string presetName);
}
