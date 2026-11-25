using System.Text;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using ThemeColor = SharpMUSH.Library.Models.Portal.ThemeColor;

namespace SharpMUSH.Implementation.Services;

/// <summary>The staff-made themes and the default (expanded server data). Built-in themes are not stored.</summary>
public sealed class PortalThemesData
{
	public List<StoredPortalTheme> Themes { get; set; } = [];

	/// <summary>The default for browsers that prefer dark. Null until staff pick one: <see cref="BuiltInThemes.Phosphor"/>.</summary>
	public string? DefaultThemeId { get; set; }

	/// <summary>The default for browsers that prefer light. Null until staff pick one: <see cref="BuiltInThemes.Daylight"/>.</summary>
	public string? DefaultLightThemeId { get; set; }
}

public sealed class StoredPortalTheme
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public bool Dark { get; set; }
	public bool Published { get; set; }
	public Dictionary<string, string> Tokens { get; set; } = [];

	/// <summary>The theme's own stylesheet (<see cref="ThemeStylesheet"/>), or null.</summary>
	public string? Stylesheet { get; set; }

	/// <summary>The theme package that installed the theme, or null for one staff made.</summary>
	public string? Package { get; set; }

	/// <summary>The version of <see cref="Package"/> that installed it, or null.</summary>
	public string? PackageVersion { get; set; }

	/// <summary>The stored pictures (wiki asset ids) the theme's stylesheet shows, which go when the theme does.</summary>
	public List<string> Assets { get; set; } = [];

	public PortalTheme ToTheme() => new(Id, Name, Dark, Published, Tokens, Stylesheet: string.IsNullOrWhiteSpace(Stylesheet) ? null : Stylesheet)
	{
		Package = Package,
		PackageVersion = PackageVersion
	};
}

/// <summary>A character's portal look (expanded object data on the player).</summary>
public sealed class PortalAppearanceData
{
	public string? ThemeId { get; set; }
	public string? Accent { get; set; }

	/// <summary>The player's colour vision (<see cref="ThemeVision"/>); empty for typical.</summary>
	public string? Vision { get; set; }

	/// <summary>Whether pictures take the theme's picture tone; null (never chosen) is on.</summary>
	public bool? ImageFilters { get; set; }

	/// <summary>Whether the portal holds still; null (never chosen) is off.</summary>
	public bool? ReduceMotion { get; set; }
}

/// <inheritdoc cref="IPortalThemeService"/>
public class PortalThemeService(IExpandedObjectDataService data) : IPortalThemeService
{
	public const int MaxNameLength = 40;

	// One record holds every theme: two saves at once must not each drop the other's change.
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	/// <inheritdoc />
	public async ValueTask<PortalThemesResponse> GetThemesAsync(bool includeUnpublished)
		=> Response(await LoadAsync(), includeUnpublished);

