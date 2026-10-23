using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The properties a world defines for its pages' facts: stored in the world, checked before they are written, and
/// changed or removed without touching the pages that state them.
/// </summary>
public class WikiPropertyStoreTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private static PropertyDefinition Property(string id, SemanticType type, Cardinality cardinality = Cardinality.Single,
		string[]? keywords = null, string label = "", string description = "")
		=> new(id, WikiFactProperties.SchemaVersion, type, cardinality, [.. (keywords ?? []).Select(k => new Keyword(k))], label, description);

	private async Task<WikiPage> AddAsync(string slug, params WikiPageFact[] facts)
	{
		var page = (await Wiki.CreatePageAsync(new WikiPage("", slug, slug, "main", "md", "<p>md</p>", "md", "#1", "#1", T0, T0, 1)))
			.Expect<WikiPage>();
		return facts.Length == 0 ? page : (await Wiki.SetPageFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	private async Task BuildAsync()
	{
		while (!(await Wiki.BuildFactIndexBatchAsync(256)).Ready) { }
	}

	private async Task<List<string>> QueryAsync(WikiFactCondition condition)
		=> [.. (await Wiki.QueryFactsAsync(new WikiFactQuery([condition], 50), WikiVisibility.All)).Expect<WikiFactQueryPage>().Pages.Select(p => p.Id)];

	[Test]
	public async Task ANewWorldDefinesNoProperties()
		=> await Assert.That((await Wiki.GetFactSchemaAsync()).Properties).IsEmpty();

	[Test]
	public async Task APropertyIsStoredAsItWasCheckedAndKeptAcrossAReopen()
	{
		var stored = (await Wiki.SetFactPropertyAsync(Property("faction:rank", SemanticType.Keyword, Cardinality.Multiple,
			[" Captain ", "SERGEANT"], " Rank ", "Where a member stands."))).Expect<PropertyDefinition>();
		await Assert.That(stored.AllowedKeywords.Select(k => k.Value)).IsEquivalentTo(["captain", "sergeant"]);
		await Assert.That(stored.Label).IsEqualTo("Rank");
		await ReopenAsync();

		var read = (await Wiki.GetFactSchemaAsync()).Properties.Single();
		await Assert.That(read with { AllowedKeywords = [] }).IsEqualTo(stored with { AllowedKeywords = [] });
		await Assert.That(read.AllowedKeywords).IsEquivalentTo(stored.AllowedKeywords);
	}

	[Test]
	[Arguments("level", SemanticType.Integer, new string[0], "'level' is not a property key like 'game:level'.")]
	[Arguments("game:level", SemanticType.Integer, new[] { "high" }, "game:level is number; only a keyword property lists its values.")]
	[Arguments("game:size", SemanticType.Keyword, new[] { "Big", "big" }, "game:size lists 'big' twice.")]
	public async Task ADefinitionTheWikiCannotTakeIsRefused(string id, SemanticType type, string[] keywords, string error)
	{
		await Assert.That((await Wiki.SetFactPropertyAsync(Property(id, type, keywords: keywords))).Expect<Error<string>>().Value).IsEqualTo(error);
		await Assert.That((await Wiki.GetFactSchemaAsync()).Properties).IsEmpty();
	}

	[Test]
	public async Task ARemovedPropertysFactsStayAndAreFoundWhenItIsDefinedAgain()
	{
		await Wiki.SetFactPropertyAsync(Property("game:level", SemanticType.Integer));
		await Wiki.SetFactPropertyAsync(Property("game:school", SemanticType.Keyword));
		var page = await AddAsync("fireball", new("game:level", "3"), new("game:school", "evocation"));
		await BuildAsync();

		(await Wiki.DeleteFactPropertyAsync("game:level")).Expect<None>();
		await Assert.That((await Wiki.DeleteFactPropertyAsync("game:level")).Expect<NotFound>()).IsEqualTo(new NotFound());
		// The page keeps the fact, may change its other facts, and may not state the removed property anew.
		await Assert.That((await Wiki.SetPageFactsAsync(page.Id, [new("game:level", "3"), new("game:school", "fire")])).Expect<WikiPage>().Facts)
			.IsEquivalentTo([new WikiPageFact("game:level", "3"), new WikiPageFact("game:school", "fire")]);
		await Assert.That((await Wiki.SetPageFactsAsync(page.Id, [new("game:level", "4"), new("game:school", "fire")])).Expect<Error<string>>().Value)
			.IsEqualTo("No property game:level is defined.");
		await Assert.That((await Wiki.QueryFactsAsync(new WikiFactQuery([new WikiFactEquals("game:level", "3")], 50), WikiVisibility.All))
			.Expect<Error<string>>().Value).Contains("game:level");

		await Wiki.SetFactPropertyAsync(Property("game:level", SemanticType.Integer));
		await Assert.That(await QueryAsync(new WikiFactRange("game:level", 1, 5))).IsEquivalentTo([page.Id]);
	}

	[Test]
	public async Task ChangingATypeMovesItsEntriesBetweenNumberAndTextOrder()
	{
		await Wiki.SetFactPropertyAsync(Property("game:tier", SemanticType.Keyword));
		var low = await AddAsync("low", new WikiPageFact("game:tier", "2"));
		var high = await AddAsync("high", new WikiPageFact("game:tier", "10"));
		var named = await AddAsync("named", new WikiPageFact("game:tier", "mythic"));
		await BuildAsync();

		await Wiki.SetFactPropertyAsync(Property("game:tier", SemanticType.Integer));
		await Assert.That(await QueryAsync(new WikiFactRange("game:tier", 1, 9))).IsEquivalentTo([low.Id]);
		await Assert.That(await QueryAsync(new WikiFactRange("game:tier", null, 1000)))
			.IsEquivalentTo([low.Id, high.Id], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		// A value the new type does not take stays on its page, and the page may keep it until someone changes it.
		await Assert.That((await Wiki.SetPageFactsAsync(named.Id, [new("game:tier", "mythic")])).Expect<WikiPage>().Facts.Single().Value)
			.IsEqualTo("mythic");

		await Wiki.SetFactPropertyAsync(Property("game:tier", SemanticType.Keyword));
		await Assert.That(await QueryAsync(new WikiFactEquals("game:tier", "10"))).IsEquivalentTo([high.Id]);
		await Assert.That(await QueryAsync(new WikiFactEquals("game:tier", "mythic"))).IsEquivalentTo([named.Id]);
	}
}
