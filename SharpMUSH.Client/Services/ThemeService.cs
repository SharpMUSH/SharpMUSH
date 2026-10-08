using System.Text.Json;
using Microsoft.JSInterop;
using MudBlazor;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Resolves the portal's theme: the acting character's chosen theme, or the game's default for the browser's light
/// or dark preference, with the character's accent laid over it. The last theme applied is kept in localStorage,
/// where <c>index.html</c> reads it before the runtime loads, so a reload paints the right look from the first frame.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
	/// <summary>
	/// The last applied theme, as JSON. <c>index.html</c> reads its <c>Css</c> member. While the theme follows the
	/// browser's preference this is the dark one and <see cref="LightCacheKey"/> holds the light one.
	/// </summary>
	public const string CacheKey = "sharp-theme";

	/// <summary>The light theme, kept only while the theme follows the browser's preference.</summary>
	public const string LightCacheKey = "sharp-theme-light";

	private readonly IJSRuntime _js;
	private readonly IHttpClientFactory _http;
	private readonly IAccountAuthState _account;
	private readonly ILogger<ThemeService> _logger;

	private IReadOnlyList<PortalTheme> _themes = BuiltInThemes.All;
	private PortalThemeDefaults _defaults = new(BuiltInThemes.PhosphorId, BuiltInThemes.DaylightId);
	private bool _loaded;
	private bool _prefersLight;
	private ResolvedTheme _resolved = ThemeResolver.Resolve(BuiltInThemes.Phosphor);
	private ResolvedTheme? _preview;
	private Task? _initialized;
	private DotNetObjectReference<ThemeService>? _self;

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

	public PortalThemeDefaults Defaults => _defaults;

	public bool PrefersLight => _prefersLight;

	/// <summary>
	/// Restores the last applied theme from localStorage, starts following the browser's light or dark preference,
	/// then reads the game's themes in the background. Program.cs awaits this before the first render; it touches no
	/// network, so that wait is two storage reads and a media query.
	/// </summary>
	public Task InitializeAsync() => _initialized ??= RestoreAsync();

	private async Task RestoreAsync()
	{
		try
		{
			_self = DotNetObjectReference.Create(this);
			_prefersLight = await _js.InvokeAsync<bool>("sharpmushLayout.watchLightScheme", _self);
		}
		catch (JSException ex)
		{
			_logger.LogDebug(ex, "Could not read the browser's colour scheme; assuming dark");
		}

		var light = _prefersLight ? await CachedAsync(LightCacheKey) : null;
		if ((light ?? await CachedAsync(CacheKey)) is { } cached)
		{
			_resolved = cached;
		}

		_ = ReloadAsync();
	}

	private async Task<ResolvedTheme?> CachedAsync(string key)
	{
		try
		{
			return await _js.GetItemAsync(BrowserStore.Local, key) is { Length: > 0 } json
				&& JsonSerializer.Deserialize<ResolvedTheme>(json) is { Tokens: not null, Style: not null, Parts: not null } cached
					? cached
					: null;
		}
		catch (JsonException ex)
		{
			_logger.LogDebug(ex, "No usable cached theme under {Key}", key);
			return null;
		}
	}

	/// <summary>The browser switched between light and dark.</summary>
	[JSInvokable]
	public void OnLightSchemeChanged(bool prefersLight)
	{
		_prefersLight = prefersLight;
		Apply();
	}

	public async Task ReloadAsync()
	{
		var result = await _http.CreateClient("api").GetApiAsync<PortalThemesResponse>("api/themes", "The server listed no themes.");
		if (result is PortalThemesResponse themes)
		{
			_themes = themes.Themes;
			_defaults = themes.Defaults;
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
		ResolvedTheme For(bool light) => ThemeResolver.Resolve(
			ThemeResolver.Pick(published, _defaults, light, character?.ThemeId), character?.Accent);

		var dark = For(light: false);
		var light = For(light: true);
		var resolved = _prefersLight ? light : dark;
		_ = SaveAsync(dark, light.ThemeId == dark.ThemeId ? null : light);
		if (resolved.Css == _resolved.Css && resolved.ThemeId == _resolved.ThemeId) return;

		_resolved = resolved;
		OnThemeChanged?.Invoke();
	}

	private async Task SaveAsync(ResolvedTheme theme, ResolvedTheme? light)
	{
		await _js.SetItemAsync(BrowserStore.Local, CacheKey, JsonSerializer.Serialize(theme));
		if (light is null)
		{
			await _js.RemoveItemAsync(BrowserStore.Local, LightCacheKey);
		}
		else
		{
			await _js.SetItemAsync(BrowserStore.Local, LightCacheKey, JsonSerializer.Serialize(light));
		}
	}

	public void Dispose()
	{
		_account.ActiveCharacterChanged -= Apply;
		_account.AppearanceChanged -= Apply;
		_self?.Dispose();
	}
}

/// <summary>Converts a <see cref="ResolvedTheme"/> to a MudBlazor <see cref="MudTheme"/>.</summary>
public static class ResolvedThemeExtensions
{
	// The theme's own faces, through the custom properties its stylesheet sets.
	private static readonly string[] UiFontFamily = ["var(--font-ui)"];
	private static readonly string[] DisplayFontFamily = ["var(--font-display)"];
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
			// A theme cached before these tokens existed lacks them until the game's themes load.
			palette.Info = t.GetValueOrDefault("info", theme.Dark ? "#5aa9ff" : "#1f63b8");
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
			H1 = new H1Typography { FontFamily = DisplayFontFamily, FontWeight = "600" },
			H2 = new H2Typography { FontFamily = DisplayFontFamily, FontWeight = "600" },
			H3 = new H3Typography { FontFamily = DisplayFontFamily, FontWeight = "600" },
			H4 = new H4Typography { FontFamily = DisplayFontFamily, FontWeight = "600" },
			H5 = new H5Typography { FontFamily = DisplayFontFamily, FontWeight = "600" },
			H6 = new H6Typography { FontFamily = DisplayFontFamily, FontWeight = "600" },
			Button = new ButtonTypography { FontFamily = UiFontFamily, FontWeight = "500" },
			Caption = new CaptionTypography { FontFamily = MonoFontFamily, FontSize = "11px" },
			Overline = new OverlineTypography { FontFamily = MonoFontFamily },
		};

		var layout = new LayoutProperties
		{
			DefaultBorderRadius = "var(--radius)",
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
