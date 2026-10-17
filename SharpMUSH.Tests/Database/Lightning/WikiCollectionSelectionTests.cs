using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCollectionSelectionTests : LightningDatabaseFixture
{
	[Test]
	public async Task CollectionSelectionAndSearchApplyBeforePagingAndCounts()
	{
		var wiki = new WikiStoreService(Db, new WikiMarkdigPipeline());
		var game = (await wiki.CreateAsync("Game", "game", "#1", categories: ["Rules"])).Expect<WikiPage>();
		var first = (await wiki.CreateAsync("A Imported", "rules", "#1", categories: ["Rules"])).Expect<WikiPage>();
		var last = (await wiki.CreateAsync("Z Imported", "rules", "#1", categories: ["Rules"])).Expect<WikiPage>();
		var draft = (await wiki.CreateAsync("Hidden Imported", "rules", "#2", categories: ["Secret"])).Expect<WikiPage>();
		await wiki.SetMetadataAsync(draft.Id, draft.Categories, false);
		var selected = WikiVisibility.PublishedOnly with { IncludedPageIds = new HashSet<string> { first.Id, last.Id, draft.Id } };
		await Assert.That(await wiki.CountPagesAsync(null, selected)).IsEqualTo(2);
		await Assert.That((await wiki.GetAllPagesAsync(1, 1, visibility: selected)).Single().Id).IsEqualTo(last.Id);
		await Assert.That((await wiki.CountPagesByCategoryAsync(selected)).Keys).IsEquivalentTo(new[] { "rules" });
		await Assert.That(await wiki.CountPagesAsync(null, selected with { Search = "Z Imported" })).IsEqualTo(1);
		await Assert.That(await wiki.CountPagesAsync(null, WikiVisibility.All with { ExcludedPageIds = selected.IncludedPageIds })).IsEqualTo(1);
		await ReopenAsync();
		await Assert.That(await Db.CountPagesAsync(null, selected)).IsEqualTo(2);
	}

	[Test]
	public async Task CollectionSelectionNeverBypassesPageRequirements()
	{
		var wiki = new WikiStoreService(Db, new WikiMarkdigPipeline());
		var page = (await wiki.CreateAsync("Protected", "x", "#1", categories: ["Rules"])).Expect<WikiPage>();
		var visibility = WikiVisibility.All with
		{
			IncludedPageIds = new HashSet<string> { page.Id },
			Hidden = new WikiReadRestrictions(false, new HashSet<string>(), new HashSet<string>(), new HashSet<string> { page.Id })
		};
		await Assert.That(await wiki.CountPagesAsync(null, visibility)).IsEqualTo(0);
		await Assert.That(await wiki.GetAllPagesAsync(visibility: visibility)).IsEmpty();
		await Assert.That(await wiki.CountPagesByCategoryAsync(visibility)).IsEmpty();
	}
}
