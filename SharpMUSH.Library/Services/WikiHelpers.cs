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
	/// The category assigned to a page when none is supplied. Category is part of a page's
	/// identity (Namespace, Category, Slug), so every page must have one.
	/// </summary>
	public const string DefaultCategory = "general";

	/// <summary>
	/// Returns the normalised string key for the slug index: "{namespace}:{category}:{slug}".
	/// Category is part of page identity, so it participates in the key.
	/// </summary>
	public static string SlugKey(string nsStr, string? category, string slug) =>
		$"{nsStr.ToLowerInvariant()}:{NormalizeCategory(category)}:{slug}";

	/// <summary>
	/// Normalises a category for storage: trimmed, lower-cased; null/whitespace →
	/// <see cref="DefaultCategory"/> (category is required because it is part of page identity).
	/// </summary>
	public static string NormalizeCategory(string? category)
	{
		var trimmed = category?.Trim().ToLowerInvariant();
		return string.IsNullOrEmpty(trimmed) ? DefaultCategory : trimmed;
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
