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
	/// True when (<paramref name="ns"/>, <paramref name="category"/>) identifies a character
	/// biography, which the portal serves from <c>/character/{slug}</c> rather than the wiki.
	/// <para>
	/// The category must be the default one: <c>/character/{name}</c> carries no category segment,
	/// so both the API alias and the bot prerenderer resolve it against <see cref="WikiHelpers.DefaultCategory"/>.
	/// A character-namespace page filed under any other category would not round-trip through that
	/// URL, so it keeps its wiki path.
	/// </para>
	/// </summary>
	public static bool IsCharacterProfile(string? ns, string? category) =>
		string.Equals(ns?.Trim(), CharacterNamespace, StringComparison.OrdinalIgnoreCase)
		&& WikiHelpers.NormalizeCategory(category) == WikiHelpers.DefaultCategory;

	/// <summary>
	/// The canonical portal path for reading a wiki page: <c>/character/{slug}</c> for character
	/// biographies, <c>/wiki/{ns}/{category}/{slug}</c> for everything else. This is what links
	/// pointing <em>at</em> a page should use.
	/// </summary>
	public static string PathFor(string? ns, string? category, string slug) =>
		IsCharacterProfile(ns, category)
			? $"/character/{WikiHelpers.Slugify(slug)}"
			: WikiPathFor(ns, category, slug);

	/// <summary>
	/// The wiki route a page is stored under, never the profile alias. Use this — not
	/// <see cref="PathFor"/> — when appending <c>/edit</c>, <c>/history</c> or <c>/diff</c>:
	/// those sub-routes exist only under <c>/wiki</c>, so building them from a
	/// <c>/character/{slug}</c> alias produces URLs that do not resolve.
	/// </summary>
	public static string WikiPathFor(string? ns, string? category, string slug)
	{
		var normalizedNs = string.IsNullOrWhiteSpace(ns) ? "main" : ns.Trim().ToLowerInvariant();
		return $"/wiki/{normalizedNs}/{WikiHelpers.NormalizeCategory(category)}/{WikiHelpers.Slugify(slug)}";
	}
}
