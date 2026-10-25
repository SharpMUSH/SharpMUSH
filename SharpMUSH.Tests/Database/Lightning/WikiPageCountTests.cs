using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The kept page counts (<c>LightningDatabase.WikiCounts.cs</c>) against a reference computed from the pages themselves,
/// for every kind of reader, through writes, and from the migration that builds them.
/// </summary>
public class WikiPageCountTests : LightningDatabaseFixture
{
	private static readonly string[] Namespaces = ["main", "help", "system", "category"];
	private static readonly string[] Categories = ["Rules", "rules", "Spells", "Lore", "Secret", "Feats", "Ancestry"];
	private static readonly string[] Authors = ["#1", "#42", "#43"];

	private async Task<List<WikiPage>> SeedAsync(int count, int seed)
	{
		var random = new Random(seed);
		var at = DateTimeOffset.UnixEpoch;
		var pages = new List<WikiPage>();
		for (var i = 0; i < count; i++)
		{
			var author = Authors[random.Next(Authors.Length)];
			var categories = Enumerable.Range(0, random.Next(4)).Select(_ => Categories[random.Next(Categories.Length)]).ToArray();
			pages.Add((await Db.CreatePageAsync(new WikiPage("", $"page-{i}", $"Page {i:D4}", Namespaces[random.Next(Namespaces.Length)],
				"body", "<p>body</p>", "body", author, author, at, at, 1)
			{ Categories = categories, Published = random.Next(3) > 0 })).Expect<WikiPage>());
		}
		return pages;
	}

	private static IEnumerable<WikiVisibility> Readers(IReadOnlyList<WikiPage> pages)
	{
		var hidden = new WikiReadRestrictions(false,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HELP" },
			new HashSet<string>(StringComparer.Ordinal) { "secret" },
			new HashSet<string>(StringComparer.Ordinal) { pages[0].Id, pages[3].Id, "17", "wiki_page/999999" });
		var collection = pages.Where((_, i) => i % 3 == 0).Select(p => p.Id).Append("bare").Append(pages[6].Id["wiki_page/".Length..])
			.ToHashSet(StringComparer.Ordinal);
		var bases = new[] { WikiVisibility.All, WikiVisibility.PublishedOnly, new WikiVisibility(false, "#42"), new WikiVisibility(true, "#42") };
		foreach (var reader in bases)
		{
			yield return reader;
			yield return reader with { Hidden = hidden };
			yield return reader with { Hidden = WikiReadRestrictions.All };
			yield return reader with { IncludedPageIds = collection };
			yield return reader with { IncludedPageIds = collection, Hidden = hidden };
			yield return reader with { ExcludedPageIds = collection };
			yield return reader with { ExcludedPageIds = collection, Hidden = hidden };
			yield return reader with { IncludedPageIds = new HashSet<string>() };
			yield return reader with { Search = "Page 00" };
		}
	}

	private async Task AssertCountsAsync(IReadOnlyList<WikiPage> pages)
	{
		foreach (var reader in Readers(pages))
		{
			var admitted = pages.Where(reader.Admits).ToList();
			var expected = admitted.SelectMany(p => p.Categories.Select(c => c.ToLowerInvariant()).Distinct())
				.CountBy(c => c).ToDictionary(pair => pair.Key, pair => pair.Value);
			await Assert.That(await Db.CountPagesAsync(null, reader)).IsEqualTo(admitted.Count);
			await Assert.That(await Db.CountPagesByCategoryAsync(reader)).IsEquivalentTo(expected);
			foreach (var category in new[] { "rules", "spells", "secret", "absent" })
				await Assert.That(await Db.CountPagesInCategoryAsync(category, reader)).IsEqualTo(expected.GetValueOrDefault(category));
		}
	}

	private async Task<List<WikiPage>> CurrentAsync(IEnumerable<WikiPage> pages)
	{
		var current = new List<WikiPage>();
		foreach (var page in pages)
			if (await Db.GetPageByIdAsync(page.Id) is WikiPage found) current.Add(found);
		return current;
	}

