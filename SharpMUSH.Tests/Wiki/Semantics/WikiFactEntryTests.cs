using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Wiki.Semantics;
using SharpMUSH.Tests.Database.Lightning;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>Facts as a reader is shown them: a value naming a page the reader may not see is left out.</summary>
public class WikiFactEntryTests : LightningDatabaseFixture
{
	[Test]
	public async Task APageIdKeptFromARemovedPagePropertyIsShownOnlyToWhoMaySeeThePage()
	{
		var wiki = new WikiStoreService(Db, new WikiMarkdigPipeline());
		var hidden = (await wiki.CreateAsync("Hidden target", "Body.", "#1")).Expect<WikiPage>();
		hidden = (await wiki.SetMetadataAsync(hidden.Id, [], false)).Expect<WikiPage>();
		var shown = (await wiki.CreateAsync("Shown target", "Body.", "#1")).Expect<WikiPage>();
		// No property is defined: the page ids are what a removed page property left on the page.
		var facts = new WikiPageFact[] { new("game:patron", hidden.Id), new("game:patron", shown.Id), new("game:note", "plain") };
		var schema = await wiki.GetFactSchemaAsync();

		await Assert.That(await WikiFactEntry.ShownAsync(wiki, schema, facts, WikiVisibility.PublishedOnly, page => page.Title))
			.IsEquivalentTo([new WikiPageFact("game:patron", "Shown target"), new WikiPageFact("game:note", "plain")]);
		// A reader who saves what they were shown keeps the one they were not.
		await Assert.That(await WikiFactEntry.KeepingUnseenAsync(wiki, schema, facts, [new WikiPageFact("game:note", "plain")], WikiVisibility.PublishedOnly))
			.IsEquivalentTo([new WikiPageFact("game:note", "plain"), new WikiPageFact("game:patron", hidden.Id)]);
	}
}
