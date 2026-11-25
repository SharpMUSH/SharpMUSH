namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// One portal theme a <see cref="PackageKind.Theme"/> package ships (<c>themes:</c>, format 1.21). It installs as a
/// theme of the game's, which staff can make the default and players can choose, and which only the package changes.
/// </summary>
/// <param name="Id">The theme's id, a lower-case slug: what a character's choice and the game's default point at.</param>
/// <param name="Name">The name the theme chooser shows.</param>
/// <param name="Dark">Whether the theme is dark.</param>
/// <param name="Tokens">Its colours and style choices, as the theme editor saves them (<c>ThemeResolver.Validate</c>).</param>
/// <param name="StylesheetFile">The package's file holding the theme's stylesheet, or null for none.</param>
/// <param name="Images">The pictures the stylesheet shows, by their path in the package.</param>
public sealed record PackageThemeSpec(
	string Id,
	string Name,
	bool Dark,
	IReadOnlyDictionary<string, string> Tokens,
	string? StylesheetFile,
	IReadOnlyList<PackageThemeImage> Images)
{
	/// <summary>The stylesheet's text, read from <see cref="StylesheetFile"/> before the package is planned; null until then.</summary>
	public string? Stylesheet { get; init; }

	/// <summary>Whether the stylesheet and every picture have been read from the package.</summary>
	public bool Loaded => (StylesheetFile is null || Stylesheet is not null) && Images.All(image => image.Content is not null);
}

/// <summary>A picture a packaged theme carries.</summary>
/// <param name="File">Its path in the package, which the stylesheet names in <c>url()</c>.</param>
public sealed record PackageThemeImage(string File)
{
	/// <summary>The picture's bytes, read before the package is planned; null until then.</summary>
	public byte[]? Content { get; init; }

	/// <summary>The type it is served as, from its extension.</summary>
	public string ContentType => PackageThemeImageTypes.For(File) ?? "application/octet-stream";
}

/// <summary>The pictures a packaged theme may carry, by file extension.</summary>
public static class PackageThemeImageTypes
{
	/// <summary>Each allowed extension, lower case and without its dot, and the type it is served as.</summary>
	public static readonly IReadOnlyDictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["png"] = "image/png",
		["jpg"] = "image/jpeg",
		["jpeg"] = "image/jpeg",
		["gif"] = "image/gif",
		["webp"] = "image/webp",
		["avif"] = "image/avif",
		["svg"] = "image/svg+xml",
	};

	/// <summary>The type <paramref name="file"/> is served as, or null when its extension is not allowed.</summary>
	public static string? For(string file)
	{
		var dot = file.LastIndexOf('.');
		return dot < 0 ? null : ByExtension.GetValueOrDefault(file[(dot + 1)..].ToLowerInvariant());
	}
}

/// <summary>What happens to one packaged theme in a plan.</summary>
public enum PackageThemeAction
{
	/// <summary>The game has no theme with this id; the package adds it.</summary>
	Add,

	/// <summary>The package already installed this theme; the new version replaces it.</summary>
	Update,

	/// <summary>The new version no longer ships this theme; it is removed, and characters who chose it see the game's default.</summary>
	Remove,

	/// <summary>The theme cannot be applied: its id or name is taken, or it is the default and the version drops it.</summary>
	Blocked
}

/// <summary>One packaged theme's line in a plan.</summary>
/// <param name="Id">The theme's id.</param>
/// <param name="Name">The theme's name: the new version's, or the installed one's for a removal.</param>
/// <param name="Dark">Whether the theme is dark.</param>
/// <param name="Action">What the apply does with it.</param>
/// <param name="Tokens">The new version's colours and style choices, for a preview; null for a removal.</param>
/// <param name="Images">How many pictures it carries.</param>
/// <param name="StylesheetLength">How long its stylesheet is, in characters; 0 for none.</param>
/// <param name="Detail">Why it is blocked, or what a removal means for the game.</param>
public sealed record PackageThemeChange(
	string Id,
	string Name,
	bool Dark,
	PackageThemeAction Action,
	IReadOnlyDictionary<string, string>? Tokens = null,
	int Images = 0,
	int StylesheetLength = 0,
	string? Detail = null);
