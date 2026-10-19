using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiSingleCategoryCountTests : LightningDatabaseFixture
{
	private async Task<WikiPage> Add(string title, string[] categories, bool published = true,
		string author = "#1", string ns = "main")
	{
		var at = DateTimeOffset.UnixEpoch;
		return (await Db.CreatePageAsync(new WikiPage("", title, title, ns, "body", "<p>body</p>", "body",
			author, author, at, at, 1)
		{ Categories = categories, Published = published })).Expect<WikiPage>();
	}

	[Test]
	public async Task ExactCategoryCountMatchesIndependentVisibilityReference()
	{
		var published = await Add("Public", ["rules", "RULES", "rules"]);
		var own = await Add("Own draft", ["rules"], false, "#42");
		var other = await Add("Other draft", ["rules"], false, "#43");
		var privatePage = await Add("Private page", ["rules"]);
		var privateNamespace = await Add("Private namespace", ["rules"], ns: "help");
		var privateCategory = await Add("Private category", ["rules", "secret"]);
		var prefixNeighbor = await Add("Prefix neighbor", ["rules-extra"]);
		var uncategorized = await Add("No category", []);
		var pages = new[] { published, own, other, privatePage, privateNamespace, privateCategory, prefixNeighbor, uncategorized };
		var hidden = new WikiReadRestrictions(false,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "help" },
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "secret" },
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { privatePage.Id });
		var restricted = WikiVisibility.All with { Hidden = hidden };
		var visibilities = new[]
		{
			WikiVisibility.All, WikiVisibility.PublishedOnly, new WikiVisibility(false, "#42"), restricted,
			WikiVisibility.PublishedOnly with { Hidden = hidden },
			restricted with { IncludedPageIds = new HashSet<string> { published.Id, own.Id, privateCategory.Id } },
			restricted with
			{
				IncludedPageIds = new HashSet<string> { published.Id, own.Id, privateCategory.Id },
				ExcludedPageIds = new HashSet<string> { own.Id }
			},
			WikiVisibility.All with { IncludedPageIds = new HashSet<string>() },
			WikiVisibility.All with { Hidden = WikiReadRestrictions.All },
			WikiVisibility.All with { Search = "Public" }
		};
		foreach (var visibility in visibilities)
		{
			foreach (var category in new[] { "rules", "RULES", "rules-extra", "missing", "" })
			{
				var expected = pages.Count(page => visibility.Admits(page)
					&& page.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
				await Assert.That(await Db.CountPagesInCategoryAsync(category, visibility)).IsEqualTo(expected);
			}
		}
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.All)).IsEqualTo(6);
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", restricted)).IsEqualTo(3);
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.PublishedOnly with { Hidden = hidden })).IsEqualTo(1);
		var facade = new WikiStoreService(Db, new WikiMarkdigPipeline());
		await Assert.That(await facade.CountPagesInCategoryAsync(" RULES ", WikiVisibility.All)).IsEqualTo(6);
	}

	[Test]
	public async Task ExactCategoryCountFollowsMetadataDeletionRecreationAndReopen()
	{
		var first = await Add("First", ["rules"]);
		var draft = await Add("Draft", ["rules"], false, "#42");
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.PublishedOnly)).IsEqualTo(1);
		await Db.SetPageMetadataAsync(first.Id, ["elsewhere"], true);
		await Db.SetPageMetadataAsync(draft.Id, ["rules"], true);
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.All)).IsEqualTo(1);
		await Assert.That(await Db.CountPagesInCategoryAsync("elsewhere", WikiVisibility.All)).IsEqualTo(1);
		await Db.DeletePageAsync(draft.Id);
		var recreated = await Add("Draft", ["rules"]);
		await Assert.That(recreated.Id == draft.Id).IsFalse();
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.All with
		{ IncludedPageIds = new HashSet<string> { draft.Id } })).IsEqualTo(0);
		await ReopenAsync();
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.PublishedOnly)).IsEqualTo(1);
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.All with
		{ ExcludedPageIds = new HashSet<string> { recreated.Id } })).IsEqualTo(0);
		await Assert.That(await Db.CountPagesInCategoryAsync("elsewhere", WikiVisibility.All)).IsEqualTo(1);
	}

	[Test]
	public async Task RestrictedExactCategoryCountReadsMatchingMetadataWithoutReadingUnrelatedCategoryBodies()
	{
		await Add("Admitted", ["rules"]);
		await Add("Denied by second category", ["rules", "secret"]);
		var unrelated = await Add("Unrelated unreadable body", ["rules-extra"]);
		// Valid native transaction, deliberately malformed application JSON, as in WikiListIndexTests.
		// Its index remains valid, so an all-category metadata scan would try to deserialize this body.
		await Db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage,
			Keys.Dbref(long.Parse(unrelated.Id["wiki_page/".Length..])), "not json"u8));
		var visibility = WikiVisibility.PublishedOnly with
		{
			Hidden = new WikiReadRestrictions(false, new HashSet<string>(),
				new HashSet<string> { "secret" }, new HashSet<string>())
		};
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", visibility)).IsEqualTo(1);
		await Assert.That(await Db.CountPagesInCategoryAsync("absent", visibility)).IsEqualTo(0);
	}

	[Test]
	public async Task UnrestrictedExactCategoryCountDoesNotHydrateMatchingPageBodies()
	{
		var member = await Add("Unreadable member", ["rules"]);
		await Db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage,
			Keys.Dbref(long.Parse(member.Id["wiki_page/".Length..])), "not json"u8));
		await Assert.That(await Db.CountPagesInCategoryAsync("rules", WikiVisibility.PublishedOnly)).IsEqualTo(1);
	}
}
