using OneOf;
using OneOf.Types;
using System.Globalization;

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

	/// <summary>
	/// Canonical form of a locale tag, or <see cref="Error{T}"/> when it is not a locale at all.
	/// Canonical means <see cref="CultureInfo"/>'s own casing — <c>pt-br</c> and <c>PT-BR</c> both become
	/// <c>pt-BR</c> — so the unique (PageId, Locale) index cannot be defeated by casing.
	/// </summary>
	/// <remarks>
	/// This is the <em>write</em> boundary: every point a locale enters storage or configuration goes
	/// through it, so no unparseable tag can be in the database to begin with. Read paths want
	/// <see cref="NormalizeLocaleOrEmpty"/> instead, because a reader typing a bad <c>?lang=</c> should get
	/// the default page rather than an error.
	/// <para>
	/// <c>predefinedOnly: true</c> is required. Without it .NET accepts any well-formed tag as a
	/// pseudo-culture, so junk like <c>qq</c> would become a "valid" locale and get persisted.
	/// </para>
	/// </remarks>
	public static OneOf<string, Error<string>> NormalizeLocale(string? locale)
	{
		var normalized = NormalizeLocaleOrEmpty(locale);
		return normalized.Length == 0
			? new Error<string>($"'{locale}' is not a recognised BCP-47 locale tag.")
			: normalized;
	}

	/// <summary>
	/// The same canonicalisation as <see cref="NormalizeLocale"/>, but returning
	/// <see cref="string.Empty"/> rather than an error when the tag is absent or not a real culture.
	/// </summary>
	/// <remarks>
	/// For read and lookup paths only. A malformed <c>?lang=</c> is treated as absent — never a 400 —
	/// so callers can substitute the configured default without branching on an error.
	/// </remarks>
	public static string NormalizeLocaleOrEmpty(string? locale)
	{
		if (string.IsNullOrWhiteSpace(locale)) return string.Empty;

		try
		{
			var culture = CultureInfo.GetCultureInfo(locale.Trim(), predefinedOnly: true);
			return culture.Name.Length == 0 ? string.Empty : culture.Name;
		}
		catch (CultureNotFoundException)
		{
			return string.Empty;
		}
	}

	/// <summary>
	/// The neutral (language-only) form of a locale tag: <c>fr-CA</c> becomes <c>fr</c>.
	/// Returns <see cref="string.Empty"/> when the tag is unusable.
	/// </summary>
	public static string NeutralLocale(string? locale)
	{
		var normalized = NormalizeLocaleOrEmpty(locale);
		return normalized.Length == 0
			? string.Empty
			: CultureInfo.GetCultureInfo(normalized).TwoLetterISOLanguageName;
	}

	/// <summary>
	/// True when two locale tags name the same language, ignoring region. Serving <c>fr</c> to an
	/// <c>fr-CA</c> reader is not a fallback and must not raise a "showing English" notice.
	/// </summary>
	public static bool SameLanguage(string? a, string? b)
	{
		var left = NeutralLocale(a);
		var right = NeutralLocale(b);
		return left.Length > 0
			&& string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
	}
}
