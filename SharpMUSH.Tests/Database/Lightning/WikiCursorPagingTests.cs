using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>Cursor pages of a category and of every page (<c>LightningDatabase.WikiWindow.cs</c>).</summary>
public class WikiCursorPagingTests : LightningDatabaseFixture
{
	private async Task<WikiPage> AddAsync(string title, string[] categories, bool published = true, string author = "#1",
		string ns = "main")
	{
		var at = DateTimeOffset.UnixEpoch;
		return (await Db.CreatePageAsync(new WikiPage("", title.ToLowerInvariant().Replace(' ', '-'), title, ns, "body", "<p>body</p>",
			"body", author, author, at, at, 1)
		{ Categories = categories, Published = published })).Expect<WikiPage>();
	}

	private static WikiPageWindow Read(Result<WikiPageWindow> result) => result.Expect<WikiPageWindow>();

	/// <summary>Every page of the listing, forward by <see cref="WikiPageWindow.Next"/> and back by <see cref="WikiPageWindow.Previous"/>.</summary>
	private static async Task<(List<List<string>> Forward, List<List<string>> Backward)> WalkAsync(
		Func<WikiPageCursor, Task<Result<WikiPageWindow>>> read)
	{
		var forward = new List<List<string>>();
		var window = Read(await read(new WikiPageAt(0)));
		await Assert.That(window.Previous).IsNull();
		forward.Add([.. window.Pages.Select(p => p.Title)]);
		while (window.Next is { } next)
		{
			window = Read(await read(new WikiPageAfter(next)));
			forward.Add([.. window.Pages.Select(p => p.Title)]);
		}
		var backward = new List<List<string>> { forward[^1] };
		while (window.Previous is { } previous)
		{
			window = Read(await read(new WikiPageBefore(previous)));
			backward.Insert(0, [.. window.Pages.Select(p => p.Title)]);
		}
		return (forward, backward);
	}

	[Test]
	public async Task ACategoryPagesForwardAndBackAsAnOffsetListingDoes()
	{
		var pages = new List<WikiPage>();
		for (var i = 0; i < 53; i++)
			pages.Add(await AddAsync($"Spell {i:D2}", i % 5 == 0 ? ["Spells", "Secret"] : ["spells"], published: i % 7 != 0,
				author: i % 2 == 0 ? "#42" : "#1"));
		await AddAsync("Unrelated", ["Feats"]);
		var hidden = new WikiReadRestrictions(false, new HashSet<string>(), new HashSet<string> { "secret" }, new HashSet<string>());
		foreach (var reader in new[] { WikiVisibility.All, WikiVisibility.PublishedOnly, new WikiVisibility(false, "#42"),
			WikiVisibility.All with { Hidden = hidden } })
		{
			var expected = pages.Where(reader.Admits).Select(p => p.Title).Order(StringComparer.Ordinal).Chunk(10)
				.Select(chunk => chunk.ToList()).ToList();
			var (forward, backward) = await WalkAsync(cursor => Db.GetPagesByCategoryWindowAsync("SPELLS", cursor, 10, reader));
			await Assert.That(forward.SelectMany(p => p)).IsEquivalentTo(expected.SelectMany(p => p), TUnit.Assertions.Enums.CollectionOrdering.Matching);
			await Assert.That(forward.Count).IsEqualTo(expected.Count);
			await Assert.That(backward.Count).IsEqualTo(forward.Count);
			await Assert.That(backward.SelectMany(p => p)).IsEquivalentTo(forward.SelectMany(p => p), TUnit.Assertions.Enums.CollectionOrdering.Matching);
			for (var skip = 0; skip < 60; skip += 10)
			{
				var offset = Read(await Db.GetPagesByCategoryWindowAsync("spells", new WikiPageAt(skip), 10, reader));
				await Assert.That(offset.Pages.Select(p => p.Id))
					.IsEquivalentTo((await Db.GetPagesByCategoryAsync("spells", skip, 10, reader)).Select(p => p.Id));
			}
		}
	}

