using System.Text.RegularExpressions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>themes:</c> block of a <c>kind: theme</c> package (format 1.22).</summary>
public partial class PackageManifestService
{
	/// <summary>The first format with <c>kind: theme</c> and <c>themes:</c>.</summary>
	public static PackageFormatVersion ThemeFormat { get; } = new(1, 22);

	/// <summary>The most themes one package may ship.</summary>
	public const int MaxPackageThemes = 20;

	/// <summary>The most pictures one packaged theme may carry.</summary>
	public const int MaxThemeImages = 20;

	/// <summary>The longest id a packaged theme may have.</summary>
	public const int MaxThemeIdLength = 40;

	private static readonly IReadOnlySet<string> KnownThemeKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"id", "name", "dark", "tokens", "stylesheet", "images"
	};

	/// <summary>
	/// The blocks only a package that changes the game itself may carry; a theme package ships themes alone. The readers of
	/// these skip a theme package, and <c>application</c>, <c>binaries</c> and <c>wiki_landing_page</c> report themselves.
	/// </summary>
	private static readonly IReadOnlyList<string> ThemeForbiddenKeys =
	[
		"configure", "categories", "permissions", "roles", "settings", "wiki", "wiki_collection_name", "plugins", "attributes",
		"grants", "wiki_properties"
	];

	[GeneratedRegex("^[a-z][a-z0-9-]*$")]
	private static partial Regex ThemeIdRegex();

	/// <summary>
	/// Reads <c>themes:</c>: required for a theme package, forbidden otherwise. Each entry names its id, name and
	/// whether it is dark, its colours and style choices as the theme editor saves them, and optionally a stylesheet
	/// file and the pictures that stylesheet shows. The files are read later (<see cref="PackageThemeFiles"/>).
	/// </summary>
	private static IReadOnlyList<PackageThemeSpec> ReadThemes(
		Dictionary<string, object?> doc, PackageKind kind, PackageFormatVersion format, List<PackageManifestIssue> issues)
	{
		var present = doc.TryGetValue("themes", out var node) && node is not null;
		if (kind != PackageKind.Theme)
		{
			if (present)
			{
				issues.Add(PackageManifestIssue.Error("themes", "'themes' is only valid when 'kind: theme'."));
			}

			return [];
		}

		if (format.Major == ThemeFormat.Major && format.Minor < ThemeFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Error("kind", $"'kind: theme' needs format {ThemeFormat} or later; set format: \"{ThemeFormat}\"."));
		}

		foreach (var key in ThemeForbiddenKeys.Where(doc.ContainsKey))
		{
			issues.Add(PackageManifestIssue.Error(key, $"A theme package ships themes only. Remove '{key}'."));
		}

		if (!present)
		{
			issues.Add(PackageManifestIssue.Error("themes", "A theme package requires a 'themes' list."));
			return [];
		}

		if (node is not List<object> { Count: > 0 } list)
		{
			issues.Add(PackageManifestIssue.Error("themes", "'themes' must be a list of at least one theme."));
			return [];
		}

		if (list.Count > MaxPackageThemes)
		{
			issues.Add(PackageManifestIssue.Error("themes", $"A package ships at most {MaxPackageThemes} themes; this one lists {list.Count}."));
			return [];
		}

		var themes = new List<PackageThemeSpec>();
		foreach (var (entry, path) in Entries(doc, "themes", "themes", KnownThemeKeys, issues))
		{
			if (ReadTheme(entry, path, issues) is not { } theme)
			{
				continue;
			}

			if (themes.Any(t => t.Id == theme.Id))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.id", $"Theme '{theme.Id}' is listed twice."));
				continue;
			}

			if (themes.Any(t => string.Equals(t.Name, theme.Name, StringComparison.OrdinalIgnoreCase)))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", $"Two themes are named '{theme.Name}'."));
				continue;
			}

			themes.Add(theme);
		}

		return themes;
	}

	private static PackageThemeSpec? ReadTheme(Dictionary<string, object?> entry, string path, List<PackageManifestIssue> issues)
	{
		var ok = true;
		var id = (entry.GetValueOrDefault("id") as string)?.Trim();
		if (string.IsNullOrEmpty(id) || !ThemeIdRegex().IsMatch(id) || id.Length > MaxThemeIdLength || id == "default")
		{
			issues.Add(PackageManifestIssue.Error($"{path}.id",
				$"'{id}' is not a theme id: lower-case letters, digits and hyphens, starting with a letter, at most {MaxThemeIdLength} long, and not 'default'."));
			ok = false;
		}

		var name = string.Join(' ', ((entry.GetValueOrDefault("name") as string) ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		if (name.Length is 0 or > PortalThemeNameLimit)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.name", $"A theme's name is 1 to {PortalThemeNameLimit} characters."));
			ok = false;
		}

		if (!bool.TryParse(entry.GetValueOrDefault("dark")?.ToString(), out var dark))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.dark", "'dark' is required: true for a dark theme, false for a light one."));
			ok = false;
		}

		var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
		if (entry.GetValueOrDefault("tokens") is Dictionary<object, object> rawTokens)
		{
			foreach (var (key, value) in rawTokens)
			{
				tokens[key.ToString() ?? ""] = value?.ToString() ?? "";
			}

			foreach (var problem in ThemeResolver.Validate(tokens))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.tokens", problem));
				ok = false;
			}
		}
		else
		{
			issues.Add(PackageManifestIssue.Error($"{path}.tokens",
				"'tokens' is required: a mapping of the theme's colours and style choices, as the theme editor saves them."));
			ok = false;
		}

		string? stylesheet = null;
		if (entry.GetValueOrDefault("stylesheet") is { } sheetNode)
		{
			stylesheet = PackageFilePath.Normalize(sheetNode as string);
			if (stylesheet is null || !stylesheet.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
				|| !PackageThemeFiles.SafePath().IsMatch(stylesheet))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.stylesheet",
					$"'{sheetNode}' is not a .css file in the package: a relative path of {SafePathCharacters} with / between folders and no '..'."));
				ok = false;
			}
		}

		var images = new List<PackageThemeImage>();
		switch (entry.GetValueOrDefault("images"))
		{
			case null:
				break;
			case List<object> list when list.Count > MaxThemeImages:
				issues.Add(PackageManifestIssue.Error($"{path}.images", $"A theme carries at most {MaxThemeImages} pictures; this one lists {list.Count}."));
				ok = false;
				break;
			case List<object> list:
				for (var i = 0; i < list.Count; i++)
				{
					var file = PackageFilePath.Normalize(list[i] as string);
					if (file is null || PackageThemeImageTypes.For(file) is null || !PackageThemeFiles.SafePath().IsMatch(file))
					{
						issues.Add(PackageManifestIssue.Error($"{path}.images[{i}]",
							$"'{list[i]}' is not a picture in the package: a relative path of {SafePathCharacters} ending in "
							+ $"{string.Join(", ", PackageThemeImageTypes.ByExtension.Keys.Select(e => "." + e))}."));
						ok = false;
						continue;
					}

					if (images.Any(image => image.File == file))
					{
						issues.Add(PackageManifestIssue.Error($"{path}.images[{i}]", $"'{file}' is listed twice."));
						ok = false;
						continue;
					}

					images.Add(new PackageThemeImage(file));
				}

				break;
			default:
				issues.Add(PackageManifestIssue.Error($"{path}.images", "'images' must be a list of file paths."));
				ok = false;
				break;
		}

		if (stylesheet is not null && ok && ThemeStyles.ModeOf(tokens, stylesheet) != ThemeStyles.Custom)
		{
			issues.Add(PackageManifestIssue.Warning($"{path}.tokens.mode",
				$"The stylesheet applies only to a custom theme, and this one's mode is '{tokens[ThemeStyles.Mode]}'. Set mode: custom, or leave it out."));
		}

		if (images.Count > 0 && stylesheet is null && ok)
		{
			issues.Add(PackageManifestIssue.Warning($"{path}.images", "The theme carries pictures but no stylesheet to show them."));
		}

		return ok ? new PackageThemeSpec(id!, name, dark, tokens, stylesheet, images) : null;
	}

	/// <summary>What a theme's file paths may hold, for the messages.</summary>
	private const string SafePathCharacters = "letters, digits, '.', '_' and '-'";

	/// <summary>The longest name a theme may have; the same as a staff-made theme's.</summary>
	private const int PortalThemeNameLimit = 40;
}
