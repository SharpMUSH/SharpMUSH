using System.Net.Http.Json;
using MudBlazor;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Service for managing themes in the Blazor portal.
/// Loads available presets from server, caches player preference in localStorage,
/// and provides the current MudTheme to MainLayout.
/// </summary>
public class ThemeService
{
	private const string LocalStorageKey = "sharpmush.theme.selected";
	private readonly HttpClient _httpClient;
	private readonly Blazored.LocalStorage.ILocalStorageService _localStorageService;
	private List<ThemePreset>? _availableThemes;
	private string? _selectedThemeName;
	private MudTheme? _currentTheme;

	public event Action? ThemeChanged;

	public ThemeService(HttpClient httpClient, Blazored.LocalStorage.ILocalStorageService localStorageService)
	{
		_httpClient = httpClient;
		_localStorageService = localStorageService;
	}

	/// <summary>
	/// Loads the list of available themes from the server.
	/// </summary>
	public async Task InitializeAsync()
	{
		try
		{
			_availableThemes = await _httpClient.GetFromJsonAsync<List<ThemePreset>>("api/portal/themes");
			_availableThemes ??= new List<ThemePreset>();
		}
		catch
		{
			_availableThemes = new List<ThemePreset>();
		}

		// Load player preference from localStorage
		try
		{
			_selectedThemeName = await _localStorageService.GetItemAsStringAsync(LocalStorageKey);
		}
		catch
		{
			_selectedThemeName = null;
		}

		// Apply theme
		ApplyTheme();
	}

	/// <summary>
	/// Gets the list of available themes.
	/// </summary>
	public IReadOnlyList<ThemePreset> AvailableThemes => _availableThemes ?? new List<ThemePreset>();

	/// <summary>
	/// Gets the currently selected theme name.
	/// </summary>
	public string? SelectedThemeName => _selectedThemeName;

	/// <summary>
	/// Gets the current MudTheme instance.
	/// </summary>
	public MudTheme CurrentTheme => _currentTheme ??= GetDefaultTheme();

	/// <summary>
	/// Selects a theme by name and saves to localStorage.
	/// </summary>
	public async Task SelectThemeAsync(string themeName)
	{
		_selectedThemeName = themeName;
		await _localStorageService.SetItemAsStringAsync(LocalStorageKey, themeName);
		ApplyTheme();
		ThemeChanged?.Invoke();
	}

	/// <summary>
	/// Applies the currently selected theme or falls back to default.
	/// </summary>
	private void ApplyTheme()
	{
		if (string.IsNullOrEmpty(_selectedThemeName))
		{
			// No preference: use the first preset marked as default, or the first one
			var defaultTheme = _availableThemes?.FirstOrDefault(t => t.IsDefault)
				?? _availableThemes?.FirstOrDefault();
			_currentTheme = defaultTheme?.ToMudTheme() ?? GetDefaultTheme();
			_selectedThemeName = defaultTheme?.Name;
		}
		else
		{
			// Find the preset by name
			var preset = _availableThemes?.FirstOrDefault(t => t.Name == _selectedThemeName);
			_currentTheme = preset?.ToMudTheme() ?? GetDefaultTheme();
		}
	}

	/// <summary>
	/// Returns the default hardcoded theme (current SharpMUSH theme).
	/// </summary>
	private static MudTheme GetDefaultTheme()
	{
		return new MudTheme
		{
			PaletteDark = new PaletteDark
			{
				Primary = MudColor.Parse("#00f5b7"),
				Secondary = MudColor.Parse("#00f5b7"),
				AppbarText = MudColor.Parse("#00f5b7"),
				TextPrimary = "rgba(255, 255, 255, 0.87)",
				TextSecondary = "rgba(255, 255, 255, 0.60)",
				Surface = MudColor.Parse("#242424"),
				Background = MudColor.Parse("#1a1a1a")
			}
		};
	}
}
