using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
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

	/// <summary>The lower-case spelling of a namespace, as stored and routed.</summary>
	public static string NamespaceName(WikiNamespace ns) => ns.ToString().ToLowerInvariant();

	/// <summary>The namespace a stored or routed spelling names, or null when it names none.</summary>
	public static WikiNamespace? ParseNamespace(string? ns) =>
		Enum.TryParse<WikiNamespace>(ns?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
			? parsed
			: null;

	/// <summary>
	/// Splits a page title as typed in a link or a command — <c>Getting Started</c>,
	/// <c>Help:Getting Started</c>, <c>Category:Lore</c> — into its namespace and the title within it.
	/// Only a known namespace counts as a prefix; any other colon is part of a Main-namespace title, as in
	/// MediaWiki.
	/// </summary>
	public static (WikiNamespace Namespace, string Title) SplitTitle(string title)
	{
		var trimmed = title.Trim();
		var colon = trimmed.IndexOf(':');
		return colon > 0 && !int.TryParse(trimmed[..colon], out _) && ParseNamespace(trimmed[..colon]) is { } ns
			? (ns, trimmed[(colon + 1)..].Trim())
			: (WikiNamespace.Main, trimmed);
	}

	/// <summary>The (namespace, slug) identity a typed title names; see <see cref="SplitTitle"/>.</summary>
	public static (WikiNamespace Namespace, string Slug) ResolveTitle(string title)
	{
		var (ns, inner) = SplitTitle(title);
		return (ns, Slugify(inner));
	}

	/// <summary>
	/// Returns the normalised string key for the slug index: "{namespace}:{slug}". A page is identified
	/// by its namespace and slug alone, as in MediaWiki; categories are labels the page carries.
	/// </summary>
	public static string SlugKey(string nsStr, string slug) =>
		$"{nsStr.ToLowerInvariant()}:{slug}";

	/// <summary>
	/// The key a category is stored and routed under: its name slugified, so <c>Places of Note</c>,
	/// <c>places of note</c> and <c>places_of_note</c> are one category. Empty when the name is blank.
	/// </summary>
	public static string CategoryKey(string? category) =>
		string.IsNullOrWhiteSpace(category) ? string.Empty : Slugify(category.Trim());

	/// <summary>
	/// Normalises a page's category list for storage: each name keyed by <see cref="CategoryKey"/>,
	/// blanks removed, de-duplicated, sorted for stable comparisons.
	/// </summary>
	public static IReadOnlyList<string> NormalizeCategories(IEnumerable<string>? categories) =>
		categories is null
			? []
			: categories
				.Select(CategoryKey)
				.Where(c => c.Length > 0)
				.Distinct()
				.OrderBy(c => c, StringComparer.Ordinal)
				.ToList();

	/// <summary>
	/// The categories every page in <paramref name="ns"/> is in, whatever its own list says: a biography in
	/// the <c>Character</c> namespace is in the <c>character</c> category. The store adds them on every write, so
	/// they cannot be taken off; an editor shows them as fixed.
	/// </summary>
	public static IReadOnlyList<string> NamespaceCategories(WikiNamespace? ns) =>
		ns is WikiNamespace.Character ? [CharacterCategory] : [];

	/// <inheritdoc cref="NamespaceCategories(WikiNamespace?)"/>
	public static IReadOnlyList<string> NamespaceCategories(string? ns) => NamespaceCategories(ParseNamespace(ns));

	/// <summary>The category every character biography is in.</summary>
	public const string CharacterCategory = "character";

	/// <summary>
	/// <see cref="NormalizeCategories(IEnumerable{string}?)"/> with the categories <paramref name="ns"/> puts every
	/// page in (<see cref="NamespaceCategories(WikiNamespace?)"/>).
	/// </summary>
	public static IReadOnlyList<string> NormalizeCategories(IEnumerable<string>? categories, WikiNamespace? ns) =>
		NormalizeCategories((categories ?? []).Concat(NamespaceCategories(ns)));

	/// <summary>
	/// The display name of a category key: underscores become spaces and the first letter is
	/// upper-cased, the way MediaWiki shows a title.
	/// </summary>
	public static string CategoryLabel(string category)
	{
		var key = CategoryKey(category);
		return key.Length == 0 ? string.Empty : char.ToUpperInvariant(key[0]) + key[1..].Replace('_', ' ');
	}

	/// <summary>
	/// The display name of a category in the reader's language: the title of its page in the category
	/// namespace, translated where that page is, from <paramref name="names"/> (keyed by category key);
	/// <see cref="CategoryLabel(string)"/> when the category has no page.
	/// </summary>
	public static string CategoryLabel(string category, IReadOnlyDictionary<string, string>? names) =>
		names is not null && names.TryGetValue(CategoryKey(category), out var name) && name.Length > 0
			? name
			: CategoryLabel(category);

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
	public static Result<string> NormalizeLocale(string? locale)
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
