using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for <see cref="WikiRoutes"/>, the single answer to "where does this page live"
/// shared by the client link producers, the sitemap, and the redirect backstops.
/// </summary>
public class WikiRoutesTests
{
	[Test]
	[Arguments("character", "general")]
	[Arguments("Character", "general")]
	[Arguments("character", null)]
	[Arguments("character", "")]
	public async Task IsCharacterProfile_CharacterNamespaceInDefaultCategory_IsTrue(string ns, string? category)
	{
		await Assert.That(WikiRoutes.IsCharacterProfile(ns, category)).IsTrue();
	}

	/// <summary>
	/// /character/{name} carries no category segment, so a character page filed under a
	/// non-default category cannot round-trip through that URL and keeps its wiki path.
	/// </summary>
	[Test]
	public async Task IsCharacterProfile_CharacterNamespaceInOtherCategory_IsFalse()
	{
		await Assert.That(WikiRoutes.IsCharacterProfile("character", "npcs")).IsFalse();
	}

	[Test]
	[Arguments("main")]
	[Arguments("help")]
	[Arguments("system")]
	public async Task IsCharacterProfile_OtherNamespaces_IsFalse(string ns)
	{
		await Assert.That(WikiRoutes.IsCharacterProfile(ns, "general")).IsFalse();
	}

	[Test]
	public async Task PathFor_CharacterProfile_UsesCharacterAlias()
	{
		await Assert.That(WikiRoutes.PathFor("character", "general", "mercutio"))
			.IsEqualTo("/character/mercutio");
	}

	[Test]
	public async Task PathFor_CharacterInOtherCategory_KeepsWikiPath()
	{
		await Assert.That(WikiRoutes.PathFor("character", "npcs", "mercutio"))
			.IsEqualTo("/wiki/character/npcs/mercutio");
	}

	[Test]
	public async Task PathFor_OrdinaryPage_UsesWikiPath()
	{
		await Assert.That(WikiRoutes.PathFor("help", "general", "markdown_guide"))
			.IsEqualTo("/wiki/help/general/markdown_guide");
	}

	[Test]
	public async Task PathFor_NormalizesNamespaceCaseAndMissingCategory()
	{
		await Assert.That(WikiRoutes.PathFor("Help", null, "markdown_guide"))
			.IsEqualTo("/wiki/help/general/markdown_guide");
	}

	/// <summary>Display names reach the same path as the stored slug, per <c>WikiHelpers.Slugify</c>.</summary>
	[Test]
	public async Task PathFor_SlugifiesDisplayNames()
	{
		await Assert.That(WikiRoutes.PathFor("character", "general", "Mannaz Byron"))
			.IsEqualTo("/character/mannaz_byron");
	}

	/// <summary>
	/// A blank namespace must fall back to main like a null one does. Wiki markup can supply an
	/// empty prefix (<c>[[ :Page]]</c>), and an empty segment would build <c>/wiki//general/x</c>.
	/// </summary>
	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	public async Task PathFor_BlankNamespace_FallsBackToMain(string? ns)
	{
		await Assert.That(WikiRoutes.PathFor(ns, "general", "page_name"))
			.IsEqualTo("/wiki/main/general/page_name");
	}

	// --- WikiPathFor: the storage route, for tooling links ------------------------------
	// /edit, /history and /diff hang off the wiki route and have no alias equivalent, so
	// anything appending them must not start from the profile alias.

	[Test]
	public async Task WikiPathFor_CharacterProfile_KeepsWikiRoute()
	{
		await Assert.That(WikiRoutes.WikiPathFor("character", "general", "mercutio"))
			.IsEqualTo("/wiki/character/general/mercutio");
	}

	[Test]
	public async Task WikiPathFor_OrdinaryPage_MatchesPathFor()
	{
		await Assert.That(WikiRoutes.WikiPathFor("help", "general", "markdown_guide"))
			.IsEqualTo(WikiRoutes.PathFor("help", "general", "markdown_guide"));
	}

	/// <summary>Sub-routes appended to the tooling path must land on real routes.</summary>
	[Test]
	[Arguments("edit")]
	[Arguments("history")]
	[Arguments("diff")]
	public async Task WikiPathFor_CharacterProfile_SupportsSubRoutes(string subRoute)
	{
		await Assert.That($"{WikiRoutes.WikiPathFor("character", "general", "mercutio")}/{subRoute}")
			.IsEqualTo($"/wiki/character/general/mercutio/{subRoute}");
	}
}
