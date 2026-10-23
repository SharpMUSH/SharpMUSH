using SharpMUSH.Library.Services.Interfaces;
using NSubstitute;
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

	[Test]
	public async Task NamersPastAReadOfOnlyHiddenPagesAreStillListed()
	{
		var store = new WikiStoreService(Db, new WikiMarkdigPipeline());
		var target = (await store.CreateAsync("Named target", "Body.", "#1")).Expect<WikiPage>();
		var namer = (await store.CreateAsync("Late namer", "Body.", "#1")).Expect<WikiPage>();
		var schema = new SemanticSchema(1, [new PropertyDefinition("test:mentor", 1, SemanticType.PageRef, Cardinality.Single, [])]);
		// The index's first read stopped at its candidate limit having found only pages this reader may not see.
		var wiki = Substitute.For<IWikiService>();
		wiki.QueryFactsAsync(Arg.Is<WikiFactQuery>(q => q.After == null), Arg.Any<WikiVisibility>())
			.Returns(new WikiFactQueryResult(new WikiFactQueryPage([], "resume")));
		wiki.QueryFactsAsync(Arg.Is<WikiFactQuery>(q => q.After == "resume"), Arg.Any<WikiVisibility>())
			.Returns(new WikiFactQueryResult(new WikiFactQueryPage([namer], null)));

		var named = await WikiFactEntry.NamedByAsync(wiki, schema, target, WikiVisibility.PublishedOnly, 20);

		await Assert.That(named).Count().IsEqualTo(1);
		await Assert.That(named[0].Pages.Select(p => p.Id)).IsEquivalentTo(new[] { namer.Id });
		await Assert.That(named[0].More).IsFalse();
	}
}
