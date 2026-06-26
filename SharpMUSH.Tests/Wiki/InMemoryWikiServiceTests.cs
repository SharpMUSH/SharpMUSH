using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for <see cref="InMemoryWikiService"/>.
/// All tests run against a fresh service instance to ensure isolation.
/// Note: CreateAsync derives the slug from the title via Slugify(title).
/// </summary>
public class InMemoryWikiServiceTests
{
	private static IWikiService BuildService() =>
		new InMemoryWikiService(new WikiMarkdigPipeline());

	/// <summary>
	/// Creates a page and asserts it succeeded, returning the <see cref="WikiPage"/>.
	/// </summary>
	private static async Task<WikiPage> CreatePageAsync(
		IWikiService svc,
		string title = "Test Page",
		WikiNamespace ns = WikiNamespace.Main,
		string markdown = "Hello **world**.",
		string editor = "#1")
	{
		var result = await svc.CreateAsync(title, markdown, editor, ns);
		await Assert.That(result.IsT0).IsTrue();
		return result.AsT0;
	}

	[Test]
	public async Task CreateAsync_ReturnsPageWithAssignedId()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc);

		await Assert.That(page.Id).IsNotNull();
		await Assert.That(page.Id).IsNotEmpty();
	}

	[Test]
	public async Task CreateAsync_SlugDerivedFromTitle()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, title: "My Cool Page");

		await Assert.That(page.Slug).IsEqualTo("my_cool_page");
	}

	[Test]
	public async Task CreateAsync_SetsTitleAndNamespace()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, title: "Help Intro", ns: WikiNamespace.Help);

		await Assert.That(page.Title).IsEqualTo("Help Intro");
		await Assert.That(page.Namespace).IsEqualTo("help");
	}

	[Test]
	public async Task CreateAsync_StoresMarkdownSourceAndRenderedHtml()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, markdown: "Hello **world**.");

		await Assert.That(page.MarkdownSource).IsEqualTo("Hello **world**.");
		await Assert.That(page.RenderedHtml).IsNotNull();
		await Assert.That(page.RenderedHtml).Contains("<strong>world</strong>");
	}

	[Test]
	public async Task CreateAsync_SetsRevisionNumberToOne()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc);

		await Assert.That(page.RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task CreateAsync_SetsAuthorDbref()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, editor: "#42");

		await Assert.That(page.AuthorDbref).IsEqualTo("#42");
		await Assert.That(page.LastEditorDbref).IsEqualTo("#42");
	}

	[Test]
	public async Task CreateAsync_SameTitleSameNamespace_ReturnsError()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Duplicate");

		var result = await svc.CreateAsync("Duplicate", "content", "#1", WikiNamespace.Main);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task CreateAsync_SameTitleDifferentNamespace_Succeeds()
	{
		var svc = BuildService();
		var main = await CreatePageAsync(svc, title: "Intro", ns: WikiNamespace.Main);
		var help = await CreatePageAsync(svc, title: "Intro", ns: WikiNamespace.Help);

		await Assert.That(main.Id).IsNotEqualTo(help.Id);
	}

	[Test]
	public async Task CreateAsync_SameSlugDifferentCategory_Succeeds()
	{
		// Category is part of identity, so the same slug may live in different categories.
		var svc = BuildService();
		var lore = (await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, "lore")).AsT0;
		var rules = (await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, "rules")).AsT0;

		await Assert.That(lore.Id).IsNotEqualTo(rules.Id);
		await Assert.That((await svc.GetBySlugAsync("dragons", "lore", WikiNamespace.Main)).AsT0.Id).IsEqualTo(lore.Id);
		await Assert.That((await svc.GetBySlugAsync("dragons", "rules", WikiNamespace.Main)).AsT0.Id).IsEqualTo(rules.Id);
	}

	[Test]
	public async Task CreateAsync_SameSlugSameCategory_ReturnsError()
	{
		var svc = BuildService();
		await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, "lore");

		var result = await svc.CreateAsync("Dragons", "more", "#1", WikiNamespace.Main, "lore");

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task SetMetadata_ChangingCategory_RekeysPage()
	{
		var svc = BuildService();
		var page = (await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, "lore")).AsT0;

		await svc.SetMetadataAsync(page.Id, "rules", [], true);

		await Assert.That((await svc.GetBySlugAsync("dragons", "rules", WikiNamespace.Main)).IsT0).IsTrue();
		await Assert.That((await svc.GetBySlugAsync("dragons", "lore", WikiNamespace.Main)).IsT1).IsTrue();
	}

	[Test]
	public async Task SetMetadata_ChangingCategoryToExisting_IsRejected()
	{
		var svc = BuildService();
		var lore = (await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, "lore")).AsT0;
		await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, "rules");

		// Moving the lore page into "rules" would collide with the existing rules page.
		var result = await svc.SetMetadataAsync(lore.Id, "rules", [], true);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That((await svc.GetBySlugAsync("dragons", "lore", WikiNamespace.Main)).AsT0.Id).IsEqualTo(lore.Id);
	}

	[Test]
	public async Task GetBySlugAsync_ExistingPage_ReturnsPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Find Me");

		var result = await svc.GetBySlugAsync("find_me", "general", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Id).IsEqualTo(created.Id);
	}

	[Test]
	public async Task GetBySlugAsync_MissingSlug_ReturnsNotFound()
	{
		var svc = BuildService();
		var result = await svc.GetBySlugAsync("nonexistent", "general", WikiNamespace.Main);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetBySlugAsync_WrongNamespace_ReturnsNotFound()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Ns Test", ns: WikiNamespace.Main);

		var result = await svc.GetBySlugAsync("ns_test", "general", WikiNamespace.Help);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetByIdAsync_ExistingId_ReturnsPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var result = await svc.GetByIdAsync(created.Id);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Id).IsEqualTo(created.Id);
	}

	[Test]
	public async Task GetByIdAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();
		var result = await svc.GetByIdAsync("id_that_does_not_exist");

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task UpdateAsync_ChangesMarkdownAndBumpsRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "Original content.");

		var updateResult = await svc.UpdateAsync(created.Id, "Updated content.", "#2", "v2");

		await Assert.That(updateResult.IsT0).IsTrue();
		var updated = updateResult.AsT0;
		await Assert.That(updated.RevisionNumber).IsEqualTo(2);
		await Assert.That(updated.MarkdownSource).IsEqualTo("Updated content.");
		await Assert.That(updated.LastEditorDbref).IsEqualTo("#2");
	}

	[Test]
	public async Task UpdateAsync_RenderedHtmlReflectsNewContent()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "# Old");

		var updateResult = await svc.UpdateAsync(created.Id, "# New Heading", "#1", "rework");

		await Assert.That(updateResult.IsT0).IsTrue();
		await Assert.That(updateResult.AsT0.RenderedHtml).Contains("New Heading");
	}

	[Test]
	public async Task UpdateAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();

		var result = await svc.UpdateAsync("ghost_id", "content", "#1");

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task DeleteAsync_ExistingPage_ReturnsNoneAndRemovesPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "To Delete");

		var deleteResult = await svc.DeleteAsync(created.Id, "#1");

		await Assert.That(deleteResult.IsT0).IsTrue();
		var getResult = await svc.GetByIdAsync(created.Id);
		await Assert.That(getResult.IsT1).IsTrue();
	}

	[Test]
	public async Task DeleteAsync_SlugBecomesAvailableAfterDeletion()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Reusable Slug");
		await svc.DeleteAsync(created.Id, "#1");

		// Should not return error — slug freed
		var recreated = await CreatePageAsync(svc, title: "Reusable Slug");
		await Assert.That(recreated.Slug).IsEqualTo("reusable_slug");
	}

	[Test]
	public async Task DeleteAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();
		var result = await svc.DeleteAsync("ghost_id", "#1");

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetRevisionsAsync_AfterCreate_HasOneRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var revisions = await svc.GetRevisionsAsync(created.Id);

		await Assert.That(revisions.Count).IsEqualTo(1);
		await Assert.That(revisions[0].RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task GetRevisionsAsync_AfterTwoUpdates_HasThreeRevisions()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "v1");
		await svc.UpdateAsync(created.Id, "v2", "#1");
		await svc.UpdateAsync(created.Id, "v3", "#1");

		// GetRevisionsAsync returns newest first; take=20 covers all
		var revisions = await svc.GetRevisionsAsync(created.Id);

		await Assert.That(revisions.Count).IsEqualTo(3);
	}

	[Test]
	public async Task GetRevisionAsync_ValidRevisionNumber_ReturnsRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "First edition.");

		var revResult = await svc.GetRevisionAsync(created.Id, 1);

		await Assert.That(revResult.IsT0).IsTrue();
		var rev = revResult.AsT0;
		await Assert.That(rev.RevisionNumber).IsEqualTo(1);
		await Assert.That(rev.MarkdownSource).IsEqualTo("First edition.");
	}

	[Test]
	public async Task GetRevisionAsync_MissingRevisionNumber_ReturnsNotFound()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var result = await svc.GetRevisionAsync(created.Id, 99);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task SetProtectionAsync_SetsIsProtectedFlag()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);
		await Assert.That(created.IsProtected).IsFalse();

		var protResult = await svc.SetProtectionAsync(created.Id, true);

		await Assert.That(protResult.IsT0).IsTrue();
		var fetchResult = await svc.GetByIdAsync(created.Id);
		await Assert.That(fetchResult.IsT0).IsTrue();
		await Assert.That(fetchResult.AsT0.IsProtected).IsTrue();
	}

	[Test]
	public async Task SetProtectionAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();

		var result = await svc.SetProtectionAsync("ghost_id", true);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetByNamespaceAsync_ReturnsOnlyPagesInNamespace()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Main One", ns: WikiNamespace.Main);
		await CreatePageAsync(svc, title: "Main Two", ns: WikiNamespace.Main);
		await CreatePageAsync(svc, title: "Help One", ns: WikiNamespace.Help);

		var mainPages = await svc.GetByNamespaceAsync(WikiNamespace.Main);
		var helpPages = await svc.GetByNamespaceAsync(WikiNamespace.Help);

		await Assert.That(mainPages.Count).IsEqualTo(2);
		await Assert.That(helpPages.Count).IsEqualTo(1);
	}

	[Test]
	public async Task GetRecentChangesAsync_ReturnsNewestFirst()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Page A");
		await Task.Delay(5); // ensure different timestamps
		await CreatePageAsync(svc, title: "Page B");

		var recent = await svc.GetRecentChangesAsync(count: 10);

		await Assert.That(recent.Count).IsEqualTo(2);
		await Assert.That(recent[0].Title).IsEqualTo("Page B");
		await Assert.That(recent[1].Title).IsEqualTo("Page A");
	}

	[Test]
	public async Task GetRecentChangesAsync_RespectsCount()
	{
		var svc = BuildService();
		for (var i = 0; i < 5; i++)
			await CreatePageAsync(svc, title: $"Page {i}");

		var recent = await svc.GetRecentChangesAsync(count: 3);

		await Assert.That(recent.Count).IsEqualTo(3);
	}

	/// <summary>
	/// After editing a page via <see cref="IWikiService.UpdateAsync"/>, the next
	/// call to <see cref="IWikiService.GetBySlugAsync"/> must return HTML that
	/// reflects the new content, not the old render.
	/// </summary>
	[Test]
	public async Task WikiCache_EditPage_NextReadGetsFreshRender()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "**original**");

		var before = (await svc.GetBySlugAsync(created.Slug, "general")).AsT0;
		await Assert.That(before.RenderedHtml).Contains("original");

		var updateResult = await svc.UpdateAsync(created.Id, "**updated**", "#1", "edit");
		await Assert.That(updateResult.IsT0).IsTrue();

		var after = (await svc.GetBySlugAsync(created.Slug, "general")).AsT0;
		await Assert.That(after.RenderedHtml).Contains("updated");
		await Assert.That(after.RenderedHtml).DoesNotContain("original");
	}
}
