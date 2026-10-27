using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The title index (<c>LightningDatabase.WikiTitles.cs</c>) against a reference computed from the pages and their
/// translations, for every kind of reader, through writes, and from the migration that builds it.
/// </summary>
public class WikiTitleSearchTests : LightningDatabaseFixture
{
	private static readonly string[] Words = ["Fireball", "Spell", "Élan", "STRASSE", "spellbook", "Ancestry", "Lore", "ab"];
	private static readonly string[] Categories = ["spells", "lore", "secret"];
	private static readonly string[] Authors = ["#1", "#42"];
	private static readonly string[] Terms = ["spell", "SPELL", "élan", "ÉLAN", "lan", "a", "ab", "ll", "fire ball", "book", "nothing", "Page 01"];
	private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

	private sealed record Seeded(WikiPage Page, Dictionary<string, string> Translations);

	private async Task<List<Seeded>> SeedAsync(int count, int seed)
	{
		var random = new Random(seed);
		var pages = new List<Seeded>();
		for (var i = 0; i < count; i++)
		{
			var author = Authors[random.Next(Authors.Length)];
			var title = $"{Words[random.Next(Words.Length)]} {Words[random.Next(Words.Length)]} Page {i:D3}";
			var page = (await Db.CreatePageAsync(new WikiPage("", $"page-{i}", title, "main",
				"body", "<p>body</p>", "body", author, author, At, At, 1)
			{
				Categories = Enumerable.Range(0, random.Next(3)).Select(_ => Categories[random.Next(Categories.Length)]).ToArray(),
				Published = random.Next(3) > 0
			})).Expect<WikiPage>();
			var translations = new Dictionary<string, string>(StringComparer.Ordinal);
			if (random.Next(3) == 0)
			{
				var translated = $"{Words[random.Next(Words.Length)]} übersetzt {i}";
				await WriteTranslationAsync(page.Id, "de", translated, published: random.Next(2) == 0, revision: null);
				translations["de"] = translated;
			}
			pages.Add(new Seeded(page, translations));
		}
		return pages;
	}

	private Task<TranslationWriteResult> WriteTranslationAsync(string pageId, string locale, string title, bool published, int? revision)
		=> Db.WriteTranslationAsync(pageId, locale, title, new WikiBody("body", "<p>body</p>", "body"), "#1", null, published, revision, At);

	private static IEnumerable<WikiVisibility> Readers(IReadOnlyList<Seeded> pages)
	{
		var hidden = new WikiReadRestrictions(false, new HashSet<string>(),
			new HashSet<string>(StringComparer.Ordinal) { "secret" },
			new HashSet<string>(StringComparer.Ordinal) { pages[0].Page.Id, pages[3].Page.Id });
		var collection = pages.Where((_, i) => i % 3 == 0).Select(p => p.Page.Id).Append("bare").ToHashSet(StringComparer.Ordinal);
		foreach (var reader in new[] { WikiVisibility.All, WikiVisibility.PublishedOnly, new WikiVisibility(false, "#42") })
		{
			yield return reader;
			yield return reader with { Hidden = hidden };
			yield return reader with { Hidden = WikiReadRestrictions.All };
			yield return reader with { IncludedPageIds = collection };
			yield return reader with { ExcludedPageIds = collection, Hidden = hidden };
			yield return reader with { IncludedPageIds = new HashSet<string>() };
		}
	}

	private async Task AssertSearchesAsync(IReadOnlyList<Seeded> pages)
	{
		foreach (var reader in Readers(pages))
		{
			var admitted = pages.Where(p => reader.Admits(p.Page)).ToList();
			foreach (var term in Terms)
			{
				var expected = admitted.Where(p => p.Page.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
						|| p.Translations.Values.Any(title => title.Contains(term, StringComparison.OrdinalIgnoreCase)))
					.Select(p => p.Page.Id).Order(StringComparer.Ordinal).ToList();
				var found = (await Db.FindPagesByTitleAsync(term, reader)).Order(StringComparer.Ordinal).ToList();
				await Assert.That(found).IsEquivalentTo(expected).Because($"term '{term}'");
			}
			foreach (var category in new[] { "spells", "secret", "absent" })
			{
				var expected = admitted.Where(p => p.Page.Categories.Contains(category)).Select(p => p.Page.Id).Order(StringComparer.Ordinal).ToList();
				await Assert.That((await Db.GetPageIdsByCategoryAsync(category, reader)).Order(StringComparer.Ordinal).ToList())
					.IsEquivalentTo(expected);
			}
		}
	}

