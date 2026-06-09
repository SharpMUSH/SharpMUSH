namespace SharpMUSH.Library.Services;

/// <summary>
/// Shared helpers for wiki page slug computation and namespace normalization.
/// </summary>
public static class WikiHelpers
{
	/// <summary>
	/// Converts a wiki page title or target string into a URL-safe slug.
	/// Rules: lowercase, spaces replaced with underscores, other characters preserved.
	/// </summary>
	public static string Slugify(string text) =>
		text.ToLowerInvariant().Replace(' ', '_');

	/// <summary>
	/// Returns the normalised string key for the slug index: "{namespace}:{slug}".
	/// </summary>
	public static string SlugKey(string nsStr, string slug) =>
		$"{nsStr.ToLowerInvariant()}:{slug}";
}
