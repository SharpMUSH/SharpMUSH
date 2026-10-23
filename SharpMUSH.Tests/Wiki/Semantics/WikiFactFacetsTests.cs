using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Wiki.Semantics;
using SharpMUSH.Tests.Database.Lightning;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>A category's facets: the values its pages state, how many state each, and how chosen values narrow them.</summary>
public class WikiFactFacetsTests : LightningDatabaseFixture
{
	private static readonly PropertyDefinition Tradition =
		new("facet:tradition", WikiFactProperties.SchemaVersion, SemanticType.Keyword, Cardinality.Multiple, [], "Tradition");

	private static readonly PropertyDefinition Level =
		new("facet:level", WikiFactProperties.SchemaVersion, SemanticType.Integer, Cardinality.Single, [], "Level");

	private static readonly PropertyDefinition Class =
		new("facet:class", WikiFactProperties.SchemaVersion, SemanticType.PageRef, Cardinality.Multiple, []);

	private async Task<WikiStoreService> WikiAsync()
	{
		var wiki = new WikiStoreService(Db, new WikiMarkdigPipeline());
		foreach (var property in new[] { Tradition, Level, Class })
			_ = (await wiki.SetFactPropertyAsync(property)).Expect<PropertyDefinition>();
		return wiki;
	}

	private static async Task<WikiPage> PageAsync(WikiStoreService wiki, string title, string[] categories, bool published,
		params WikiPageFact[] facts)
	{
		var page = (await wiki.CreateAsync(title, "Body.", "#1")).Expect<WikiPage>();
		_ = (await wiki.SetMetadataAsync(page.Id, categories, published)).Expect<WikiPage>();
		return (await wiki.SetFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	private static string Shown(WikiFacets facets)
		=> string.Join("; ", facets.Facets.Select(f => $"{f.Label}: {string.Join(", ", f.Values.Select(v => $"{v.Value}={v.Label}({v.Count})"))}"
			+ (f.More > 0 ? $" +{f.More}" : string.Empty)));

	[Test]
	public async Task EachValueIsCountedAndChosenValuesNarrowTheOtherProperties()
	{
		var wiki = await WikiAsync();
		var wizard = await PageAsync(wiki, "Wizard", [], true);
		var secret = await PageAsync(wiki, "Secret class", [], false);
		await PageAsync(wiki, "Fireball", ["Spells"], true, new WikiPageFact(Tradition.Id, "arcane"), new WikiPageFact(Tradition.Id, "primal"), new WikiPageFact(Level.Id, "3"),
			new WikiPageFact(Class.Id, wizard.Id));
		await PageAsync(wiki, "Shield", ["Spells"], true, new WikiPageFact(Tradition.Id, "arcane"), new WikiPageFact(Tradition.Id, "divine"), new WikiPageFact(Level.Id, "1"),
			new WikiPageFact(Class.Id, wizard.Id), new WikiPageFact(Class.Id, secret.Id));
		await PageAsync(wiki, "Heal", ["Spells"], true, new WikiPageFact(Tradition.Id, "divine"), new WikiPageFact(Level.Id, "1"));
		await PageAsync(wiki, "Draft spell", ["Spells"], false, new WikiPageFact(Tradition.Id, "occult"));
		await PageAsync(wiki, "Sword", ["Gear"], true, new WikiPageFact(Tradition.Id, "martial"));

		var all = (await WikiFactFacets.CountAsync(wiki, "Spells", [], WikiVisibility.PublishedOnly)).Expect<WikiFacets>();
		await Assert.That(all.Pages).IsEqualTo(3);
		// By property id; the most stated first; a page the reader may not see is no value; a page value is typed as namespace:slug.
		await Assert.That(Shown(all)).IsEqualTo(
			"facet:class: main:wizard=Wizard(2); Level: 1=1(2), 3=3(1); Tradition: arcane=arcane(2), divine=divine(2), primal=primal(1)");

		var arcane = (await WikiFactFacets.CountAsync(wiki, "Spells", ["facet:tradition=arcane"], WikiVisibility.PublishedOnly)).Expect<WikiFacets>();
		await Assert.That(arcane.Pages).IsEqualTo(2);
		// Tradition keeps the values that could be chosen instead; the others count only the arcane spells.
		await Assert.That(Shown(arcane)).IsEqualTo(
			"facet:class: main:wizard=Wizard(2); Level: 1=1(1), 3=3(1); Tradition: arcane=arcane(2), divine=divine(2), primal=primal(1)");

		// Each property counts the pages meeting the other one's condition.
		var both = (await WikiFactFacets.CountAsync(wiki, "Spells", ["facet:tradition=arcane", "facet:level=1"], WikiVisibility.PublishedOnly))
			.Expect<WikiFacets>();
		await Assert.That(both.Pages).IsEqualTo(1);
		await Assert.That(Shown(both)).IsEqualTo(
			"facet:class: main:wizard=Wizard(1); Level: 1=1(1), 3=3(1); Tradition: divine=divine(2), arcane=arcane(1)");
	}

	[Test]
	public async Task ManyWholeNumbersAreCountedInRangesAndManyValuesAreCut()
	{
		var wiki = await WikiAsync();
		for (var level = 1; level <= 20; level++)
			await PageAsync(wiki, $"Spell {level}", ["Many"], true, new WikiPageFact(Level.Id, $"{level}"), new WikiPageFact(Tradition.Id, $"t{level:00}"));

		var facets = (await WikiFactFacets.CountAsync(wiki, "Many", [], WikiVisibility.PublishedOnly)).Expect<WikiFacets>();
		await Assert.That(Shown(facets)).IsEqualTo(
			"Level: 1..4=1 to 4(4), 5..8=5 to 8(4), 9..12=9 to 12(4), 13..16=13 to 16(4), 17..20=17 to 20(4); "
			+ "Tradition: t01=t01(1), t02=t02(1), t03=t03(1), t04=t04(1), t05=t05(1), t06=t06(1), t07=t07(1), t08=t08(1), t09=t09(1), "
			+ "t10=t10(1), t11=t11(1), t12=t12(1) +8");

		// A range is a condition as written.
		var narrowed = (await WikiFactFacets.CountAsync(wiki, "Many", ["facet:level=5..8"], WikiVisibility.PublishedOnly)).Expect<WikiFacets>();
		await Assert.That(narrowed.Pages).IsEqualTo(4);
	}

	[Test]
	[Arguments("category=Spells", "Facets take fact conditions only, like 'pf2e:level=1..3'.")]
	[Arguments("show=facet:level", "Facets take fact conditions only, like 'pf2e:level=1..3'.")]
	[Arguments("facet:nope=1", "No property facet:nope is defined.")]
	[Arguments("facet:level=high", "facet:level is a whole number, not 'high'.")]
	public async Task AConditionTheWikiCannotAnswerIsRefused(string condition, string error)
	{
		var wiki = await WikiAsync();
		await Assert.That((await WikiFactFacets.CountAsync(wiki, "Spells", [condition], WikiVisibility.PublishedOnly)).Expect<Error<string>>().Value)
			.IsEqualTo(error);
	}
}
