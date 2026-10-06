using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Wiki listings answered from the ordered list indexes and revision pages read as bounded ranges (#1462).
/// Every listing is compared with the definition the store had when it sorted every page in memory —
/// <see cref="Reference"/> below — over pages chosen to stress its tie-breaks: titles that differ only in
/// case, a prefix, a supplementary-plane character against a fullwidth one, namespaces and categories
/// that differ only in case, a page in several categories, drafts by two authors and timestamps that tie.
/// </summary>
public class WikiListIndexTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private static readonly WikiVisibility[] Visibilities =
	[
		WikiVisibility.All,
		WikiVisibility.PublishedOnly,
		new(IncludeDrafts: false, AuthorDbref: "#42"),
		// A reader the help namespace and the rules category are closed to: the listings read each candidate
		// row, and paging must still count only what is shown.
		new(IncludeDrafts: true)
		{
			Hidden = new WikiReadRestrictions(false,
				new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "help" },
				new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rules" },
				new HashSet<string>())
		}
	];

	private async Task<string> Add(string ns, string slug, string title, string[] categories, bool published,
		string author = "#1", int minutes = 0)
	{
		var at = T0.AddMinutes(minutes);
		var page = new WikiPage("", slug, title, ns, "md " + slug, "<p>" + slug + "</p>", slug, author, author, at, at, 1)
		{
			Categories = categories,
			Published = published
		};
		return (await Wiki.CreatePageAsync(page)).Expect<WikiPage>().Id;
	}

	private async Task<List<string>> Seed()
	{
		return
		[
			await Add("main", "apple", "apple", ["lore", "x", "X", "y"], true, minutes: 5),
			await Add("main", "apple_upper", "Apple", ["Lore", "y"], true, minutes: 5),
			await Add("main", "ab", "ab", ["lore"], false, author: "#42", minutes: 3),
			await Add("main", "a", "a", ["rules", "x"], true, minutes: 9),
			await Add("help", "emoji", "\U0001F600 smile", ["lore", "Y"], true, minutes: 9),
			await Add("help", "fullwidth", "＠fullwidth", ["lore"], false, minutes: 1),
			await Add("Main", "legacy_case", "zeta", ["LORE", "x"], true, minutes: 9),
			await Add("MAIN", "legacy_upper", "Zeta", [], true, minutes: 2),
			await Add("main", "uncategorized", "Ünïcode", ["x"], false, author: "#42", minutes: 7),
			await Add("system", "z", "", ["rules"], true, minutes: 0)
		];
	}

	/// <summary>Every page, read row by row the way the old listings did.</summary>
	private async Task<List<WikiPage>> AllPages()
	{
		var keys = Db.Store.Read(tx => tx.Range(Tables.WikiPage, []).Select(e => Keys.ReadDbref(e.Key)).ToList());
		var pages = new List<WikiPage>();
		foreach (var key in keys) pages.Add((await Wiki.GetPageByIdAsync($"wiki_page/{key}")).Expect<WikiPage>());
		return pages;
	}

	private static long PageKey(WikiPage page) => long.Parse(page.Id["wiki_page/".Length..]);

	/// <summary>The listings as they were defined before the indexes: filter, then a stable in-memory sort.</summary>
	private static class Reference
	{
		private static IEnumerable<WikiPage> Visible(IEnumerable<WikiPage> all, WikiVisibility v)
			=> all.Where(v.Admits);

		public static IEnumerable<string> Recent(IEnumerable<WikiPage> all, int count, WikiVisibility v)
			=> Visible(all, v).OrderByDescending(p => p.UpdatedAt).ThenByDescending(PageKey).Take(count).Select(p => p.Id);

		public static IEnumerable<string> Pages(IEnumerable<WikiPage> all, string? ns, int skip, int take, WikiVisibility v)
			=> Visible(all, v)
				.Where(p => ns is null || p.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase))
				.OrderBy(p => p.Namespace, StringComparer.Ordinal)
				.ThenBy(p => p.Slug, StringComparer.Ordinal)
				.Skip(skip).Take(take).Select(p => p.Id);

		public static int Count(IEnumerable<WikiPage> all, string? ns, WikiVisibility v)
			=> Visible(all, v).Count(p => ns is null || p.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase));

		public static IEnumerable<string> Category(IEnumerable<WikiPage> all, string category, int skip, int take, WikiVisibility v)
			=> Visible(all, v)
				.Where(p => p.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
				.OrderBy(p => p.Title, StringComparer.Ordinal)
				.Skip(skip).Take(take).Select(p => p.Id);

		public static Dictionary<string, int> CategoryCounts(IEnumerable<WikiPage> all, WikiVisibility v)
			=> Visible(all, v)
				.SelectMany(p => p.Categories.Select(c => c.ToLowerInvariant()).Distinct())
				.CountBy(c => c)
				.ToDictionary(pair => pair.Key, pair => pair.Value);
	}

	private async Task AssertListingsMatchReference()
	{
		var all = await AllPages();
		foreach (var v in Visibilities)
		{
			foreach (var count in new[] { 0, 1, 3, 50 })
			{
				await Assert.That((await Wiki.GetRecentPagesAsync(count, v)).Select(p => p.Id))
					.IsEquivalentTo(Reference.Recent(all, count, v), CollectionOrdering.Matching);
			}

			foreach (var ns in new string?[] { null, "main", "MAIN", "help", "system", "nowhere" })
			{
				foreach (var (skip, take) in new[] { (0, 50), (1, 2), (3, 3), (40, 5) })
				{
					await Assert.That((await Wiki.GetPagesAsync(ns, skip, take, v)).Select(p => p.Id))
						.IsEquivalentTo(Reference.Pages(all, ns, skip, take, v), CollectionOrdering.Matching);
				}
			}

			await Assert.That(await Wiki.CountPagesByCategoryAsync(v)).IsEquivalentTo(Reference.CategoryCounts(all, v));

			foreach (var category in new[] { "lore", "LORE", "rules", "x", "Y", "", "none" })
			{
				foreach (var (skip, take) in new[] { (0, 50), (1, 2) })
				{
					await Assert.That((await Wiki.GetPagesByCategoryAsync(category, skip, take, v)).Select(p => p.Id))
						.IsEquivalentTo(Reference.Category(all, category, skip, take, v), CollectionOrdering.Matching);
				}
			}
		}

		foreach (var ns in new string?[] { null, "main", "help", "nowhere" })
		{
			foreach (var v in Visibilities)
			{
				await Assert.That(await Wiki.CountPagesAsync(ns, v)).IsEqualTo(Reference.Count(all, ns, v));
			}
		}
	}

	[Test]
	public async Task ListingsMatchTheirInMemoryDefinition()
	{
		await Seed();
		await AssertListingsMatchReference();

		// Spelled out once: ordinal is UTF-16 code-unit order, so the supplementary-plane emoji sorts before
		// the fullwidth sign, which UTF-8 byte order would reverse.
		await Assert.That((await Wiki.GetPagesByCategoryAsync("lore", 0, 50, WikiVisibility.All)).Select(p => p.Title))
			.IsEquivalentTo(["Apple", "ab", "apple", "zeta", "\U0001F600 smile", "\uFF20fullwidth"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task IndexesFollowEveryPageWrite()
	{
		var ids = await Seed();

		await Wiki.UpdatePageBodyAsync(ids[0], new WikiBody("new", "<p>new</p>", "new"), "#1", null, T0.AddMinutes(30));
		await Wiki.SetPageMetadataAsync(ids[0], ["rules"], published: true);
		await Wiki.UpdatePageBodyAsync(ids[3], new WikiBody("moved", "<p>moved</p>", "moved"), "#1", null, T0.AddMinutes(31));
		await Wiki.SetPageMetadataAsync(ids[3], ["lore", "y", "z"], published: false);
		await Wiki.SetPageMetadataAsync(ids[2], ["lore"], published: true);
		await Wiki.SetRequirementsAsync(new WikiRequirementSet(WikiRuleTarget.ForPage(ids[4]), WikiRequirementSet.Protection, "#1", T0));
		await Wiki.DeletePageAsync(ids[6]);
		await AssertListingsMatchReference();

		// A deleted page leaves nothing behind in any list index.
		var deletedKey = long.Parse(ids[6]["wiki_page/".Length..]);
		foreach (var table in new[] { Tables.WikiRecent, Tables.WikiByNamespace, Tables.WikiByCategory })
		{
			var keys = Db.Store.Read(tx => tx.Range(table, []).Select(e => Keys.ReadDbref(e.Key.AsSpan(e.Key.Length - 8))).ToList());
			await Assert.That(keys).DoesNotContain(deletedKey);
		}
	}

	/// <summary>Proof the listings read the indexes: pages they do not return can be unreadable.</summary>
	[Test]
	public async Task ListingsDecodeOnlyThePagesTheyReturn()
	{
		var ids = await Seed();
		foreach (var id in new[] { ids[0], ids[1], ids[4], ids[9] })
		{
			await Db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(long.Parse(id["wiki_page/".Length..])), "not json"u8));
		}

		// The untitled rules page sorts first and is unreadable; skipping it does not read it.
		await Assert.That((await Wiki.GetPagesByCategoryAsync("rules", 1, 1, WikiVisibility.All)).Single().Id).IsEqualTo(ids[3]);
		// Newest published: three pages tie on the timestamp and the newest-created of them wins.
		await Assert.That((await Wiki.GetRecentPagesAsync(1, WikiVisibility.PublishedOnly)).Single().Id).IsEqualTo(ids[6]);
		await Assert.That((await Wiki.GetPagesAsync("MAIN", 0, 1, WikiVisibility.All)).Single().Id).IsEqualTo(ids[7]);
		await Assert.That(await Wiki.CountPagesAsync(null, WikiVisibility.All)).IsEqualTo(10);
	}

	/// <summary>The counts by state as defined over decoded page rows.</summary>
	private async Task<WikiPageCounts> ReferenceCounts(WikiVisibility visibility)
	{
		var counted = (await AllPages()).Where(visibility.Admits).ToList();
		return new WikiPageCounts(counted.Count(p => p.Published), counted.Count(p => !p.Published));
	}

	/// <summary>
	/// The counts by state follow publication and deletion, and without read restrictions are read from the
	/// indexes alone: once they are taken as the reference, every page row is made unreadable and the counts
	/// still answer.
	/// </summary>
	[Test]
	public async Task CountsByStateFollowWritesWithoutReadingPageRows()
	{
		var ids = await Seed();
		await Wiki.SetPageMetadataAsync(ids[5], ["lore"], published: true);
		await Wiki.DeletePageAsync(ids[0]);

		var all = await ReferenceCounts(WikiVisibility.All);
		var published = await ReferenceCounts(WikiVisibility.PublishedOnly);
		var restricted = await ReferenceCounts(Visibilities[^1]);
		await Assert.That(all).IsEqualTo(new WikiPageCounts(Published: 7, Drafts: 2));
		await Assert.That(published).IsEqualTo(new WikiPageCounts(Published: 7, Drafts: 0));
		await Assert.That(await Wiki.CountPagesByStateAsync(Visibilities[^1])).IsEqualTo(restricted);

		await Db.Store.WriteAsync(tx =>
		{
			foreach (var key in tx.Range(Tables.WikiPage, []).Select(e => e.Key).ToList()) tx.Put(Tables.WikiPage, key, "not json"u8);
		});

		await Assert.That(await Wiki.CountPagesByStateAsync(WikiVisibility.All)).IsEqualTo(all);
		await Assert.That(await Wiki.CountPagesByStateAsync(WikiVisibility.PublishedOnly)).IsEqualTo(published);
	}

	[Test]
	public async Task RevisionPagesAreNewestFirstByOffsetAndByCursor()
	{
		var id = await Add("main", "history", "History", ["lore"], true);
		for (var i = 2; i <= 30; i++)
		{
			await Wiki.UpdatePageBodyAsync(id, new WikiBody($"v{i}", $"<p>v{i}</p>", $"v{i}"), "#1", null, T0.AddMinutes(i));
		}

		(await Wiki.WriteTranslationAsync(id, "fr", "Histoire", new WikiBody("fr", "<p>fr</p>", "fr"), "#1", null, true, null, T0))
			.Expect<WikiTranslation>();

		static int[] Numbers(IEnumerable<WikiRevision> revisions) => revisions.Select(r => r.RevisionNumber).ToArray();

		await Assert.That(Numbers(await Wiki.GetRevisionsAsync(id, "", 0, 3))).IsEquivalentTo([30, 29, 28], CollectionOrdering.Matching);
		await Assert.That(Numbers(await Wiki.GetRevisionsAsync(id, "", 5, 3))).IsEquivalentTo([25, 24, 23], CollectionOrdering.Matching);
		await Assert.That(Numbers(await Wiki.GetRevisionsAsync(id, "", 28, 5))).IsEquivalentTo([2, 1], CollectionOrdering.Matching);
		await Assert.That(Numbers(await Wiki.GetRevisionsAsync(id, "fr", 0, 5))).IsEquivalentTo([1], CollectionOrdering.Matching);

		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id, "", 23, 3))).IsEquivalentTo([22, 21, 20], CollectionOrdering.Matching);
		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id, "", 1000, 2))).IsEquivalentTo([30, 29], CollectionOrdering.Matching);
		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id, "", 3, 5))).IsEquivalentTo([2, 1], CollectionOrdering.Matching);
		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id, "", 1, 5))).IsEmpty();
		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id, "", 0, 5))).IsEmpty();
		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id, "fr", 2, 5))).IsEquivalentTo([1], CollectionOrdering.Matching);
		// The bare numeric id reaches the same stream as the canonical one.
		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id["wiki_page/".Length..], "", 2, 5))).IsEquivalentTo([1], CollectionOrdering.Matching);

		// Proof a page reads only its own range: the oldest revisions can be unreadable.
		await Db.Store.WriteAsync(tx =>
		{
			tx.Put(Tables.WikiRev, Keys.Composite(id, "", 1u), "not json"u8);
			tx.Put(Tables.WikiRev, Keys.Composite(id, "", 2u), "not json"u8);
		});
		await Assert.That(Numbers(await Wiki.GetRevisionsAsync(id, "", 0, 20))).IsEquivalentTo(Enumerable.Range(11, 20).Reverse().ToArray(), CollectionOrdering.Matching);
		await Assert.That(Numbers(await Wiki.GetRevisionsBeforeAsync(id, "", 10, 7))).IsEquivalentTo([9, 8, 7, 6, 5, 4, 3], CollectionOrdering.Matching);
	}
}
