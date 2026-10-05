using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for the metadata and listing additions to <see cref="IWikiService"/>:
/// GetAllPagesAsync, CountPagesAsync, GetByCategoryAsync, SetMetadataAsync.
/// Exercised against the in-memory implementation; the DB providers share the same
/// contract and are covered by the HTTP integration tests.
/// </summary>
public class WikiMetadataServiceTests
{
	private static IWikiService BuildService() =>
		InMemoryWikiStore.CreateService();

	private static async Task<WikiPage> CreatePageAsync(
		IWikiService svc, string title, WikiNamespace ns = WikiNamespace.Main, string[]? categories = null)
	{
		var result = await svc.CreateAsync(title, $"# {title}", "#1", ns, categories: categories);
		var page = result.Expect<WikiPage>();
		return page;
	}

	[Test]
	public async Task GetAllPages_SpansNamespaces_OrderedByNamespaceThenSlug()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, "Zulu");
		await CreatePageAsync(svc, "Alpha");
		await CreatePageAsync(svc, "Guide", WikiNamespace.Help);

		var all = await svc.GetAllPagesAsync();

		await Assert.That(all.Count).IsEqualTo(3);
		await Assert.That(all[0].Namespace).IsEqualTo("help");
		await Assert.That(all[1].Slug).IsEqualTo("alpha");
		await Assert.That(all[2].Slug).IsEqualTo("zulu");
	}

	[Test]
	public async Task GetAllPages_NamespaceFilter_RestrictsResults()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, "Main Page");
		await CreatePageAsync(svc, "Help Page", WikiNamespace.Help);

		var helpOnly = await svc.GetAllPagesAsync(ns: WikiNamespace.Help);

		await Assert.That(helpOnly.Count).IsEqualTo(1);
		await Assert.That(helpOnly[0].Namespace).IsEqualTo("help");
	}

	[Test]
	public async Task GetAllPages_Pagination_SkipsAndTakes()
	{
		var svc = BuildService();
		for (var i = 0; i < 5; i++)
			await CreatePageAsync(svc, $"Page {i}");

		var window = await svc.GetAllPagesAsync(skip: 2, take: 2);

		await Assert.That(window.Count).IsEqualTo(2);
	}

	[Test]
	public async Task CountPages_TotalAndPerNamespace()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, "One");
		await CreatePageAsync(svc, "Two");
		await CreatePageAsync(svc, "Help One", WikiNamespace.Help);

		await Assert.That(await svc.CountPagesAsync(null, WikiVisibility.All)).IsEqualTo(3);
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Help, WikiVisibility.All)).IsEqualTo(1);
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Character, WikiVisibility.All)).IsEqualTo(0);
	}

	[Test]
	public async Task CountPages_WithoutDrafts_ExcludesUnpublishedPages()
	{
		// The count is rendered beside a draft-filtered listing, so a total that counted drafts let a
		// reader difference it against the visible rows and learn how many drafts the store holds.
		var svc = BuildService();
		await CreatePageAsync(svc, "Published One");
		var draft = await CreatePageAsync(svc, "Draft One");
		await CreatePageAsync(svc, "Help Draft", WikiNamespace.Help);

		var help = await svc.GetAllPagesAsync(ns: WikiNamespace.Help);
		await svc.SetMetadataAsync(draft.Id, draft.Categories, published: false);
		await svc.SetMetadataAsync(help[0].Id, help[0].Categories, published: false);

		await Assert.That(await svc.CountPagesAsync(null, WikiVisibility.PublishedOnly)).IsEqualTo(1);
		await Assert.That(await svc.CountPagesAsync(null, WikiVisibility.All)).IsEqualTo(3);

		// Per namespace too: the namespace filter and the draft filter have to compose, not replace.
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Help, WikiVisibility.PublishedOnly)).IsEqualTo(0);
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Help, WikiVisibility.All)).IsEqualTo(1);
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Main, WikiVisibility.PublishedOnly)).IsEqualTo(1);
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Main, WikiVisibility.All)).IsEqualTo(2);
	}

	[Test]
	public async Task SetMetadata_StoresNormalizedCategories()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Magic System");

		var updated = (await svc.SetMetadataAsync(page.Id, ["  Lore ", "Magic", "magic", "Places of Note", ""], published: true))
			.Expect<WikiPage>();

		await Assert.That(updated.Categories).IsEquivalentTo(new[] { "lore", "magic", "places_of_note" });
	}

	[Test]
	public async Task CreateAsync_StoresNormalizedCategories()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Magic System", categories: ["Lore", " lore", ""]);

		await Assert.That(page.Categories).IsEquivalentTo(new[] { "lore" });
	}

	/// <summary>Categories are held by the page, not read from its text: a link to a category files nothing.</summary>
	[Test]
	public async Task CategoryLinkInText_IsNotMembership()
	{
		var svc = BuildService();
		var page = (await svc.CreateAsync("Index", "See [[Category:Lore]].", "#1")).Expect<WikiPage>();

		await Assert.That(page.Categories.Count).IsEqualTo(0);
		await Assert.That((await svc.GetByCategoryAsync("lore")).Count).IsEqualTo(0);
	}

	[Test]
	public async Task Categories_SurviveContentEdits()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Drifting", categories: ["Lore"]);

		var updated = (await svc.UpdateAsync(page.Id, "# Drifting\n\nNew text.", "#1")).Expect<WikiPage>();

		await Assert.That(updated.Categories).IsEquivalentTo(new[] { "lore" });
		await Assert.That((await svc.GetByCategoryAsync("lore")).Count).IsEqualTo(1);
	}

	[Test]
	public async Task SetMetadata_DoesNotCreateRevisionOrChangeContent()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Stable");

		var updated = (await svc.SetMetadataAsync(page.Id, ["lore"], published: false)).Expect<WikiPage>();
		await Assert.That(updated.Published).IsFalse();

		var reloaded = (await svc.GetByIdAsync(page.Id)).Expect<WikiPage>();
		await Assert.That(reloaded.Published).IsFalse();
		await Assert.That(reloaded.Categories).IsEquivalentTo(new[] { "lore" });
		await Assert.That(reloaded.RevisionNumber).IsEqualTo(page.RevisionNumber);
		await Assert.That(reloaded.MarkdownSource).IsEqualTo(page.MarkdownSource);

		var revisions = await svc.GetRevisionsAsync(page.Id);
		await Assert.That(revisions.Count).IsEqualTo(1); // only the creation snapshot
	}

	[Test]
	public async Task SetMetadata_UnknownId_ReturnsNotFound()
	{
		var svc = BuildService();

		var result = await svc.SetMetadataAsync("nope", ["lore"], true);

		await Assert.That(result is NotFound).IsTrue();
	}

	[Test]
	public async Task NewPage_DefaultsToPublishedWithNoCategories()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Defaults");

		await Assert.That(page.Published).IsTrue();
		await Assert.That(page.Categories.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetByCategory_ReturnsOnlyMatchingPages_CaseInsensitive()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, "Dragons", categories: ["Lore"]);
		await CreatePageAsync(svc, "Elves", categories: ["lore", "Peoples"]);
		await CreatePageAsync(svc, "Combat Rules", categories: ["Rules"]);

		var lorePages = await svc.GetByCategoryAsync("LORE");

		await Assert.That(lorePages.Count).IsEqualTo(2);
		await Assert.That(lorePages[0].Title).IsEqualTo("Dragons");
		await Assert.That(lorePages.All(p => p.Categories.Contains("lore"))).IsTrue();
		await Assert.That((await svc.GetByCategoryAsync("Peoples")).Count).IsEqualTo(1);
	}

	[Test]
	public async Task GetByCategory_NoMatches_ReturnsEmpty()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, "Nothing");

		var result = await svc.GetByCategoryAsync("ghost-category");

		await Assert.That(result.Count).IsEqualTo(0);
	}
}
