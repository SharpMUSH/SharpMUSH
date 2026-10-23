using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>Facts the schema refuses are an error, and change neither the page nor the fact index.</summary>
public class WikiPageFactsTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private async Task<WikiPage> AddAsync(string slug)
		=> (await Wiki.CreatePageAsync(new WikiPage("", slug, slug, "main", "md", "<p>md</p>", "md", "#1", "#1", T0, T0, 1))).Expect<WikiPage>();

	private async Task<List<string>> LevelAsync(long level)
		=> [.. (await Wiki.QueryFactsAsync(new WikiFactQuery([new WikiFactEquals("pf2e:level", level.ToString(System.Globalization.CultureInfo.InvariantCulture))], WikiFactQuery.MaxTake), WikiVisibility.All))
			.Expect<WikiFactQueryPage>().Pages.Select(p => p.Id)];

	[Test]
	[Arguments("pf2e:unknown", "1", "No property pf2e:unknown")]
	[Arguments("pf2e:level", "three", "pf2e:level is a whole number")]
	[Arguments("pf2e:level", "6", "takes one value")]
	[Arguments("pf2e:tradition", "elemental", "pf2e:tradition does not allow")]
	[Arguments("pf2e:class", "wiki_page/987654", "names a page that does not exist")]
	[Arguments("not a key", "1", "is not a property key")]
	public async Task FactsTheSchemaRefusesAreAnErrorAndChangeNothing(string property, string value, string message)
	{
		while (!(await Wiki.BuildFactIndexBatchAsync(256)).Ready) { }
		var spell = await AddAsync($"refused-{Guid.NewGuid():N}");
		await Assert.That((await Wiki.SetPageFactsAsync(spell.Id, [new("pf2e:level", "5")])).Value is WikiPage).IsTrue();

		var refused = await Wiki.SetPageFactsAsync(spell.Id, [new("pf2e:level", "5"), new(property, value)]);
		await Assert.That(refused.Value is Error<string> { Value: var error } && error.Contains(message)).IsTrue().Because($"got {refused.Value}");
		await Assert.That((await Wiki.GetPageByIdAsync(spell.Id)).Expect<WikiPage>().Facts).IsEquivalentTo([new WikiPageFact("pf2e:level", "5")]);
		await Assert.That(await LevelAsync(5)).IsEquivalentTo([spell.Id]);
		await Assert.That(await LevelAsync(6)).IsEmpty();
	}
}
