namespace SharpMUSH.Library.Markup;

/// <summary>
/// The game's own pictures as PNG, for a reader that cannot decode the format they are stored in. An MXP or
/// Pueblo client reads PNG, JPEG, GIF and BMP (Axmud refuses any other extension), and the terminal
/// renderer the same, so a WebP picture is sent to them at <c>/pictures/png/&lt;its path&gt;.png</c>, which
/// the server answers with the picture converted.
/// </summary>
public static class PngPictures
{
	/// <summary>Where the converted pictures are served.</summary>
	public const string Prefix = "/pictures/png";

	private static readonly string[] ReadableExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];

	/// <summary>
	/// The address of <paramref name="source"/> as PNG when it is one of the game's own pictures (a path from
	/// the root) in a format other than those; otherwise <paramref name="source"/> unchanged.
	/// </summary>
	public static string Readable(string source)
	{
		var path = source.Trim();
		if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)
			|| path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase)
			|| path.IndexOfAny(['?', '#']) >= 0)
			return source;

		var extension = Path.GetExtension(path);
		return extension.Length == 0 || ReadableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
			? source
			: $"{Prefix}{path}.png";
	}

	/// <summary>
	/// The picture a converted address names: <c>/pictures/png/assets/a.webp.png</c> is
	/// <c>/assets/a.webp</c>. Null for anything else.
	/// </summary>
	public static string? Original(string path) =>
		path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase)
		&& path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
		&& path.Length > Prefix.Length + ".png".Length + 1
			? path[Prefix.Length..^".png".Length]
			: null;
}
