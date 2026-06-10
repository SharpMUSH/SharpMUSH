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

	/// <summary>
	/// Normalises a category for storage: trimmed, lower-cased; null/whitespace → null.
	/// </summary>
	public static string? NormalizeCategory(string? category)
	{
		var trimmed = category?.Trim().ToLowerInvariant();
		return string.IsNullOrEmpty(trimmed) ? null : trimmed;
	}

	/// <summary>
	/// Normalises a tag list for storage: trimmed, lower-cased, blanks removed,
	/// de-duplicated, sorted for stable comparisons.
	/// </summary>
	public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags) =>
		tags is null
			? []
			: tags
				.Select(t => t.Trim().ToLowerInvariant())
				.Where(t => t.Length > 0)
				.Distinct()
				.OrderBy(t => t, StringComparer.Ordinal)
				.ToList();
}
