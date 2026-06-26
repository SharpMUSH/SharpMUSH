using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for the metadata and listing additions to <see cref="IWikiService"/>:
/// GetAllPagesAsync, CountPagesAsync, GetByCategoryAsync, GetByTagAsync, SetMetadataAsync.
/// Exercised against the in-memory implementation; the DB providers share the same
/// contract and are covered by the HTTP integration tests.
/// </summary>
public class WikiMetadataServiceTests
{
	private static IWikiService BuildService() =>
		new InMemoryWikiService(new WikiMarkdigPipeline());

	private static async Task<WikiPage> CreatePageAsync(
		IWikiService svc, string title, WikiNamespace ns = WikiNamespace.Main)
	{
		var result = await svc.CreateAsync(title, $"# {title}", "#1", ns);
		await Assert.That(result.IsT0).IsTrue();
		return result.AsT0;
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

		await Assert.That(await svc.CountPagesAsync()).IsEqualTo(3);
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Help)).IsEqualTo(1);
		await Assert.That(await svc.CountPagesAsync(WikiNamespace.Character)).IsEqualTo(0);
	}

	[Test]
	public async Task SetMetadata_StoresNormalizedCategoryAndTags()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Magic System");

		var result = await svc.SetMetadataAsync(page.Id, "  Lore ", ["Magic", " magic ", "RULES", ""], published: true);

		await Assert.That(result.IsT0).IsTrue();
		var updated = result.AsT0;
		await Assert.That(updated.Category).IsEqualTo("lore");
		await Assert.That(updated.Tags.Count).IsEqualTo(2);
		await Assert.That(updated.Tags).Contains("magic");
		await Assert.That(updated.Tags).Contains("rules");
	}

	[Test]
	public async Task SetMetadata_BlankCategory_StoresDefault()
	{
		// Category is part of page identity, so a blank value normalizes to the default "general".
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Untagged");

		var result = await svc.SetMetadataAsync(page.Id, "   ", [], published: true);

		await Assert.That(result.AsT0.Category).IsEqualTo("general");
	}

	[Test]
	public async Task SetMetadata_DoesNotCreateRevisionOrChangeContent()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Stable");

		await svc.SetMetadataAsync(page.Id, "lore", ["x"], published: false);

		var reloaded = await svc.GetByIdAsync(page.Id);
		await Assert.That(reloaded.AsT0.RevisionNumber).IsEqualTo(page.RevisionNumber);
		await Assert.That(reloaded.AsT0.MarkdownSource).IsEqualTo(page.MarkdownSource);

		var revisions = await svc.GetRevisionsAsync(page.Id);
		await Assert.That(revisions.Count).IsEqualTo(1); // only the creation snapshot
	}

	[Test]
	public async Task SetMetadata_UnknownId_ReturnsNotFound()
	{
		var svc = BuildService();

		var result = await svc.SetMetadataAsync("nope", "lore", [], true);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task NewPage_DefaultsToPublishedWithNoMetadata()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Defaults");

		await Assert.That(page.Published).IsTrue();
		await Assert.That(page.Category).IsEqualTo("general");
		await Assert.That(page.Tags.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetByCategory_ReturnsOnlyMatchingPages_CaseInsensitive()
	{
		var svc = BuildService();
		var lore1 = await CreatePageAsync(svc, "Dragons");
		var lore2 = await CreatePageAsync(svc, "Elves");
		var other = await CreatePageAsync(svc, "Combat Rules");
		await svc.SetMetadataAsync(lore1.Id, "lore", [], true);
		await svc.SetMetadataAsync(lore2.Id, "lore", [], true);
		await svc.SetMetadataAsync(other.Id, "rules", [], true);

		var lorePages = await svc.GetByCategoryAsync("LORE");

		await Assert.That(lorePages.Count).IsEqualTo(2);
		await Assert.That(lorePages[0].Title).IsEqualTo("Dragons");
		await Assert.That(lorePages.All(p => p.Category == "lore")).IsTrue();
	}

	[Test]
	public async Task GetByTag_ReturnsPagesCarryingTag()
	{
		var svc = BuildService();
		var a = await CreatePageAsync(svc, "Alpha");
		var b = await CreatePageAsync(svc, "Beta");
		await svc.SetMetadataAsync(a.Id, null, ["magic", "fire"], true);
		await svc.SetMetadataAsync(b.Id, null, ["water"], true);

		var magic = await svc.GetByTagAsync("Magic");

		await Assert.That(magic.Count).IsEqualTo(1);
		await Assert.That(magic[0].Title).IsEqualTo("Alpha");
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
