using Microsoft.JSInterop;
using MudBlazor;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side theme service that persists the active preset to localStorage
/// and raises <see cref="OnThemeChanged"/> so components can re-render.
/// </summary>
public sealed class ThemeService : IThemeService
{
	private const string LocalStorageKey = "sharp-theme-preset";

	private static readonly IReadOnlyList<ThemePreset> BuiltInPresets =
	[
		GetDefaultPreset(),
		new ThemePreset(
			Name: "Midnight Blue",
			PrimaryColor: "#448aff",
			SecondaryColor: "#82b1ff",
			TertiaryColor: "#b0bec5",
			BackgroundColor: "#0d1b2a",
			SurfaceColor: "#1b2a3b",
			AppBarColor: "#0d1b2a",
			DrawerBackgroundColor: "#122030",
			IsDarkMode: true),
		new ThemePreset(
			Name: "Forest",
			PrimaryColor: "#66bb6a",
			SecondaryColor: "#a5d6a7",
			TertiaryColor: "#c8e6c9",
			BackgroundColor: "#1b2619",
			SurfaceColor: "#253323",
			AppBarColor: "#1b2619",
			DrawerBackgroundColor: "#1e2e1c",
			IsDarkMode: true)
	];

	private readonly IJSRuntime _js;
	private ThemePreset _current = GetDefaultPreset();

	public event Action? OnThemeChanged;

	public ThemeService(IJSRuntime js) => _js = js;

	/// <inheritdoc/>
	public static ThemePreset GetDefaultPreset() => new(
		Name: "Default Dark",
		PrimaryColor: "#4caf50",
		SecondaryColor: "#81c784",
		TertiaryColor: "#a5d6a7",
		BackgroundColor: "#1a1a1a",
		SurfaceColor: "#242424",
		AppBarColor: "#1a1a1a",
		DrawerBackgroundColor: "#1e1e1e",
		IsDarkMode: true);

	/// <inheritdoc/>
	public Task<ThemePreset> GetCurrentThemeAsync() => Task.FromResult(_current);

	/// <inheritdoc/>
	public Task<IReadOnlyList<ThemePreset>> GetAvailablePresetsAsync()
		=> Task.FromResult(BuiltInPresets);

	/// <inheritdoc/>
	public async Task ApplyPresetAsync(string presetName)
	{
		var preset = BuiltInPresets.FirstOrDefault(p => p.Name == presetName)
			?? throw new ArgumentException($"Unknown theme preset: '{presetName}'", nameof(presetName));

		_current = preset;

		try
		{
			await _js.InvokeVoidAsync("localStorage.setItem", LocalStorageKey, presetName);
		}
		catch (JSException)
		{
			// localStorage unavailable in some test environments — ignore
		}

		OnThemeChanged?.Invoke();
	}

	/// <summary>
	/// Restores the persisted preset from localStorage on first mount.
	/// Falls back to the default silently on any failure.
	/// </summary>
	public async Task InitializeAsync()
	{
		try
		{
			var saved = await _js.InvokeAsync<string?>("localStorage.getItem", LocalStorageKey);
			if (saved is not null)
			{
				var match = BuiltInPresets.FirstOrDefault(p => p.Name == saved);
				if (match is not null)
					_current = match;
			}
		}
		catch (JSException)
		{
			// ignore — use default
		}
	}
}

/// <summary>
/// Extension methods that convert a <see cref="ThemePreset"/> to a MudBlazor <see cref="MudTheme"/>.
/// </summary>
public static class ThemePresetExtensions
{
	public static MudTheme ToMudTheme(this ThemePreset preset)
	{
		var palette = new PaletteDark
		{
			Primary = preset.PrimaryColor,
			Secondary = preset.SecondaryColor,
			Tertiary = preset.TertiaryColor,
			Background = preset.BackgroundColor,
			Surface = preset.SurfaceColor,
			AppbarBackground = preset.AppBarColor,
			DrawerBackground = preset.DrawerBackgroundColor
		};

		return new MudTheme { PaletteDark = palette };
	}
}