	private async Task<List<Seeded>> CurrentAsync(IEnumerable<Seeded> pages)
	{
		var current = new List<Seeded>();
		foreach (var seeded in pages)
		{
			if (await Db.GetPageByIdAsync(seeded.Page.Id) is not WikiPage page) continue;
			var translations = (await Db.GetTranslationSummariesAsync(page.Id)).ToDictionary(t => t.Locale, t => t.Title, StringComparer.Ordinal);
			current.Add(new Seeded(page, translations));
		}
		return current;
	}

	[Test]
	public async Task SearchesMatchThePagesForEveryReader()
		=> await AssertSearchesAsync(await SeedAsync(150, seed: 2044));

	[Test]
	public async Task TheIndexFollowsEveryWrite()
	{
		var pages = await SeedAsync(90, seed: 7);
		var random = new Random(13);
		for (var i = 0; i < 80; i++)
		{
			var page = pages[random.Next(pages.Count)].Page;
			var translations = await Db.GetTranslationSummariesAsync(page.Id);
			switch (random.Next(5))
			{
				case 0:
					await Db.SetPageTitleAsync(page.Id, $"{Words[random.Next(Words.Length)]} renamed {i}");
					break;
				case 1:
					await Db.DeletePageAsync(page.Id);
					break;
				case 2 when translations.Count > 0:
					await WriteTranslationAsync(page.Id, "de", $"Fireball neu {i}", published: true, translations[0].RevisionNumber);
					break;
				case 3 when translations.Count > 0:
					await Db.DeleteTranslationAsync(page.Id, "de");
					break;
				default:
					await WriteTranslationAsync(page.Id, "fr", $"Sort {Words[random.Next(Words.Length)]} {i}", published: false,
						(await Db.GetTranslationSummariesAsync(page.Id)).FirstOrDefault(t => t.Locale == "fr")?.RevisionNumber);
					break;
			}
		}
		await AssertSearchesAsync(await CurrentAsync(pages));
		await ReopenAsync();
		await AssertSearchesAsync(await CurrentAsync(pages));
	}

	[Test]
	public async Task AWorldWrittenBeforeTheIndexGetsItOnItsNextStart()
	{
		var pages = await SeedAsync(60, seed: 3);
		await Db.Store.WriteAsync(tx =>
		{
			tx.DeletePrefix(Tables.WikiTitle, []);
			tx.Delete(Tables.Meta, Keys.Str("mig:0008_wiki_title_index"));
		});
		// Until then a search reads every page's titles, and is still right.
		await AssertSearchesAsync(pages);
		await ReopenAsync();
		await Assert.That(Db.Store.Read(tx => tx.Count(Tables.WikiTitle))).IsGreaterThan(0);
		await AssertSearchesAsync(pages);
	}

	[Test]
	public async Task ASearchReadsOnlyThePagesWhoseTitlesHoldTheTerm()
	{
		var pages = new List<WikiPage>();
		for (var i = 0; i < 40; i++)
			pages.Add((await Db.CreatePageAsync(new WikiPage("", $"read-{i}", i < 3 ? $"Fireball {i}" : $"Lore {i}", "main",
				"body", "<p>body</p>", "body", "#1", "#1", At, At, 1))).Expect<WikiPage>());
		// Every page without "fireball" in its title would throw if the search read it.
		await Db.Store.WriteAsync(tx =>
		{
			foreach (var page in pages.Skip(3))
				tx.Put(Tables.WikiPage, Keys.Dbref(long.Parse(page.Id["wiki_page/".Length..])), "not json"u8);
		});
		await Assert.That((await Db.FindPagesByTitleAsync("fireBALL", WikiVisibility.All)).Order(StringComparer.Ordinal).ToList())
			.IsEquivalentTo(pages.Take(3).Select(p => p.Id).Order(StringComparer.Ordinal).ToList());
	}
}
