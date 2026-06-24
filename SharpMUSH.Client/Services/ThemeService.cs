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
		MakeAccentPreset("Amber",  "#ffb454"),
		MakeAccentPreset("Violet", "#b39cff"),
		MakeAccentPreset("Rose",   "#ff7a9c"),
		MakeAccentPreset("Signal", "#5aa9ff"),
	];

	private readonly IJSRuntime _js;
	private ThemePreset _current = GetDefaultPreset();

	public event Action? OnThemeChanged;

	public ThemeService(IJSRuntime js) => _js = js;

	/// <summary>C# port of <c>deriveAccent(hex)</c> from the prototype's app.jsx.</summary>
	public static (string Primary, string Secondary, string OnAccent) DeriveAccent(string hex)
	{
		var (r, g, b) = ParseHex(hex);
		var lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
		var secondary = $"rgb({(int)(r * 0.78)},{(int)(g * 0.78)},{(int)(b * 0.78)})";
		var onAccent = lum > 0.55
			? $"rgb({(int)(r * 0.12)},{(int)(g * 0.12)},{(int)(b * 0.12)})"
			: "#ffffff";
		return (hex, secondary, onAccent);
	}

	private static (int r, int g, int b) ParseHex(string hex)
	{
		var h = hex.TrimStart('#');
		if (h.Length == 3)
			h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
		if (!int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var n))
			return (0, 245, 183);
		return ((n >> 16) & 0xFF, (n >> 8) & 0xFF, n & 0xFF);
	}

	/// <inheritdoc/>
	public static ThemePreset GetDefaultPreset() => MakeAccentPreset("Phosphor", "#00f5b7");

	private static ThemePreset MakeAccentPreset(string name, string accentHex)
	{
		var (primary, secondary, _) = DeriveAccent(accentHex);
		return new ThemePreset(
			Name: name,
			PrimaryColor: primary,
			SecondaryColor: secondary,
			TertiaryColor: secondary,
			BackgroundColor: "#0e0f11",
			SurfaceColor: "#16181b",
			AppBarColor: "#101113",
			DrawerBackgroundColor: "#101113",
			IsDarkMode: true);
	}

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
	private static readonly string[] UiFontFamily = ["Hanken Grotesk", "sans-serif"];
	private static readonly string[] MonoFontFamily = ["JetBrains Mono", "monospace"];

	public static MudTheme ToMudTheme(this ThemePreset preset)
	{
		var (_, _, onAccent) = ThemeService.DeriveAccent(preset.PrimaryColor);

		var palette = new PaletteDark
		{
			Primary          = preset.PrimaryColor,
			Secondary        = preset.SecondaryColor,
			Tertiary         = preset.TertiaryColor,
			Background       = preset.BackgroundColor,
			Surface          = preset.SurfaceColor,
			AppbarBackground = preset.AppBarColor,
			DrawerBackground = preset.DrawerBackgroundColor,
			TextPrimary      = "#e9edf0",
			TextSecondary    = "#9aa3ab",
			TextDisabled     = "#5f6870",
			AppbarText       = preset.PrimaryColor,
			DrawerText       = "#e9edf0",
			DrawerIcon       = preset.PrimaryColor,
			Warning          = "#d9a23a",
			Error            = "#e57373",
			Info             = "#5aa9ff",
			Success          = preset.PrimaryColor,
			LinesDefault     = "#262a2f",
			LinesInputs      = "#262a2f",
			TableLines       = "#262a2f",
			Divider          = "#1d2024",
			PrimaryContrastText  = onAccent,
			SecondaryContrastText = onAccent,
		};

		var typography = new Typography
		{
			Default = new DefaultTypography
			{
				FontFamily = UiFontFamily,
				FontSize   = "14px",
				FontWeight = "400",
				LineHeight = "1.5",
			},
			H1 = new H1Typography { FontFamily = UiFontFamily, FontWeight = "600" },
			H2 = new H2Typography { FontFamily = UiFontFamily, FontWeight = "600" },
			H3 = new H3Typography { FontFamily = UiFontFamily, FontWeight = "600" },
			H4 = new H4Typography { FontFamily = UiFontFamily, FontWeight = "600" },
			H5 = new H5Typography { FontFamily = UiFontFamily, FontWeight = "600" },
			H6 = new H6Typography { FontFamily = UiFontFamily, FontWeight = "600" },
			Button = new ButtonTypography { FontFamily = UiFontFamily, FontWeight = "500" },
			Caption = new CaptionTypography { FontFamily = MonoFontFamily, FontSize = "11px" },
			Overline = new OverlineTypography { FontFamily = MonoFontFamily },
		};

		var layout = new LayoutProperties
		{
			DefaultBorderRadius = "9px",
			DrawerWidthLeft     = "250px",
			DrawerMiniWidthLeft = "60px",
		};

		return new MudTheme
		{
			PaletteDark    = palette,
			Typography     = typography,
			LayoutProperties = layout,
		};
	}
}
