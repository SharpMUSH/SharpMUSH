using System.Text;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

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

	public PortalTheme ToTheme() => new(Id, Name, Dark, Published, Tokens);
}

/// <summary>A character's portal look (expanded object data on the player).</summary>
public sealed class PortalAppearanceData
{
	public string? ThemeId { get; set; }
	public string? Accent { get; set; }
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
			? new CharacterAppearance(NullIfEmpty(appearance.ThemeId), NullIfEmpty(appearance.Accent))
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

		var themeId = string.IsNullOrWhiteSpace(appearance.ThemeId) ? null : appearance.ThemeId;
		if (themeId is not null && !Response(await LoadAsync(), includeUnpublished: false).Themes.Any(t => t.Id == themeId))
		{
			return new Error<string>($"There is no theme '{themeId}' to choose.");
		}

		// Per-object data is merged over what is stored, and a null property keeps the stored value, so a cleared
		// choice is written as an empty string.
		await data.SetExpandedDataAsync(new PortalAppearanceData { ThemeId = themeId ?? "", Accent = accent ?? "" }, character);
		return new CharacterAppearance(themeId, accent);
	}

	private static bool BuiltIn(string id) => BuiltInThemes.All.Any(t => t.Id == id);

	private static bool IsDefault(PortalThemesData stored, string id)
		=> (stored.DefaultThemeId ?? BuiltInThemes.PhosphorId) == id || (stored.DefaultLightThemeId ?? BuiltInThemes.DaylightId) == id;

	private static IEnumerable<PortalTheme> All(PortalThemesData stored)
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

		return ThemeResolver.Validate(request.Tokens) is [var first, ..] ? first : null;
	}

	private static StoredPortalTheme Store(StoredPortalTheme theme, PortalThemeRequest request)
	{
		theme.Name = request.Name.Trim();
		theme.Dark = request.Dark;
		theme.Published = request.Published;
		var (colors, style) = ThemeResolver.Complete(request.Tokens);
		theme.Tokens = colors.Concat(style).ToDictionary();
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
