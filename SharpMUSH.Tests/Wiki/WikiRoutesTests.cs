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
}
