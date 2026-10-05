using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for <see cref="WikiRoutes"/>, the single answer to "where does this page live"
/// shared by the client link producers, the sitemap, and the redirect backstops.
/// </summary>
public class WikiRoutesTests
{
	[Test]
	[Arguments("character")]
	[Arguments("Character")]
	[Arguments(" character ")]
	public async Task IsCharacterProfile_CharacterNamespace_IsTrue(string ns)
	{
		await Assert.That(WikiRoutes.IsCharacterProfile(ns)).IsTrue();
	}

	[Test]
	[Arguments("main")]
	[Arguments("help")]
	[Arguments("system")]
	[Arguments("category")]
	[Arguments(null)]
	public async Task IsCharacterProfile_OtherNamespaces_IsFalse(string? ns)
	{
		await Assert.That(WikiRoutes.IsCharacterProfile(ns)).IsFalse();
	}

	[Test]
	public async Task PathFor_CharacterProfile_UsesCharacterAlias()
	{
		await Assert.That(WikiRoutes.PathFor("character", "mercutio"))
			.IsEqualTo("/character/mercutio");
	}

	[Test]
	public async Task PathFor_OrdinaryPage_UsesWikiPath()
	{
		await Assert.That(WikiRoutes.PathFor("help", "markdown_guide"))
			.IsEqualTo("/wiki/help/markdown_guide");
	}

	[Test]
	public async Task PathFor_NormalizesNamespaceCase()
	{
		await Assert.That(WikiRoutes.PathFor("Help", "markdown_guide"))
			.IsEqualTo("/wiki/help/markdown_guide");
	}

	[Test]
	public async Task PathFor_Enum_MatchesString()
	{
		await Assert.That(WikiRoutes.PathFor(WikiNamespace.Help, "markdown_guide"))
			.IsEqualTo(WikiRoutes.PathFor("help", "markdown_guide"));
	}

	/// <summary>Display names reach the same path as the stored slug, per <c>WikiHelpers.Slugify</c>.</summary>
	[Test]
	public async Task PathFor_SlugifiesDisplayNames()
	{
		await Assert.That(WikiRoutes.PathFor("character", "Mannaz Byron"))
			.IsEqualTo("/character/mannaz_byron");
	}

	/// <summary>
	/// A blank namespace must fall back to main like a null one does. Wiki markup can supply an
	/// empty prefix, and an empty segment would build <c>/wiki//x</c>.
	/// </summary>
	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	public async Task PathFor_BlankNamespace_FallsBackToMain(string? ns)
	{
		await Assert.That(WikiRoutes.PathFor(ns, "page_name"))
			.IsEqualTo("/wiki/main/page_name");
	}

	// --- WikiPathFor: the storage route, for tooling links ------------------------------
	// /edit, /history and /diff hang off the wiki route and have no alias equivalent, so
	// anything appending them must not start from the profile alias.

	[Test]
	public async Task WikiPathFor_CharacterProfile_KeepsWikiRoute()
	{
		await Assert.That(WikiRoutes.WikiPathFor("character", "mercutio"))
			.IsEqualTo("/wiki/character/mercutio");
	}

	[Test]
	public async Task WikiPathFor_OrdinaryPage_MatchesPathFor()
	{
		await Assert.That(WikiRoutes.WikiPathFor("help", "markdown_guide"))
			.IsEqualTo(WikiRoutes.PathFor("help", "markdown_guide"));
	}

	/// <summary>Sub-routes appended to the tooling path must land on real routes.</summary>
	[Test]
	[Arguments("edit")]
	[Arguments("history")]
	[Arguments("diff")]
	public async Task WikiPathFor_CharacterProfile_SupportsSubRoutes(string subRoute)
	{
		await Assert.That($"{WikiRoutes.WikiPathFor("character", "mercutio")}/{subRoute}")
			.IsEqualTo($"/wiki/character/mercutio/{subRoute}");
	}

	// --- CategoryPath: a category's own page -----------------------------------------

	[Test]
	[Arguments("Help", "/wiki/category/help")]
	[Arguments("Places of Note", "/wiki/category/places_of_note")]
	[Arguments("places_of_note", "/wiki/category/places_of_note")]
	public async Task CategoryPath_UsesCategoryKey(string category, string expected)
	{
		await Assert.That(WikiRoutes.CategoryPath(category)).IsEqualTo(expected);
	}

	/// <summary>A category's page is an ordinary page in the category namespace, so both routes agree.</summary>
	[Test]
	public async Task CategoryPath_MatchesCategoryNamespacePage()
	{
		await Assert.That(WikiRoutes.CategoryPath("Lore"))
			.IsEqualTo(WikiRoutes.PathFor(WikiNamespace.Category, "lore"));
	}
}
