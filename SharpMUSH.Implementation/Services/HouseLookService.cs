using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <inheritdoc cref="IHouseLookService"/>
public sealed class HouseLookService(IOptionsWrapper<SharpMUSHOptions> configuration, ILayoutThemeService layoutThemes) : IHouseLookService
{
	/// <summary>The <c>layout_border</c> and resolved <c>layout_theme</c> the look was last made from, and the look.</summary>
	private sealed record Made(string? Border, string? Theme, ThemePalette? Palette, LayoutTheme Look);

	private volatile Made? _made;

	/// <inheritdoc />
	public LayoutTheme Look => Current().Look;

	/// <inheritdoc />
	public ThemePalette? Palette => Current().Palette;

	/// <inheritdoc />
	public BorderStyle Border =>
		BorderStyle.Preset(configuration.CurrentValue.Cosmetic.LayoutBorder ?? string.Empty) ?? BorderStyle.Double;

	private Made Current()
	{
		var cosmetic = configuration.CurrentValue.Cosmetic;
		// Keyed on what layout_theme resolves to, so a change to an added theme it names is seen.
		var unset = string.IsNullOrWhiteSpace(cosmetic.LayoutTheme) || cosmetic.LayoutTheme.Trim().Equals(LayoutThemes.None, StringComparison.OrdinalIgnoreCase);
		var theme = unset ? null : layoutThemes.Resolve(cosmetic.LayoutTheme) switch
		{
			string resolved => resolved,
			_ => string.Empty,
		};
		if (_made is { } made && made.Border == cosmetic.LayoutBorder && made.Theme == theme) return made;
		var palette = string.IsNullOrEmpty(theme) ? null : LayoutThemes.Read(theme) switch
		{
			ThemePalette read => read,
			_ => null,
		};
		// A theme with a look of its own brings its border; layout_border is the border of one without.
		var colours = palette?.ToLayoutTheme() ?? LayoutTheme.Default;
		var current = new Made(cosmetic.LayoutBorder, theme, palette, colours with { Border = colours.Border ?? Border });
		_made = current;
		return current;
	}
}
