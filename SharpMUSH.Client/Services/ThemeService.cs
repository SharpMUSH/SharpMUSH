using System.Text.Json;
using Microsoft.JSInterop;
using MudBlazor;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Resolves the portal's theme: the acting character's chosen theme (or the game's default) with the
/// character's accent laid over it. The last theme applied is kept in localStorage, where <c>index.html</c>
/// reads it before the runtime loads, so a reload paints the right colours from the first frame.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
	/// <summary>The last applied theme, as JSON. <c>index.html</c> reads its <c>Css</c> member.</summary>
	public const string CacheKey = "sharp-theme";

	private readonly IJSRuntime _js;
	private readonly IHttpClientFactory _http;
	private readonly IAccountAuthState _account;
	private readonly ILogger<ThemeService> _logger;

	private IReadOnlyList<PortalTheme> _themes = BuiltInThemes.All;
	private string _defaultThemeId = BuiltInThemes.PhosphorId;
	private bool _loaded;
	private ResolvedTheme _resolved = ThemeResolver.Resolve(BuiltInThemes.Phosphor);
	private ResolvedTheme? _preview;
	private Task? _initialized;

	public event Action? OnThemeChanged;

	public ThemeService(IJSRuntime js, IHttpClientFactory http, IAccountAuthState account, ILogger<ThemeService> logger)
	{
		_js = js;
		_http = http;
		_account = account;
		_logger = logger;
		_account.ActiveCharacterChanged += Apply;
		_account.AppearanceChanged += Apply;
	}

	public ResolvedTheme Current => _preview ?? _resolved;

	public IReadOnlyList<PortalTheme> Themes => _themes;

	public string DefaultThemeId => _defaultThemeId;

	/// <summary>
	/// Restores the last applied theme from localStorage, then reads the game's themes in the background.
	/// Program.cs awaits this before the first render; it touches no network, so that wait is a storage read.
	/// </summary>
	public Task InitializeAsync() => _initialized ??= RestoreAsync();

	private async Task RestoreAsync()
	{
		try
		{
			if (await _js.GetItemAsync(BrowserStore.Local, CacheKey) is { Length: > 0 } json
					&& JsonSerializer.Deserialize<ResolvedTheme>(json) is { Tokens: not null } cached)
			{
				_resolved = cached;
			}
		}
		catch (Exception ex) when (ex is JsonException or JSException or InvalidOperationException)
		{
			_logger.LogDebug(ex, "No usable cached theme; starting from Phosphor");
		}

		_ = ReloadAsync();
	}

	public async Task ReloadAsync()
	{
		var result = await _http.CreateClient("api").GetApiAsync<PortalThemesResponse>("api/themes", "The server listed no themes.");
		if (result is PortalThemesResponse themes)
		{
			_themes = themes.Themes;
			_defaultThemeId = themes.DefaultThemeId;
			_loaded = true;
			Apply();
		}
		else if (result is ApiFailure failure)
		{
			_logger.LogWarning("Could not read the portal's themes: {Message}", failure.Message);
		}
	}

	public void Preview(ResolvedTheme? theme)
	{
		_preview = theme;
		OnThemeChanged?.Invoke();
	}

	/// <summary>
	/// Resolves the theme for the acting character. Waits for the game's themes: until they arrive the cached
	/// theme stands, rather than flashing Phosphor at a character whose theme is one the game made. A signed-in
	/// tab waits for its acting character the same way, since a reload restores the session before the roster.
	/// Only published themes apply, so staff, who are also sent drafts, see what the settings page reports.
	/// </summary>
	private void Apply()
	{
		if (!_loaded) return;

		var character = _account.ActiveCharacter;
		if (character is null && _account.IsLoggedIn) return;

		var published = _themes.Where(t => t.Published).ToList();
		var resolved = ThemeResolver.Resolve(
			ThemeResolver.Pick(published, _defaultThemeId, character?.ThemeId), character?.Accent);
		if (resolved.Css == _resolved.Css && resolved.ThemeId == _resolved.ThemeId) return;

		_resolved = resolved;
		_ = SaveAsync(resolved);
		OnThemeChanged?.Invoke();
	}

	private async Task SaveAsync(ResolvedTheme theme)
	{
		try
		{
			await _js.SetItemAsync(BrowserStore.Local, CacheKey, JsonSerializer.Serialize(theme));
		}
		catch (JSException ex)
		{
			_logger.LogDebug(ex, "Could not keep the theme for the next visit");
		}
	}

	public void Dispose()
	{
		_account.ActiveCharacterChanged -= Apply;
		_account.AppearanceChanged -= Apply;
	}
}

/// <summary>Converts a <see cref="ResolvedTheme"/> to a MudBlazor <see cref="MudTheme"/>.</summary>
public static class ResolvedThemeExtensions
{
	private static readonly string[] UiFontFamily = ["Hanken Grotesk", "sans-serif"];
	private static readonly string[] MonoFontFamily = ["var(--font-mono)"];

	public static MudTheme ToMudTheme(this ResolvedTheme theme)
	{
		var t = theme.Tokens;
		Palette Fill(Palette palette)
		{
			palette.Primary = theme.Accent;
			palette.Secondary = t["accent-dim"];
			palette.Tertiary = t["accent-dim"];
			palette.Background = t[ThemeTokens.Background];
			palette.Surface = t[ThemeTokens.Surface];
			palette.AppbarBackground = t[ThemeTokens.Surface3];
			palette.DrawerBackground = t[ThemeTokens.Surface3];
			palette.TextPrimary = t[ThemeTokens.Text];
			palette.TextSecondary = t[ThemeTokens.TextDim];
			palette.TextDisabled = t[ThemeTokens.TextFaint];
			palette.AppbarText = theme.Accent;
			palette.DrawerText = t[ThemeTokens.Text];
			palette.DrawerIcon = theme.Accent;
			palette.ActionDefault = t[ThemeTokens.TextDim];
			palette.Warning = t[ThemeTokens.Warn];
			palette.Error = t[ThemeTokens.LinkMissing];
			palette.Info = theme.Dark ? "#5aa9ff" : "#1f63b8";
			palette.Success = theme.Accent;
			palette.LinesDefault = t[ThemeTokens.Border];
			palette.LinesInputs = t[ThemeTokens.Border];
			palette.TableLines = t[ThemeTokens.Border];
			palette.Divider = t[ThemeTokens.BorderSoft];
			palette.PrimaryContrastText = t["accent-on"];
			palette.SecondaryContrastText = t["accent-on"];
			return palette;
		}

		var typography = new Typography
		{
			Default = new DefaultTypography
			{
				FontFamily = UiFontFamily,
				FontSize = "14px",
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
			DrawerWidthLeft = "250px",
			DrawerMiniWidthLeft = "60px",
		};

		return new MudTheme
		{
			PaletteDark = (PaletteDark)Fill(new PaletteDark()),
			PaletteLight = (PaletteLight)Fill(new PaletteLight()),
			Typography = typography,
			LayoutProperties = layout,
		};
	}
}