	[Test]
	public async Task CountsMatchThePagesForEveryReader()
		=> await AssertCountsAsync(await SeedAsync(240, seed: 2044));

	[Test]
	public async Task CountsFollowEveryWrite()
	{
		var pages = await SeedAsync(120, seed: 7);
		var random = new Random(11);
		for (var i = 0; i < 60; i++)
		{
			var page = pages[random.Next(pages.Count)];
			switch (random.Next(4))
			{
				case 0:
					await Db.SetPageMetadataAsync(page.Id, [Categories[random.Next(Categories.Length)]], random.Next(2) == 0);
					break;
				case 1:
					await Db.SetPageTitleAsync(page.Id, $"Renamed {i}");
					break;
				case 2:
					await Db.DeletePageAsync(page.Id);
					break;
				default:
					await Db.SetPageMetadataAsync(page.Id, [], true);
					break;
			}
		}
		await AssertCountsAsync(await CurrentAsync(pages));
		await ReopenAsync();
		await AssertCountsAsync(await CurrentAsync(pages));
	}

	[Test]
	public async Task AWorldWrittenBeforeTheCountsGetsThemOnItsNextStart()
	{
		var pages = await SeedAsync(90, seed: 3);
		// As a world written before the counts existed: none kept, no migration recorded.
		await Db.Store.WriteAsync(tx =>
		{
			tx.DeletePrefix(Tables.WikiCount, []);
			tx.DeletePrefix(Tables.WikiSummary, []);
			tx.DeletePrefix(Tables.WikiDraft, []);
			tx.Delete(Tables.Meta, Keys.Str("mig:0005_wiki_counts"));
		});
		// Until then a count scans, and is still right.
		await AssertCountsAsync(pages);
		await ReopenAsync();
		await Assert.That(Db.Store.Read(tx => tx.Count(Tables.WikiSummary))).IsEqualTo(90);
		await AssertCountsAsync(pages);
	}

	[Test]
	public async Task ACollectionCountReadsOnlyThatCollectionsPages()
	{
		var pages = await SeedAsync(60, seed: 5);
		var collection = pages.Take(10).ToList();
		// Every other page's summary and body would throw if a count read them.
		await Db.Store.WriteAsync(tx =>
		{
			foreach (var page in pages.Skip(10))
			{
				var key = Keys.Dbref(long.Parse(page.Id["wiki_page/".Length..]));
				tx.Put(Tables.WikiSummary, key, "not json"u8);
				tx.Put(Tables.WikiPage, key, "not json"u8);
			}
		});
		var reader = WikiVisibility.PublishedOnly with { IncludedPageIds = collection.Select(p => p.Id).ToHashSet() };
		await Assert.That(await Db.CountPagesAsync(null, reader)).IsEqualTo(collection.Count(reader.Admits));
		await Assert.That((await Db.CountPagesByCategoryAsync(reader)).Values.Sum())
			.IsEqualTo(collection.Where(reader.Admits).Sum(p => p.Categories.Select(c => c.ToLowerInvariant()).Distinct().Count()));
	}

	[Test]
	public async Task TheGameCollectionCountReadsOnlyThePagesItLeavesOut()
	{
		var pages = await SeedAsync(60, seed: 9);
		var packaged = pages.Take(10).ToList();
		await Db.Store.WriteAsync(tx =>
		{
			foreach (var page in pages.Skip(10))
				tx.Put(Tables.WikiSummary, Keys.Dbref(long.Parse(page.Id["wiki_page/".Length..])), "not json"u8);
		});
		var reader = WikiVisibility.All with { ExcludedPageIds = packaged.Select(p => p.Id).ToHashSet() };
		await Assert.That(await Db.CountPagesAsync(null, reader)).IsEqualTo(50);
	}
}
