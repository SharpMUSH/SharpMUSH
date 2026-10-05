using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Maps a wiki page's identity to the portal path that page is canonically served from.
/// Shared by the client's link producers, the sitemap, and the redirect backstops so every
/// surface agrees on one answer.
/// </summary>
public static class WikiRoutes
{
	/// <summary>The namespace whose pages are character biographies.</summary>
	public const string CharacterNamespace = "character";

	/// <summary>
	/// True when <paramref name="ns"/> is the character namespace, whose pages the portal serves from
	/// <c>/character/{slug}</c> rather than the wiki.
	/// </summary>
	public static bool IsCharacterProfile(string? ns) =>
		string.Equals(ns?.Trim(), CharacterNamespace, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// The canonical portal path for reading a wiki page: <c>/character/{slug}</c> for character
	/// biographies, <c>/wiki/{ns}/{slug}</c> for everything else. This is what links pointing
	/// <em>at</em> a page should use.
	/// </summary>
	public static string PathFor(string? ns, string slug) =>
		IsCharacterProfile(ns)
			? $"/character/{WikiHelpers.Slugify(slug)}"
			: WikiPathFor(ns, slug);

	/// <inheritdoc cref="PathFor(string?, string)"/>
	public static string PathFor(WikiNamespace ns, string slug) => PathFor(WikiHelpers.NamespaceName(ns), slug);

	/// <summary>
	/// The wiki route a page is stored under, never the profile alias. Use this — not
	/// <see cref="PathFor(string?, string)"/> — when appending <c>/edit</c>, <c>/history</c> or <c>/diff</c>:
	/// those sub-routes exist only under <c>/wiki</c>, so building them from a
	/// <c>/character/{slug}</c> alias produces URLs that do not resolve.
	/// </summary>
	public static string WikiPathFor(string? ns, string slug)
	{
		var normalizedNs = string.IsNullOrWhiteSpace(ns) ? "main" : ns.Trim().ToLowerInvariant();
		return $"/wiki/{normalizedNs}/{WikiHelpers.Slugify(slug)}";
	}

	/// <summary>
	/// A category's page, <c>/wiki/category/{key}</c>: the page in the <see cref="WikiNamespace.Category"/>
	/// namespace, which lists the category's members.
	/// </summary>
	public static string CategoryPath(string category) =>
		$"/wiki/category/{Uri.EscapeDataString(WikiHelpers.CategoryKey(category))}";
}
