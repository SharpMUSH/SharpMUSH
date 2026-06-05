namespace SharpMUSH.Client.Models;

/// <summary>
/// A named theme preset for the Blazor portal. Stores MudBlazor palette properties.
/// </summary>
public record ThemePreset(
	string Name,
	bool IsDark,
	bool IsDefault,
	string Primary,
	string Secondary,
	string Tertiary,
	string Info,
	string Success,
	string Warning,
	string Error,
	string Dark,
	string TextPrimary,
	string TextSecondary,
	string TextDisabled,
	string Surface,
	string Background,
	string AppbarBackground,
	string DrawerBackground,
	string ActionDefault,
	string ActionDisabled,
	string Dividers,
	string OverlayDark)
{
	/// <summary>
	/// Creates a ThemePreset from the current MudBlazor theme.
	/// </summary>
	public static ThemePreset FromMudTheme(string name, bool isDark, MudBlazor.MudTheme theme)
	{
		var palette = isDark ? theme.PaletteDark : theme.PaletteLight;
		
		return new ThemePreset(
			Name: name,
			IsDark: isDark,
			IsDefault: false,
			Primary: palette.Primary,
			Secondary: palette.Secondary,
			Tertiary: palette.Tertiary,
			Info: palette.Info,
			Success: palette.Success,
			Warning: palette.Warning,
			Error: palette.Error,
			Dark: palette.Dark,
			TextPrimary: palette.TextPrimary,
			TextSecondary: palette.TextSecondary,
			TextDisabled: palette.TextDisabled,
			Surface: palette.Surface,
			Background: palette.Background,
			AppbarBackground: palette.AppbarBackground,
			DrawerBackground: palette.DrawerBackground,
			ActionDefault: palette.ActionDefault,
			ActionDisabled: palette.ActionDisabled,
			Dividers: palette.Dividers,
			OverlayDark: palette.OverlayDark
		);
	}

	/// <summary>
	/// Converts this preset back to a MudBlazor theme.
	/// </summary>
	public MudBlazor.MudTheme ToMudTheme()
	{
		var palette = IsDark
			? new MudBlazor.PaletteDark
			{
				Primary = MudBlazor.MudColor.Parse(Primary),
				Secondary = MudBlazor.MudColor.Parse(Secondary),
				Tertiary = MudBlazor.MudColor.Parse(Tertiary),
				Info = MudBlazor.MudColor.Parse(Info),
				Success = MudBlazor.MudColor.Parse(Success),
				Warning = MudBlazor.MudColor.Parse(Warning),
				Error = MudBlazor.MudColor.Parse(Error),
				Dark = MudBlazor.MudColor.Parse(Dark),
				TextPrimary = TextPrimary,
				TextSecondary = TextSecondary,
				TextDisabled = TextDisabled,
				Surface = MudBlazor.MudColor.Parse(Surface),
				Background = MudBlazor.MudColor.Parse(Background),
				AppbarBackground = MudBlazor.MudColor.Parse(AppbarBackground),
				DrawerBackground = MudBlazor.MudColor.Parse(DrawerBackground),
				ActionDefault = MudBlazor.MudColor.Parse(ActionDefault),
				ActionDisabled = MudBlazor.MudColor.Parse(ActionDisabled),
				Dividers = MudBlazor.MudColor.Parse(Dividers),
				OverlayDark = MudBlazor.MudColor.Parse(OverlayDark)
			}
			: new MudBlazor.PaletteLight
			{
				Primary = MudBlazor.MudColor.Parse(Primary),
				Secondary = MudBlazor.MudColor.Parse(Secondary),
				Tertiary = MudBlazor.MudColor.Parse(Tertiary),
				Info = MudBlazor.MudColor.Parse(Info),
				Success = MudBlazor.MudColor.Parse(Success),
				Warning = MudBlazor.MudColor.Parse(Warning),
				Error = MudBlazor.MudColor.Parse(Error),
				Dark = MudBlazor.MudColor.Parse(Dark),
				TextPrimary = TextPrimary,
				TextSecondary = TextSecondary,
				TextDisabled = TextDisabled,
				Surface = MudBlazor.MudColor.Parse(Surface),
				Background = MudBlazor.MudColor.Parse(Background),
				AppbarBackground = MudBlazor.MudColor.Parse(AppbarBackground),
				DrawerBackground = MudBlazor.MudColor.Parse(DrawerBackground),
				ActionDefault = MudBlazor.MudColor.Parse(ActionDefault),
				ActionDisabled = MudBlazor.MudColor.Parse(ActionDisabled),
				Dividers = MudBlazor.MudColor.Parse(Dividers),
				OverlayDark = MudBlazor.MudColor.Parse(OverlayDark)
			};

		var theme = new MudBlazor.MudTheme();
		if (IsDark)
			theme.PaletteDark = palette as MudBlazor.PaletteDark;
		else
			theme.PaletteLight = palette as MudBlazor.PaletteLight;

		return theme;
	}
}
