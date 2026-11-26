using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// What installing a theme package's themes does to the game's: each one added, replaced or blocked, and each theme
/// an earlier version installed that this one drops. Pure: the plan, the review screen and the write under the theme
/// lock all compute it the same way.
/// </summary>
public static class PackageThemePlan
{
	/// <summary>
	/// The changes that installing <paramref name="themes"/> as <paramref name="packageId"/>'s would make to
	/// <paramref name="stored"/>. A theme is blocked when a built-in theme, a staff theme or another package's has its id
	/// or its name, or when it is a default and the version makes it the other mode; a theme the version drops is blocked
	/// while it is a default, unless <paramref name="dropDefaults"/>.
	/// </summary>
	public static IReadOnlyList<PackageThemeChange> Compute(PortalThemesData stored, string packageId,
		IReadOnlyList<(string Id, string Name, bool Dark)> themes, bool dropDefaults = false)
	{
		var others = PortalThemeService.All(stored).Where(t => t.Package != packageId).ToList();
		var owned = stored.Themes.Where(t => t.Package == packageId).ToList();
		var changes = new List<PackageThemeChange>();
		foreach (var (id, name, dark) in themes)
		{
			var installed = owned.FirstOrDefault(t => t.Id == id);
			var blocked = others.FirstOrDefault(t => t.Id == id) is { } sameId
				? $"{Whose(sameId)} has the id '{id}'."
				: others.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is { } sameName
					? $"{Whose(sameName)} is already named '{sameName.Name}'."
					: installed is not null && installed.Dark != dark && PortalThemeService.IsDefault(stored, id)
						? $"It is the game's default for browsers that prefer {Mode(installed.Dark)}, and this version makes it a {Mode(dark)} theme. Make another theme the default first."
						: null;
			changes.Add(new PackageThemeChange(id, name, dark,
				blocked is not null ? PackageThemeAction.Blocked : installed is null ? PackageThemeAction.Add : PackageThemeAction.Update,
				Detail: blocked));
		}

		foreach (var dropped in owned.Where(t => themes.All(theme => theme.Id != t.Id)))
		{
			var isDefault = PortalThemeService.IsDefault(stored, dropped.Id);
			changes.Add(isDefault && !dropDefaults
				? new PackageThemeChange(dropped.Id, dropped.Name, dropped.Dark, PackageThemeAction.Blocked,
					Detail: $"It is the game's default for browsers that prefer {Mode(dropped.Dark)}, and it would be removed. Make another theme the default first.")
				: new PackageThemeChange(dropped.Id, dropped.Name, dropped.Dark, PackageThemeAction.Remove,
					Detail: isDefault
						? $"It is the game's default for browsers that prefer {Mode(dropped.Dark)}; {(dropped.Dark ? BuiltInThemes.Phosphor.Name : BuiltInThemes.Daylight.Name)} takes its place."
						: "Characters who chose it see the game's default instead."));
		}

		return changes;
	}

	private static string Mode(bool dark) => dark ? "dark" : "light";

	private static string Whose(Library.API.PortalTheme theme)
		=> theme.BuiltIn ? $"The built-in theme {theme.Name}"
			: theme.Package is { } package ? $"The {package} package's theme {theme.Name}"
			: $"The game's own theme {theme.Name}";
}
