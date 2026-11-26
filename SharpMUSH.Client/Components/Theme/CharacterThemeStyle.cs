using MudBlazor;
using MudBlazor.Utilities;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Components.Theme;

/// <summary>The custom properties that paint one part of the page in a theme other than the page's.</summary>
public static class CharacterThemeStyle
{
	/// <summary>
	/// Every token, the accent, the colour scheme, and MudBlazor's palette colours made from the same theme, so its
	/// controls inside the part match the portal's own.
	/// </summary>
	public static string Of(ResolvedTheme theme)
	{
		var mud = theme.ToMudTheme();
		MudBlazor.Palette palette = theme.Dark ? mud.PaletteDark : mud.PaletteLight;
		(string Name, MudColor Color)[] colours =
		[
			("primary", palette.Primary), ("primary-text", palette.PrimaryContrastText),
			("secondary", palette.Secondary), ("tertiary", palette.Tertiary),
			("background", palette.Background), ("surface", palette.Surface),
			("text-primary", palette.TextPrimary), ("text-secondary", palette.TextSecondary), ("text-disabled", palette.TextDisabled),
			("action-default", palette.ActionDefault), ("lines-default", palette.LinesDefault), ("lines-inputs", palette.LinesInputs),
			("table-lines", palette.TableLines), ("divider", palette.Divider),
			("info", palette.Info), ("success", palette.Success), ("warning", palette.Warning), ("error", palette.Error),
		];
		return string.Concat(theme.Tokens.Select(t => $"--{t.Key}:{t.Value};"))
			+ $"--accent:{theme.Accent};color-scheme:{(theme.Dark ? "dark" : "light")};"
			+ string.Concat(colours.Select(c =>
				$"--mud-palette-{c.Name}:{c.Color.ToString(MudColorOutputFormats.RGBA)};--mud-palette-{c.Name}-rgb:{c.Color.ToString(MudColorOutputFormats.ColorElements)};"));
	}
}