	[Test]
	public async Task EveryPagePagesForwardAndBackInNamespaceAndSlugOrder()
	{
		for (var i = 0; i < 25; i++) await AddAsync($"Page {i:D2}", [], ns: i % 3 == 0 ? "help" : "main");
		var expected = (await Db.GetPagesAsync(null, 0, 100, WikiVisibility.All)).Select(p => p.Title).ToList();
		var (forward, backward) = await WalkAsync(cursor => Db.GetPagesWindowAsync(cursor, 7, WikiVisibility.All));
		await Assert.That(forward.SelectMany(p => p)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(backward.SelectMany(p => p)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(forward.Select(p => p.Count)).IsEquivalentTo(new[] { 7, 7, 7, 4 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ANextPageReadsNothingBeforeItsToken()
	{
		var pages = new List<WikiPage>();
		for (var i = 0; i < 30; i++) pages.Add(await AddAsync($"Rule {i:D2}", ["rules"]));
		var first = Read(await Db.GetPagesByCategoryWindowAsync("rules", new WikiPageAt(0), 10, WikiVisibility.All));
		// The first page's index entries would throw if the next page's read stepped over them again.
		await Db.Store.WriteAsync(tx =>
		{
			foreach (var page in pages.Take(10))
				tx.Put(Tables.WikiByCategory, WikiCategoryKey("rules", page), "not an entry"u8);
		});
		var second = Read(await Db.GetPagesByCategoryWindowAsync("rules", new WikiPageAfter(first.Next!), 10, WikiVisibility.All));
		await Assert.That(second.Pages.Select(p => p.Title)).IsEquivalentTo(pages.Skip(10).Take(10).Select(p => p.Title),
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ATokenSurvivesItsRowsDeletionAndRename()
	{
		var pages = new List<WikiPage>();
		for (var i = 0; i < 20; i++) pages.Add(await AddAsync($"Lore {i:D2}", ["lore"]));
		var first = Read(await Db.GetPagesByCategoryWindowAsync("lore", new WikiPageAt(0), 5, WikiVisibility.All));
		await Db.DeletePageAsync(pages[4].Id);
		await Db.SetPageTitleAsync(pages[5].Id, "Lore 05 renamed");
		var second = Read(await Db.GetPagesByCategoryWindowAsync("lore", new WikiPageAfter(first.Next!), 5, WikiVisibility.All));
		await Assert.That(second.Pages.Select(p => p.Title)).IsEquivalentTo(
			new[] { "Lore 05 renamed", "Lore 06", "Lore 07", "Lore 08", "Lore 09" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ATokenCannotReachAnotherCategoryOrBeForged()
	{
		await AddAsync("Alpha", ["a"]);
		await AddAsync("Beta", ["b"]);
		await AddAsync("Gamma", ["b"]);
		var a = Read(await Db.GetPagesByCategoryWindowAsync("a", new WikiPageAt(0), 1, WikiVisibility.All));
		var b = Read(await Db.GetPagesByCategoryWindowAsync("b", new WikiPageAt(0), 1, WikiVisibility.All));
		// A token holds a position inside the category it is used with; b's first row's position read in a lists a's rows after it.
		var crossed = Read(await Db.GetPagesByCategoryWindowAsync("a", new WikiPageAfter(b.Next!), 5, WikiVisibility.All));
		await Assert.That(crossed.Pages.Select(p => p.Title).All(t => t == "Alpha")).IsTrue();
		foreach (var forged in new[] { "", "c1.", "c1.!!!", "n1.AAAA", "c1.AAAA", "x" + b.Next, new string('A', 5000) })
		{
			await Assert.That(await Db.GetPagesByCategoryWindowAsync("b", new WikiPageAfter(forged), 5, WikiVisibility.All) is Error<string>).IsTrue();
			await Assert.That(await Db.GetPagesWindowAsync(new WikiPageBefore(forged), 5, WikiVisibility.All) is Error<string>).IsTrue();
		}
		await Assert.That(a.Next).IsNull();
	}

	[Test]
	public async Task TheInMemoryStorePagesTheSameWay()
	{
		var store = new InMemoryWikiStore();
		var at = DateTimeOffset.UnixEpoch;
		for (var i = 0; i < 23; i++)
			await store.CreatePageAsync(new WikiPage("", $"p{i}", $"Item {i:D2}", "main", "", "", "", "#1", "#1", at, at, 1) { Categories = ["items"] });
		var (forward, backward) = await WalkAsync(cursor => store.GetPagesByCategoryWindowAsync("items", cursor, 5, WikiVisibility.All));
		var expected = Enumerable.Range(0, 23).Select(i => $"Item {i:D2}").ToList();
		await Assert.That(forward.SelectMany(p => p)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(backward.SelectMany(p => p)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	private static byte[] WikiCategoryKey(string category, WikiPage page)
	{
		var label = Keys.Concat(Keys.Upper(category), [0]);
		var title = System.Text.Encoding.BigEndianUnicode.GetBytes(page.Title);
		return Keys.Concat(label, title, [0, 0], Keys.Dbref(long.Parse(page.Id["wiki_page/".Length..])));
	}
}