	/// <inheritdoc />
	public async ValueTask<Result<PortalTheme>> CreateAsync(PortalThemeRequest request)
	{
		Result<PortalTheme> result = new Error<string>("Not saved.");
		await ChangeAsync(stored =>
		{
			if (Invalid(request, stored, exceptId: null) is { } problem)
			{
				result = new Error<string>(problem);
				return false;
			}

			var theme = Store(new StoredPortalTheme { Id = NewId(request.Name, stored) }, request);
			stored.Themes.Add(theme);
			result = theme.ToTheme();
			return true;
		});
		return result;
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<PortalTheme>> UpdateAsync(string id, PortalThemeRequest request)
	{
		FoundResult<PortalTheme> result = new NotFound();
		await ChangeAsync(stored =>
		{
			if (BuiltIn(id))
			{
				result = new Error<string>("A built-in theme cannot be changed. Duplicate it instead.");
				return false;
			}

			if (stored.Themes.FirstOrDefault(t => t.Id == id) is not { } theme)
			{
				return false;
			}

			if (theme.Package is { } package)
			{
				result = new Error<string>($"{theme.Name} comes from the {package} package, which is the only thing that changes it. Duplicate it to make one of your own.");
				return false;
			}

			if (Invalid(request, stored, exceptId: id) is { } problem)
			{
				result = new Error<string>(problem);
				return false;
			}

			if (request.Dark != theme.Dark && IsDefault(stored, id))
			{
				result = new Error<string>("A default theme keeps its light or dark mode. Make another theme the default first.");
				return false;
			}

			if (!request.Published && IsDefault(stored, id))
			{
				result = new Error<string>("The default theme must stay published. Make another theme the default first.");
				return false;
			}

			result = Store(theme, request).ToTheme();
			return true;
		});
		return result;
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<PortalThemesResponse>> DeleteAsync(string id)
	{
		FoundResult<PortalThemesResponse> result = new NotFound();
		await ChangeAsync(stored =>
		{
			if (BuiltIn(id))
			{
				result = new Error<string>("A built-in theme cannot be deleted.");
				return false;
			}

			if (stored.Themes.FirstOrDefault(t => t.Id == id) is not { } theme)
			{
				return false;
			}

			if (theme.Package is { } package)
			{
				result = new Error<string>($"{theme.Name} comes from the {package} package. Uninstall the package to remove it.");
				return false;
			}

			if (IsDefault(stored, id))
			{
				result = new Error<string>("The default theme cannot be deleted. Make another theme the default first.");
				return false;
			}

			stored.Themes.Remove(theme);
			result = Response(stored, includeUnpublished: true);
			return true;
		});
		return result;
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<PortalThemesResponse>> SetDefaultAsync(string id)
	{
		FoundResult<PortalThemesResponse> result = new NotFound();
		await ChangeAsync(stored =>
		{
			if (All(stored).FirstOrDefault(t => t.Id == id) is not { } theme)
			{
				return false;
			}

			if (!theme.Published)
			{
				result = new Error<string>("Only a published theme can be the default.");
				return false;
			}

			if (theme.Dark)
			{
				stored.DefaultThemeId = id;
			}
			else
			{
				stored.DefaultLightThemeId = id;
			}

			result = Response(stored, includeUnpublished: true);
			return true;
		});
		return result;
	}

	/// <inheritdoc />
	public async ValueTask<CharacterAppearance> GetAppearanceAsync(SharpObject character)
		=> await data.GetExpandedDataAsync<PortalAppearanceData>(character) is { } appearance
			? new CharacterAppearance(NullIfEmpty(appearance.ThemeId), NullIfEmpty(appearance.Accent), ThemeVision.Chosen(appearance.Vision))
			{
				ImageFilters = appearance.ImageFilters ?? true,
				ReduceMotion = appearance.ReduceMotion ?? false,
			}
			: new CharacterAppearance(null, null);

	private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

	/// <inheritdoc />
	public async ValueTask<Result<CharacterAppearance>> SetAppearanceAsync(SharpObject character, CharacterAppearance appearance)
	{
		string? accent = null;
		if (!string.IsNullOrWhiteSpace(appearance.Accent))
		{
			if (!ThemeColor.TryParse(appearance.Accent, out var color))
			{
				return new Error<string>($"'{appearance.Accent}' is not a #rrggbb colour.");
			}

			accent = color.Hex;
		}

		if (!string.IsNullOrWhiteSpace(appearance.Vision) && !ThemeVision.All.Contains(appearance.Vision))
		{
			return new Error<string>($"Colour vision is one of {string.Join(", ", ThemeVision.All)}, not '{appearance.Vision}'.");
		}

		var vision = ThemeVision.Chosen(appearance.Vision);
		var themeId = string.IsNullOrWhiteSpace(appearance.ThemeId) ? null : appearance.ThemeId;
		if (themeId is not null && !Response(await LoadAsync(), includeUnpublished: false).Themes.Any(t => t.Id == themeId))
		{
			return new Error<string>($"There is no theme '{themeId}' to choose.");
		}

		// Per-object data is merged over what is stored, and a null property keeps the stored value, so a cleared
		// choice is written as an empty string.
		await data.SetExpandedDataAsync(new PortalAppearanceData
		{
			ThemeId = themeId ?? "",
			Accent = accent ?? "",
			Vision = vision ?? "",
			ImageFilters = appearance.ImageFilters,
			ReduceMotion = appearance.ReduceMotion,
		}, character);
		return new CharacterAppearance(themeId, accent, vision) { ImageFilters = appearance.ImageFilters, ReduceMotion = appearance.ReduceMotion };
	}

	/// <summary>The themes and defaults as stored, for planning a theme package against.</summary>
	public ValueTask<PortalThemesData> GetStoredAsync() => LoadAsync();

	/// <summary>
	/// Replaces the themes <paramref name="packageId"/> installed with <paramref name="themes"/>, under the same lock as
	/// every other change, after planning them again against what is stored now (<see cref="PackageThemePlan"/>): a theme
	/// whose id or name another theme took since the review, or a default the package would drop, refuses the whole
	/// change. With <paramref name="dropDefaults"/> a dropped default goes back to the built-in one. Answers the stored
	/// pictures the package's previous themes showed that the new ones do not, for the caller to delete.
	/// </summary>
	public async ValueTask<Result<IReadOnlyList<string>>> ReplacePackageThemesAsync(
		string packageId, IReadOnlyList<StoredPortalTheme> themes, bool dropDefaults = false)
	{
		Result<IReadOnlyList<string>> result = new Error<string>("Not saved.");
		await ChangeAsync(stored =>
		{
			var plan = PackageThemePlan.Compute(stored, packageId, themes.Select(t => (t.Id, t.Name, t.Dark)).ToList(), dropDefaults);
			if (plan.FirstOrDefault(change => change.Action == PackageThemeAction.Blocked) is { } blocked)
			{
				result = new Error<string>($"Theme {blocked.Id}: {blocked.Detail}");
				return false;
			}

			var previous = stored.Themes.Where(t => t.Package == packageId).ToList();
			if (dropDefaults)
			{
				var kept = themes.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
				if (stored.DefaultThemeId is { } dark && previous.Any(t => t.Id == dark) && !kept.Contains(dark)) stored.DefaultThemeId = null;
				if (stored.DefaultLightThemeId is { } light && previous.Any(t => t.Id == light) && !kept.Contains(light)) stored.DefaultLightThemeId = null;
			}

			// The package's themes keep their place in the list, and a new one goes where the first of them was.
			var index = stored.Themes.FindIndex(t => t.Package == packageId);
			stored.Themes.RemoveAll(t => t.Package == packageId);
			stored.Themes.InsertRange(index < 0 ? stored.Themes.Count : index, themes);
			// A picture is released only when no stored theme still shows it: a staff copy of a packaged theme keeps
			// the package's addresses in its stylesheet.
			var used = themes.SelectMany(t => t.Assets).ToHashSet(StringComparer.Ordinal);
			result = previous.SelectMany(t => t.Assets)
				.Where(asset => !used.Contains(asset)
					&& !stored.Themes.Any(t => t.Stylesheet?.Contains($"/api/wiki-assets/{asset}/", StringComparison.Ordinal) == true))
				.Distinct().ToList();
			return true;
		});
		return result;
	}

	/// <summary>The themes <paramref name="packageId"/> installed, as stored.</summary>
	public async ValueTask<IReadOnlyList<StoredPortalTheme>> GetPackageThemesAsync(string packageId)
		=> (await LoadAsync()).Themes.Where(t => t.Package == packageId).ToList();

	internal static bool BuiltIn(string id) => BuiltInThemes.All.Any(t => t.Id == id);

	internal static bool IsDefault(PortalThemesData stored, string id)
		=> (stored.DefaultThemeId ?? BuiltInThemes.PhosphorId) == id || (stored.DefaultLightThemeId ?? BuiltInThemes.DaylightId) == id;

	internal static IEnumerable<PortalTheme> All(PortalThemesData stored)
		=> BuiltInThemes.All.Concat(stored.Themes.Select(t => t.ToTheme()));

	private static PortalThemesResponse Response(PortalThemesData stored, bool includeUnpublished)
	{
		var themes = All(stored).Where(t => includeUnpublished || t.Published).ToList();
		string Usable(string? id, string fallback) => themes.Any(t => t.Id == id && t.Published) ? id! : fallback;
		return new PortalThemesResponse(themes,
			Usable(stored.DefaultThemeId, BuiltInThemes.PhosphorId), Usable(stored.DefaultLightThemeId, BuiltInThemes.DaylightId));
	}

	private static string? Invalid(PortalThemeRequest request, PortalThemesData stored, string? exceptId)
	{
		var name = request.Name?.Trim() ?? string.Empty;
		if (name.Length is 0 or > MaxNameLength)
		{
			return $"A theme's name is 1 to {MaxNameLength} characters.";
		}

		if (All(stored).Any(t => t.Id != exceptId && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
		{
			return $"There is already a theme named '{name}'.";
		}

		return ThemeResolver.Validate(request.Tokens) is [var first, ..] ? first
			: ThemeStylesheet.Validate(request.Stylesheet) is [var sheet, ..] ? sheet
			: null;
	}

	/// <summary>A theme's colours and style choices as stored: every colour normalised, and every style choice set.</summary>
	public static Dictionary<string, string> CompleteTokens(IReadOnlyDictionary<string, string> tokens)
	{
		var (colors, style) = ThemeResolver.Complete(tokens);
		return colors.Concat(style).ToDictionary();
	}

	private static StoredPortalTheme Store(StoredPortalTheme theme, PortalThemeRequest request)
	{
		theme.Name = request.Name.Trim();
		theme.Dark = request.Dark;
		theme.Published = request.Published;
		theme.Tokens = CompleteTokens(request.Tokens);
		theme.Stylesheet = string.IsNullOrWhiteSpace(request.Stylesheet) ? null : request.Stylesheet;
		return theme;
	}

	/// <summary>A lower-case slug of the name, numbered when taken.</summary>
	private static string NewId(string name, PortalThemesData stored)
	{
		var slug = new StringBuilder();
		foreach (var c in name.Trim().ToLowerInvariant())
		{
			if (char.IsAsciiLetterOrDigit(c)) slug.Append(c);
			else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
		}

		// "default" is the route that sets the default theme.
		var root = slug.ToString().Trim('-') is { Length: > 0 } s and not "default" ? s : "theme";
		var id = root;
		for (var n = 2; All(stored).Any(t => t.Id == id); n++)
		{
			id = $"{root}-{n}";
		}

		return id;
	}

	private async ValueTask<PortalThemesData> LoadAsync()
		=> await data.GetExpandedServerDataAsync<PortalThemesData>() ?? new PortalThemesData();

	/// <summary>Runs <paramref name="change"/> on the stored themes under the lock, and writes them when it returns true.</summary>
	private async ValueTask ChangeAsync(Func<PortalThemesData, bool> change)
	{
		await _writeLock.WaitAsync();
		try
		{
			var stored = await LoadAsync();
			if (change(stored))
			{
				await data.SetExpandedServerDataAsync(stored);
			}
		}
		finally
		{
			_writeLock.Release();
		}
	}
}
