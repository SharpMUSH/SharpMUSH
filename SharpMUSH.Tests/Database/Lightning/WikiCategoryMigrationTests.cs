using System.Text.Json.Nodes;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <c>0012_wiki_categories</c>: a world whose pages were filed under one category each, with a separate tag
/// list, comes out with both merged into the page's category list, its text and history untouched.
/// </summary>
public class WikiCategoryMigrationTests : LightningDatabaseFixture
{
	private IWikiService Wiki => new WikiStoreService(Db, new WikiMarkdigPipeline());

	private static long Key(WikiPage page) => long.Parse(page.Id["wiki_page/".Length..]);

	/// <summary>Rewrites a page row as the old shape stored it: a <c>Category</c>, a <c>Tags</c> list, no <c>Categories</c>.</summary>
	private async Task FileUnderAsync(WikiPage page, string category, string[] tags, string? slug = null)
		=> await Db.Store.WriteAsync(tx =>
		{
			tx.TryGet(Tables.WikiPage, Keys.Dbref(Key(page)), out var bytes);
			var row = JsonNode.Parse(bytes)!.AsObject();
			row.Remove("Categories");
			row["Category"] = category;
			row["Tags"] = new JsonArray([.. tags.Select(tag => (JsonNode?)tag)]);
			if (slug is not null) row["Slug"] = slug;
			tx.Put(Tables.WikiPage, Keys.Dbref(Key(page)), System.Text.Encoding.UTF8.GetBytes(row.ToJsonString()));
		});

	[Test]
	public async Task CategoryAndTagsMergeIntoTheCategoryList()
	{
		var dragons = (await Wiki.CreateAsync("Dragons", "Big lizards.", "#1")).Expect<WikiPage>();
		var plain = (await Wiki.CreateAsync("Plain", "Nothing filed.", "#1")).Expect<WikiPage>();
		var clash = (await Wiki.CreateAsync("Dragons Rules", "Combat with [[Dragons]].", "#1")).Expect<WikiPage>();
		await FileUnderAsync(dragons, "lore", ["Harbour", "harbour"]);
		await FileUnderAsync(plain, "general", []);
		// The old key allowed a second "dragons" in main under another category.
		await FileUnderAsync(clash, "rules", [], slug: "dragons");
		await ForgetIndexAsync(LightningDatabase.WikiCategoriesMigrationId);

		await Db.Migrate();

		var moved = (await Wiki.GetByIdAsync(dragons.Id)).Expect<WikiPage>();
		await Assert.That(moved.Categories).IsEquivalentTo(new[] { "harbour", "lore" });
		await Assert.That(moved.MarkdownSource).IsEqualTo("Big lizards.");
		await Assert.That(moved.RevisionNumber).IsEqualTo(1);
		await Assert.That((await Wiki.GetRevisionsAsync(dragons.Id)).Count()).IsEqualTo(1);

		var untouched = (await Wiki.GetByIdAsync(plain.Id)).Expect<WikiPage>();
		await Assert.That(untouched.MarkdownSource).IsEqualTo("Nothing filed.")
			.Because("the old default category is not a category anyone chose");
		await Assert.That(untouched.RevisionNumber).IsEqualTo(1);
		await Assert.That(untouched.Categories.Count).IsEqualTo(0);

		var renamed = (await Wiki.GetByIdAsync(clash.Id)).Expect<WikiPage>();
		await Assert.That(renamed.Slug).IsEqualTo("dragons_rules")
			.Because("the first page keeps the shared slug and the later one takes its old category as a suffix");
		await Assert.That((await Wiki.GetBySlugAsync("dragons")).Expect<WikiPage>().Id).IsEqualTo(dragons.Id);
		await Assert.That(renamed.RenderedHtml).Contains("/wiki/main/dragons");

		await Assert.That((await Wiki.GetByCategoryAsync("lore")).Select(p => p.Id)).IsEquivalentTo(new[] { dragons.Id });
		await Assert.That((await Wiki.GetByCategoryAsync("rules")).Select(p => p.Id)).IsEquivalentTo(new[] { clash.Id });
	}
}
