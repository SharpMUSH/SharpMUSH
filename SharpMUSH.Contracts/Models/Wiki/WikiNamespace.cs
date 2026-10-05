namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// The namespace a wiki page lives in. With its slug it is the page's identity, and it is spelled as a
/// title prefix the way MediaWiki spells one: <c>Help:Markdown Guide</c>, <c>Category:Lore</c>. A title
/// without a known prefix is in <see cref="Main"/>.
/// </summary>
public enum WikiNamespace
{
	Main,
	Help,
	Character,
	System,

	/// <summary>
	/// A category's own page. <c>Category:Lore</c> holds the category's description, and its view lists the
	/// pages and subcategories filed in <c>Lore</c>.
	/// </summary>
	Category,
}
